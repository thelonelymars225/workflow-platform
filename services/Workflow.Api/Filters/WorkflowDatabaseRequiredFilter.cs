using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Workflow.Api.Data;

namespace Workflow.Api.Filters;

// The default action-filter order runs after ApiController's automatic model validation.
public sealed class WorkflowDatabaseRequiredFilter(WorkflowDbContext db, ProblemDetailsFactory problems) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (!string.IsNullOrWhiteSpace(db.Database.GetConnectionString())) return;

        context.Result = new ObjectResult(problems.CreateProblemDetails(context.HttpContext,
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Database is not configured",
            detail: "Set ConnectionStrings:WorkflowDatabase on the API. See the backend README."))
        { StatusCode = StatusCodes.Status503ServiceUnavailable };
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}
