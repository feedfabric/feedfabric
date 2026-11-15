using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FeedFabric.Api.Auth;
using FeedFabric.Api.Errors;
using FeedFabric.Application.Errors;
using FeedFabric.Application.Services.DataTypes;
using FeedFabric.Application.Services.Users;

namespace FeedFabric.Api.Controllers;

public class DataTypeRequest
{
    [Required(ErrorMessage = ErrorCodes.Required)]
    [MaxLength(DataTypeStore.MaxLabelLength, ErrorMessage = ErrorCodes.TooLong + ";max={1}")]
    public string Label { get; init; } = "";

    [MaxLength(DataTypeStore.MaxDescriptionLength, ErrorMessage = ErrorCodes.TooLong + ";max={1}")]
    public string? Description { get; init; }

    // A JSON Schema whose root is "type": "object"
    public JsonElement Schema { get; init; }
}

// The kinds of structured data posts can carry ("data": { "type", "value" }), each with a JSON Schema the API checks
// every post's data against. Anyone signed in, and API keys, can read them, so apps and SDK type generators can;
// only integration admins change them.
[ApiController]
[Authorize]
public class DataTypesController(DataTypeStore store, ICurrentUser currentUser) : ControllerBase
{
    // GET /data-types
    [HttpGet("data-types")]
    public Task<List<DataTypeResponse>> List(CancellationToken cancellationToken) => store.ListAsync(cancellationToken);

    // GET /data-types/{key}
    [HttpGet("data-types/{key}")]
    public async Task<ActionResult<DataTypeResponse>> Get(string key, CancellationToken cancellationToken) =>
        await store.GetAsync(key, cancellationToken) is { } type ? type : NotFoundProblem(key);

    // GET /data-types/{key}/schema: the schema on its own, for code generators (quicktype, datamodel-codegen…)
    [HttpGet("data-types/{key}/schema")]
    public async Task<IActionResult> Schema(string key, CancellationToken cancellationToken) =>
        await store.GetAsync(key, cancellationToken) is { } type
            ? Content(type.Schema, "application/schema+json")
            : NotFoundProblem(key);

    // PUT /admin/data-types/{key} { "label", "description", "schema" }: creates the type, or saves it again. A changed
    // schema is a new version; posts made before keep their data and the version it was checked against.
    [Authorize(Policy = AdminPolicies.IntegrationAdmin)]
    [HttpPut("admin/data-types/{key}")]
    public async Task<ActionResult<DataTypeResponse>> Save(string key, DataTypeRequest request, CancellationToken cancellationToken)
    {
        var result = await store.SaveAsync(key, request.Label, request.Description, request.Schema, currentUser.ExternalUserId, cancellationToken);
        return result.Status switch
        {
            DataTypeSaveStatus.Ok => result.Type!,
            DataTypeSaveStatus.InvalidKey => this.ApiProblem(StatusCodes.Status400BadRequest, ErrorCodes.DataTypeInvalidKey,
                "Use 2-40 lowercase letters, digits and dashes, starting with a letter.", new { type = key }),
            _ => this.ApiProblem(StatusCodes.Status400BadRequest, ErrorCodes.DataTypeInvalidSchema,
                $"That isn't a schema a post's data can use: {result.Message}", new { reason = result.Message }),
        };
    }

    // DELETE /admin/data-types/{key}: new posts can't use it; posts that have it keep their data
    [Authorize(Policy = AdminPolicies.IntegrationAdmin)]
    [HttpDelete("admin/data-types/{key}")]
    public async Task<IActionResult> Delete(string key, CancellationToken cancellationToken) =>
        await store.DeleteAsync(key, cancellationToken) ? NoContent() : NotFoundProblem(key);

    private ObjectResult NotFoundProblem(string key) =>
        this.ApiProblem(StatusCodes.Status404NotFound, ErrorCodes.DataTypeNotFound, $"There's no data type called '{key}'.", new { type = key });
}
