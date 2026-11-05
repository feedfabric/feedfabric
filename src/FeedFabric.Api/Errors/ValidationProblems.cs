using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using FeedFabric.Application.Errors;

namespace FeedFabric.Api.Errors;

public record FieldError(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Field,
    string Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, object>? Params);

// Replaces the default 400 so validation errors carry field codes instead of English sentences
public static class ValidationProblems
{
    private static readonly HashSet<string> FieldCodes = [ErrorCodes.Required, ErrorCodes.OutOfRange, ErrorCodes.InvalidValue];

    public static IActionResult Create(ActionContext context)
    {
        var bodyParameters = context.ActionDescriptor.Parameters
            .Where(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Body)
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var errors = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors.Select(error => ToFieldError(FieldName(entry.Key, bodyParameters), error)))
            .ToList();

        // MVC often adds a whole-body error next to the real field errors; keep one only when no field is named
        var fieldErrors = errors.Where(error => error.Field is not null).Distinct().ToList();
        errors = fieldErrors.Count > 0 ? fieldErrors : errors.Take(1).ToList();

        return ProblemResults.Create(context.HttpContext, StatusCodes.Status400BadRequest,
            ErrorCodes.ValidationFailed, "One or more fields are invalid.", errors: errors);
    }

    // ErrorMessage is "code;name=value;..." from our attributes; anything else (binding or JSON errors) is invalid_value
    private static FieldError ToFieldError(string? field, ModelError error)
    {
        var parts = error.ErrorMessage.Split(';');
        if (!FieldCodes.Contains(parts[0]))
        {
            return new FieldError(field, ErrorCodes.InvalidValue, null);
        }

        var parameters = parts.Length > 1
            ? parts.Skip(1)
                .Select(part => part.Split('=', 2))
                .ToDictionary(pair => pair[0], pair => int.TryParse(pair[1], out var number) ? number : (object)pair[1])
            : null;

        return new FieldError(field, parts[0], parameters);
    }

    // "$.content" (JSON body), "request.Content" or "Limit" (query) become "content" / "limit";
    // the whole body ("", "$" or the body parameter's name) is null
    private static string? FieldName(string key, HashSet<string> bodyParameters)
    {
        if (bodyParameters.Contains(key))
        {
            return null;
        }

        var name = key.StartsWith('$') ? key.TrimStart('$', '.') : key[(key.IndexOf('.') + 1)..];
        return name.Length == 0 ? null : JsonNamingPolicy.CamelCase.ConvertName(name);
    }
}
