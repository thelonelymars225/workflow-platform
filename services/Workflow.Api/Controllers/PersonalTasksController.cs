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
[Route("api/tasks")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public class PersonalTasksController(WorkflowDbContext db, TaskScopeResolver access, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResponse<PersonalTaskResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<PersonalTaskResponse>>> List(
        [FromQuery] PageQuery paging, [FromQuery] PersonalTaskStatus? status, CancellationToken cancellationToken)
    {
        var scope = HttpContext.GetCallerScope();
        var query = access.VisiblePersonalTasks(scope);
        if (status is { } s) query = query.Where(x => x.Status == s);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Skip((paging.Page - 1) * paging.PageSize).Take(paging.PageSize).ToListAsync(cancellationToken);
        return new PagedResponse<PersonalTaskResponse>(
            rows.Select(x => PersonalTaskResponse.From(x, TaskScopeResolver.Decide(scope, x))).ToList(), paging.Page, paging.PageSize, total);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PersonalTaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonalTaskResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var scope = HttpContext.GetCallerScope();
        var task = await db.PersonalTasks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        var decision = task is null ? AccessDecision.Nothing : TaskScopeResolver.Decide(scope, task);
        // Invisible and missing tasks look the same, so task IDs from other scopes or organizations leak nothing.
        if (task is null || !decision.CanView) return this.ApiProblem(statusCode: 404, title: "Task not found");
        return PersonalTaskResponse.From(task, decision);
    }

    [HttpPost]
    [ProducesResponseType<PersonalTaskResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PersonalTaskResponse>> Create(CreatePersonalTaskRequest request, CancellationToken cancellationToken)
    {
        var scope = HttpContext.GetCallerScope();
        if (string.IsNullOrWhiteSpace(request.Title))
            ModelState.AddModelError(nameof(request.Title), "A non-blank title is required.");
        var departmentId = request.DepartmentId!.Value;
        if (!await db.Departments.AnyAsync(x => x.Id == departmentId && x.OrganizationId == scope.OrganizationId, cancellationToken))
            ModelState.AddModelError(nameof(request.DepartmentId), "The department does not exist in this organization.");
        if (request.AssigneeUserId is { } assignee && !await db.Memberships.AnyAsync(
                x => x.OrganizationId == scope.OrganizationId && x.UserId == assignee && x.IsActive, cancellationToken))
            ModelState.AddModelError(nameof(request.AssigneeUserId), "The assignee must be an active member of this organization.");
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        // Anyone may file their own task in a department they belong to; otherwise Edit scope on it is required.
        var department = scope.For(departmentId);
        var belongs = await db.Memberships.AnyAsync(x => x.OrganizationId == scope.OrganizationId && x.UserId == scope.UserId
                && x.PrimaryDepartmentId == departmentId, cancellationToken)
            || await db.DepartmentMemberships.AnyAsync(x => x.DepartmentId == departmentId && x.UserId == scope.UserId, cancellationToken);
        if (!department.CanEditPersonal && !(belongs && department.Reason != AccessReason.Denied))
            return this.ApiProblem(statusCode: 403, title: "You cannot create tasks in this department");

        var now = clock.GetUtcNow().TruncateToMicroseconds();
        var task = new PersonalTask
        {
            Id = Guid.NewGuid(), OrganizationId = scope.OrganizationId, DepartmentId = departmentId,
            Title = request.Title!.Trim(), Description = request.Description?.Trim(), Status = PersonalTaskStatus.ToDo,
            OwnerUserId = scope.UserId, AssigneeUserId = request.AssigneeUserId, DueAt = request.DueAt?.ToUniversalTime().TruncateToMicroseconds(),
            CreatedAt = now, UpdatedAt = now
        };
        db.PersonalTasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = task.Id }, PersonalTaskResponse.From(task, TaskScopeResolver.Decide(scope, task)));
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType<PersonalTaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PersonalTaskResponse>> UpdateStatus(Guid id, UpdatePersonalTaskStatusRequest request, CancellationToken cancellationToken)
    {
        var scope = HttpContext.GetCallerScope();
        var task = await db.PersonalTasks.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        var decision = task is null ? AccessDecision.Nothing : TaskScopeResolver.Decide(scope, task);
        if (task is null || !decision.CanView) return this.ApiProblem(statusCode: 404, title: "Task not found");
        if (!decision.CanEdit) return this.ApiProblem(statusCode: 403, title: "You can view this task but not edit it");

        var target = request.Status!.Value;
        switch (TaskLifecycle.Check(task.Status, target))
        {
            case TransitionOutcome.Invalid:
                return this.ApiProblem(statusCode: 409, title: "Invalid status transition",
                    detail: $"A {task.Status} task cannot move to {target}.",
                    extensions: new Dictionary<string, object?> { ["allowed"] = TaskLifecycle.NextStatuses(task.Status).Select(x => x.ToString()) });
            case TransitionOutcome.Allowed:
                task.Status = target;
                task.UpdatedAt = clock.GetUtcNow().TruncateToMicroseconds();
                await db.SaveChangesAsync(cancellationToken);
                break;
        }
        return PersonalTaskResponse.From(task, decision);
    }

}
