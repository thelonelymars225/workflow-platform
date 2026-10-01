using System.ComponentModel.DataAnnotations;
using Workflow.Api.Domain;

namespace Workflow.Api.Models;

public sealed class PageQuery
{
    [Range(1, int.MaxValue)]
    public int Page { get; init; } = 1;

    [Range(1, 200)]
    public int PageSize { get; init; } = 50;
}

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed class CreatePersonalTaskRequest
{
    [Required, StringLength(200), NoNullCharacters]
    public string? Title { get; init; }

    [StringLength(2000), NoNullCharacters]
    public string? Description { get; init; }

    [Required]
    public Guid? DepartmentId { get; init; }

    public Guid? AssigneeUserId { get; init; }
    public DateTimeOffset? DueAt { get; init; }
}

public sealed class CreateAutomationTaskRequest
{
    [Required, StringLength(200), NoNullCharacters]
    public string? Name { get; init; }

    [StringLength(2000), NoNullCharacters]
    public string? Description { get; init; }

    [Required]
    public Guid? DepartmentId { get; init; }

    public AutomationTaskStatus Status { get; init; } = AutomationTaskStatus.Active;
}

public sealed class UpdatePersonalTaskStatusRequest
{
    [Required]
    public PersonalTaskStatus? Status { get; init; }
}

public sealed class UpdateAutomationTaskStatusRequest
{
    [Required]
    public AutomationTaskStatus? Status { get; init; }
}

public sealed record PersonalTaskResponse(
    Guid Id, Guid OrganizationId, Guid DepartmentId, string Title, string? Description, PersonalTaskStatus Status,
    Guid OwnerUserId, Guid? AssigneeUserId, DateTimeOffset? DueAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    bool CanEdit, AccessReason Access, IReadOnlyList<PersonalTaskStatus> NextStatuses)
{
    public static PersonalTaskResponse From(PersonalTask task, AccessDecision access) => new(
        task.Id, task.OrganizationId, task.DepartmentId, task.Title, task.Description, task.Status, task.OwnerUserId,
        task.AssigneeUserId, task.DueAt, task.CreatedAt, task.UpdatedAt, access.CanEdit, access.Reason,
        access.CanEdit ? TaskLifecycle.NextStatuses(task.Status) : []);
}

public sealed record AutomationTaskResponse(
    Guid Id, Guid OrganizationId, Guid DepartmentId, string Name, string? Description, AutomationTaskStatus Status,
    Guid OwnerUserId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool CanEdit, AccessReason Access)
{
    public static AutomationTaskResponse From(AutomationTask task, AccessDecision access) => new(
        task.Id, task.OrganizationId, task.DepartmentId, task.Name, task.Description, task.Status, task.OwnerUserId,
        task.CreatedAt, task.UpdatedAt, access.CanEdit, access.Reason);
}

public sealed record AutomationRunResponse(
    Guid Id, Guid AutomationTaskId, AutomationRunStatus Status, int Attempt, Guid? RetryOfRunId, string? Error,
    DateTimeOffset QueuedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt)
{
    public static AutomationRunResponse From(AutomationRun run) => new(
        run.Id, run.AutomationTaskId, run.Status, run.Attempt, run.RetryOfRunId, run.Error, run.QueuedAt, run.StartedAt, run.FinishedAt);
}

public sealed record DepartmentResponse(
    Guid Id, Guid? ParentId, string Name, string Path, int Depth, Guid? ManagerUserId, Guid? EffectiveManagerUserId);
