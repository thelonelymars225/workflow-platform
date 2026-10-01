using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Workflow.Api.Data;
using Workflow.Api.Domain;
using Workflow.Api.Filters;
using Workflow.Api.Models;
using Workflow.Api.Services;

namespace Workflow.Api.Controllers;

[ApiController]
[OrgScoped]
[Route("api/automation-tasks")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public class AutomationTasksController(WorkflowDbContext db, TaskScopeResolver access, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<AutomationTaskResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<AutomationTaskResponse>>> List(
        [FromQuery] PageQuery paging, [FromQuery] AutomationTaskStatus? status, CancellationToken cancellationToken)
    {
        var scope = HttpContext.GetCallerScope();
        var query = access.VisibleAutomationTasks(scope);
        if (status is { } s) query = query.Where(x => x.Status == s);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((paging.Page - 1) * paging.PageSize).Take(paging.PageSize).ToListAsync(cancellationToken);
        return new PagedResponse<AutomationTaskResponse>(
            rows.Select(x => AutomationTaskResponse.From(x, TaskScopeResolver.Decide(scope, x))).ToList(), paging.Page, paging.PageSize, total);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AutomationTaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AutomationTaskResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var (task, decision) = await FindVisibleAsync(id, tracked: false, cancellationToken);
        return task is null ? NotFoundProblem() : AutomationTaskResponse.From(task, decision);
    }

    [HttpPost]
    [ProducesResponseType<AutomationTaskResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AutomationTaskResponse>> Create(CreateAutomationTaskRequest request, CancellationToken cancellationToken)
    {
        var scope = HttpContext.GetCallerScope();
        if (string.IsNullOrWhiteSpace(request.Name))
            ModelState.AddModelError(nameof(request.Name), "A non-blank name is required.");
        var departmentId = request.DepartmentId!.Value;
        if (!await db.Departments.AnyAsync(x => x.Id == departmentId && x.OrganizationId == scope.OrganizationId, cancellationToken))
            ModelState.AddModelError(nameof(request.DepartmentId), "The department does not exist in this organization.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (!scope.For(departmentId).CanEditAutomation)
            return this.ApiProblem(403, "Only admins and managers in scope can create automation tasks here");

        var now = clock.GetUtcNow().TruncateToMicroseconds();
        var task = new AutomationTask
        {
            Id = Guid.NewGuid(), OrganizationId = scope.OrganizationId, DepartmentId = departmentId, Name = request.Name!.Trim(),
            Description = request.Description?.Trim(), Status = request.Status, OwnerUserId = scope.UserId, CreatedAt = now, UpdatedAt = now
        };
        db.AutomationTasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = task.Id }, AutomationTaskResponse.From(task, TaskScopeResolver.Decide(scope, task)));
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType<AutomationTaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AutomationTaskResponse>> UpdateStatus(Guid id, UpdateAutomationTaskStatusRequest request, CancellationToken cancellationToken)
    {
        var (task, decision) = await FindVisibleAsync(id, tracked: true, cancellationToken);
        if (task is null) return NotFoundProblem();
        if (!decision.CanEdit) return ReadOnlyProblem();
        if (TaskLifecycle.Check(task.Status, request.Status!.Value) == TransitionOutcome.Allowed)
        {
            task.Status = request.Status.Value;
            task.UpdatedAt = clock.GetUtcNow().TruncateToMicroseconds();
            await db.SaveChangesAsync(cancellationToken);
        }
        return AutomationTaskResponse.From(task, decision);
    }

    [HttpGet("{id:guid}/runs")]
    [ProducesResponseType<PagedResponse<AutomationRunResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<AutomationRunResponse>>> ListRuns(Guid id, [FromQuery] PageQuery paging, CancellationToken cancellationToken)
    {
        var (task, _) = await FindVisibleAsync(id, tracked: false, cancellationToken);
        if (task is null) return NotFoundProblem();
        var query = db.AutomationRuns.AsNoTracking().Where(x => x.AutomationTaskId == id);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.QueuedAt).ThenByDescending(x => x.Attempt)
            .Skip((paging.Page - 1) * paging.PageSize).Take(paging.PageSize).ToListAsync(cancellationToken);
        return new PagedResponse<AutomationRunResponse>(rows.Select(AutomationRunResponse.From).ToList(), paging.Page, paging.PageSize, total);
    }

    // A manual retry never mutates the failed run; it queues a new run linked to it.
    [HttpPost("{id:guid}/runs/{runId:guid}/retry")]
    [ProducesResponseType<AutomationRunResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AutomationRunResponse>> Retry(Guid id, Guid runId, CancellationToken cancellationToken)
    {
        var (task, decision) = await FindVisibleAsync(id, tracked: false, cancellationToken);
        if (task is null) return NotFoundProblem();
        var run = await db.AutomationRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId && x.AutomationTaskId == id, cancellationToken);
        if (run is null) return this.ApiProblem(404, "Run not found");
        if (!decision.CanEdit) return ReadOnlyProblem();
        if (!TaskLifecycle.CanRetry(task.Status, run.Status))
            return this.ApiProblem(409, "Run cannot be retried",
                task.Status == AutomationTaskStatus.Paused ? "Resume the automation task before retrying." : $"Only failed runs can be retried; this run is {run.Status}.");
        if (await db.AutomationRuns.AnyAsync(x => x.RetryOfRunId == runId, cancellationToken))
            return this.ApiProblem(409, "Run cannot be retried", "This run has already been retried.");

        var retry = TaskLifecycle.CreateRetry(run, Guid.NewGuid(), clock.GetUtcNow().TruncateToMicroseconds());
        db.AutomationRuns.Add(retry);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/automation-tasks/{id}/runs/{retry.Id}", AutomationRunResponse.From(retry));
    }

    private async Task<(AutomationTask? Task, AccessDecision Decision)> FindVisibleAsync(Guid id, bool tracked, CancellationToken cancellationToken)
    {
        var scope = HttpContext.GetCallerScope();
        var source = tracked ? db.AutomationTasks : db.AutomationTasks.AsNoTracking();
        var task = await source.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (task is null) return (null, AccessDecision.Nothing);
        var decision = TaskScopeResolver.Decide(scope, task);
        return decision.CanView ? (task, decision) : (null, decision);
    }

    private ObjectResult NotFoundProblem() => this.ApiProblem(404, "Automation task not found");

    private ObjectResult ReadOnlyProblem() => this.ApiProblem(403, "You can view this automation task but not edit it");
}
