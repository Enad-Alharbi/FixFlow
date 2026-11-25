using Microsoft.AspNetCore.Mvc;

namespace FixFlow.Api.Infrastructure.Http;

/// <summary>Base for FixFlow controllers: turns a <see cref="ServiceError"/> into RFC 9457 ProblemDetails with a `code`.</summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected ObjectResult ErrorResult(ServiceError error)
    {
        // The factory already applies the global CustomizeProblemDetails (traceId and a default code by status);
        // the indexer then replaces that default with the specific code. (Problem(extensions:) would Add a duplicate key.)
        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, statusCode: error.Status);
        problem.Extensions["code"] = error.Code;
        if (error.Errors is not null) problem.Extensions["errors"] = error.Errors;
        foreach (var (key, value) in error.Extensions ?? new Dictionary<string, object?>())
            problem.Extensions[key] = value;
        return new ObjectResult(problem) { StatusCode = error.Status, ContentTypes = { "application/problem+json" } };
    }

    /// <summary>200 with the value, or the error as ProblemDetails.</summary>
    protected IActionResult OkOrError<T>(Result<T> result) => result.IsSuccess ? Ok(result.Value) : ErrorResult(result.Error!);
}
