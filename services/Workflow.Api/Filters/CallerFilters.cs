using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Workflow.Api.Domain;
using Workflow.Api.Identity;
using Workflow.Api.Services;

namespace Workflow.Api.Filters;

// Runs after model validation and before the database check, so an unidentified caller gets 401 even without a database.
public sealed class CallerIdentityRequiredFilter(ICallerIdentityProvider identities, IWebHostEnvironment environment,
    ProblemDetailsFactory problems) : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var caller = identities.GetCaller(context.HttpContext);
        if (caller is not null)
        {
            context.HttpContext.Items[typeof(CallerIdentity)] = caller;
            return;
        }
        var detail = environment.IsDevelopment()
            ? $"Send the {DevelopmentHeaderCallerIdentityProvider.UserHeader} and {DevelopmentHeaderCallerIdentityProvider.OrganizationHeader} headers with UUID values."
            : "Authentication is not available yet.";
        context.Result = new ObjectResult(problems.CreateProblemDetails(context.HttpContext, 401, "Caller is not identified", detail: detail))
            { StatusCode = StatusCodes.Status401Unauthorized };
    }

    public void OnActionExecuted(ActionExecutedContext context) { }
}

// Resolves the caller's scope; callers without an active membership in the organization get 403.
public sealed class CallerScopeRequiredFilter(TaskScopeResolver resolver, ProblemDetailsFactory problems) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var caller = (CallerIdentity)context.HttpContext.Items[typeof(CallerIdentity)]!;
        var scope = await resolver.ResolveAsync(caller, context.HttpContext.RequestAborted);
        if (scope is null)
        {
            context.Result = new ObjectResult(problems.CreateProblemDetails(context.HttpContext, 403,
                "No active membership in this organization"))
                { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }
        context.HttpContext.Items[typeof(CallerScope)] = scope;
        await next();
    }
}

public static class CallerHttpContextExtensions
{
    public static CallerScope GetCallerScope(this HttpContext context) => (CallerScope)context.Items[typeof(CallerScope)]!;
}

// Apply to org-scoped controllers: identity (401), then database (503), then membership scope (403).
[AttributeUsage(AttributeTargets.Class)]
public sealed class OrgScopedAttribute : Attribute, IFilterFactory, IOrderedFilter
{
    public bool IsReusable => false;
    public int Order => 0;

    public IFilterMetadata CreateInstance(IServiceProvider services) => new Pipeline(services);

    private sealed class Pipeline(IServiceProvider services) : IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            IActionFilter[] syncFilters =
            [
                services.GetRequiredService<CallerIdentityRequiredFilter>(),
                services.GetRequiredService<WorkflowDatabaseRequiredFilter>()
            ];
            foreach (var filter in syncFilters)
            {
                filter.OnActionExecuting(context);
                if (context.Result is not null) return;
            }
            await services.GetRequiredService<CallerScopeRequiredFilter>().OnActionExecutionAsync(context, next);
        }
    }
}
