namespace Workflow.Api.Models;

public enum PersonalTaskStatus { ToDo, InProgress, Done, Cancelled }

public enum AutomationTaskStatus { Active, Paused }

public enum AutomationRunStatus { Queued, Running, Succeeded, Failed, Retrying }

public class PersonalTask
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public PersonalTaskStatus Status { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class AutomationTask
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public AutomationTaskStatus Status { get; set; }
    public Guid OwnerUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<AutomationRun> Runs { get; set; } = [];
}

public class AutomationRun
{
    public Guid Id { get; set; }
    public Guid AutomationTaskId { get; set; }
    public AutomationRunStatus Status { get; set; }
    // 1 for the first run; a manual retry creates a new run with the next attempt number.
    public int Attempt { get; set; }
    public Guid? RetryOfRunId { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset QueuedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
