using Microsoft.AspNetCore.Mvc;

namespace Workflow.Api.Controllers;

public static class ApiProblems
{
    public static ObjectResult ApiProblem(this ControllerBase controller, int statusCode, string title, string? detail = null,
        IDictionary<string, object?>? extensions = null)
    {
        var problem = controller.ProblemDetailsFactory.CreateProblemDetails(controller.HttpContext, statusCode, title, detail: detail);
        if (extensions is not null) foreach (var (key, value) in extensions) problem.Extensions[key] = value;
        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}
