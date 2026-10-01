using Workflow.Api.Models;

namespace Workflow.Api.Domain;

public enum TransitionOutcome { Allowed, Unchanged, Invalid }

// Allowed state changes for personal tasks, automation tasks, and automation runs.
public static class TaskLifecycle
{
    private static readonly Dictionary<PersonalTaskStatus, PersonalTaskStatus[]> PersonalTransitions = new()
    {
        [PersonalTaskStatus.ToDo] = [PersonalTaskStatus.InProgress, PersonalTaskStatus.Cancelled],
        [PersonalTaskStatus.InProgress] = [PersonalTaskStatus.Done, PersonalTaskStatus.Cancelled],
        [PersonalTaskStatus.Done] = [PersonalTaskStatus.InProgress, PersonalTaskStatus.Cancelled],
        [PersonalTaskStatus.Cancelled] = [PersonalTaskStatus.ToDo],
    };

    private static readonly Dictionary<AutomationRunStatus, AutomationRunStatus[]> RunTransitions = new()
    {
        [AutomationRunStatus.Queued] = [AutomationRunStatus.Running],
        [AutomationRunStatus.Running] = [AutomationRunStatus.Succeeded, AutomationRunStatus.Failed, AutomationRunStatus.Retrying],
        [AutomationRunStatus.Retrying] = [AutomationRunStatus.Running, AutomationRunStatus.Failed],
        [AutomationRunStatus.Succeeded] = [],
        [AutomationRunStatus.Failed] = [],
    };

    public static TransitionOutcome Check(PersonalTaskStatus from, PersonalTaskStatus to) =>
        from == to ? TransitionOutcome.Unchanged
        : PersonalTransitions[from].Contains(to) ? TransitionOutcome.Allowed : TransitionOutcome.Invalid;

    public static IReadOnlyList<PersonalTaskStatus> NextStatuses(PersonalTaskStatus from) => PersonalTransitions[from];

    // Automation tasks toggle freely between Active and Paused.
    public static TransitionOutcome Check(AutomationTaskStatus from, AutomationTaskStatus to) =>
        from == to ? TransitionOutcome.Unchanged : TransitionOutcome.Allowed;

    public static TransitionOutcome Check(AutomationRunStatus from, AutomationRunStatus to) =>
        from == to ? TransitionOutcome.Unchanged
        : RunTransitions[from].Contains(to) ? TransitionOutcome.Allowed : TransitionOutcome.Invalid;

    public static bool IsTerminal(AutomationRunStatus status) =>
        status is AutomationRunStatus.Succeeded or AutomationRunStatus.Failed;

    // A manual retry is only offered for a failed run of an active automation task.
    public static bool CanRetry(AutomationTaskStatus taskStatus, AutomationRunStatus runStatus) =>
        taskStatus == AutomationTaskStatus.Active && runStatus == AutomationRunStatus.Failed;

    public static AutomationRun CreateRetry(AutomationRun failed, Guid newRunId, DateTimeOffset now)
    {
        if (failed.Status != AutomationRunStatus.Failed)
            throw new InvalidOperationException("Only failed runs can be retried.");
        return new AutomationRun
        {
            Id = newRunId, AutomationTaskId = failed.AutomationTaskId, Status = AutomationRunStatus.Queued,
            Attempt = failed.Attempt + 1, RetryOfRunId = failed.Id, QueuedAt = now
        };
    }
}
