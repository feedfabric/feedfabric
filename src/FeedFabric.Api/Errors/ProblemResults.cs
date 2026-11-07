using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using FeedFabric.Application.Errors;

namespace FeedFabric.Api.Errors;

// Every error is RFC 9457 Problem Details plus "code" (always) and "params" (when values matter)
public static class ProblemResults
{
    public static ObjectResult ApiProblem(this ControllerBase controller, int statusCode, string code, string detail, object? parameters = null) =>
        Create(controller.HttpContext, statusCode, code, detail, parameters);

    public static ObjectResult Create(HttpContext httpContext, int statusCode, string code, string detail, object? parameters = null, object? errors = null)
    {
        var factory = httpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateProblemDetails(httpContext, statusCode, detail: detail);
        problem.Extensions["code"] = code;
        if (parameters is not null)
        {
            problem.Extensions["params"] = parameters;
        }
        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        return new ObjectResult(problem)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" },
        };
    }

    // Fallback code for errors raised outside our own code (auth, routing, exceptions)
    public static string CodeForStatus(int? statusCode) => statusCode switch
    {
        400 => ErrorCodes.BadRequest,
        401 => ErrorCodes.Unauthorized,
        403 => ErrorCodes.Forbidden,
        404 => ErrorCodes.NotFound,
        405 => ErrorCodes.MethodNotAllowed,
        409 => ErrorCodes.Conflict,
        413 => ErrorCodes.RequestTooLarge,
        415 => ErrorCodes.UnsupportedMediaType,
        _ => ErrorCodes.InternalError,
    };
}
