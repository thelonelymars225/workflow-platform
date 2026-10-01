using Workflow.Api.Domain;
using Workflow.Api.Models;
using static Workflow.Api.Models.PersonalTaskStatus;

namespace Workflow.Tests;

public class TaskLifecycleTests
{
    public static TheoryData<PersonalTaskStatus, PersonalTaskStatus> AllowedPersonal() => new()
    {
        { ToDo, InProgress }, { InProgress, Done },
        { ToDo, Cancelled }, { InProgress, Cancelled }, { Done, Cancelled },
        { Done, InProgress }, // reopen
        { Cancelled, ToDo },  // restore
    };

    public static TheoryData<PersonalTaskStatus, PersonalTaskStatus> InvalidPersonal() => new()
    {
        { ToDo, Done }, { InProgress, ToDo }, { Done, ToDo },
        { Cancelled, InProgress }, { Cancelled, Done },
    };

    [Theory]
    [MemberData(nameof(AllowedPersonal))]
    public void PersonalTaskAllowsDesignedTransitions(PersonalTaskStatus from, PersonalTaskStatus to) =>
        Assert.Equal(TransitionOutcome.Allowed, TaskLifecycle.Check(from, to));

    [Theory]
    [MemberData(nameof(InvalidPersonal))]
    public void PersonalTaskRejectsOtherTransitions(PersonalTaskStatus from, PersonalTaskStatus to) =>
        Assert.Equal(TransitionOutcome.Invalid, TaskLifecycle.Check(from, to));

    [Fact]
    public void EveryPersonalPairIsClassified()
    {
        var statuses = Enum.GetValues<PersonalTaskStatus>();
        var allowed = AllowedPersonal().Select(row => ((PersonalTaskStatus)row[0], (PersonalTaskStatus)row[1])).ToHashSet();
        foreach (var from in statuses)
        foreach (var to in statuses)
        {
            var expected = from == to ? TransitionOutcome.Unchanged
                : allowed.Contains((from, to)) ? TransitionOutcome.Allowed : TransitionOutcome.Invalid;
            Assert.Equal(expected, TaskLifecycle.Check(from, to));
        }
    }

    [Fact]
    public void AnyStatusCanBeCancelled()
    {
        foreach (var from in Enum.GetValues<PersonalTaskStatus>().Where(s => s != Cancelled))
            Assert.Contains(Cancelled, TaskLifecycle.NextStatuses(from));
    }

    [Fact]
    public void AutomationTaskTogglesBetweenActiveAndPaused()
    {
        Assert.Equal(TransitionOutcome.Allowed, TaskLifecycle.Check(AutomationTaskStatus.Active, AutomationTaskStatus.Paused));
        Assert.Equal(TransitionOutcome.Allowed, TaskLifecycle.Check(AutomationTaskStatus.Paused, AutomationTaskStatus.Active));
        Assert.Equal(TransitionOutcome.Unchanged, TaskLifecycle.Check(AutomationTaskStatus.Paused, AutomationTaskStatus.Paused));
    }

    [Theory]
    [InlineData(AutomationRunStatus.Queued, AutomationRunStatus.Running, TransitionOutcome.Allowed)]
    [InlineData(AutomationRunStatus.Running, AutomationRunStatus.Succeeded, TransitionOutcome.Allowed)]
    [InlineData(AutomationRunStatus.Running, AutomationRunStatus.Failed, TransitionOutcome.Allowed)]
    [InlineData(AutomationRunStatus.Running, AutomationRunStatus.Retrying, TransitionOutcome.Allowed)]
    [InlineData(AutomationRunStatus.Retrying, AutomationRunStatus.Running, TransitionOutcome.Allowed)]
    [InlineData(AutomationRunStatus.Retrying, AutomationRunStatus.Failed, TransitionOutcome.Allowed)]
    [InlineData(AutomationRunStatus.Queued, AutomationRunStatus.Succeeded, TransitionOutcome.Invalid)]
    [InlineData(AutomationRunStatus.Succeeded, AutomationRunStatus.Running, TransitionOutcome.Invalid)]
    [InlineData(AutomationRunStatus.Failed, AutomationRunStatus.Running, TransitionOutcome.Invalid)]
    [InlineData(AutomationRunStatus.Failed, AutomationRunStatus.Queued, TransitionOutcome.Invalid)]
    public void AutomationRunTransitions(AutomationRunStatus from, AutomationRunStatus to, TransitionOutcome expected) =>
        Assert.Equal(expected, TaskLifecycle.Check(from, to));

    [Theory]
    [InlineData(AutomationTaskStatus.Active, AutomationRunStatus.Failed, true)]
    [InlineData(AutomationTaskStatus.Paused, AutomationRunStatus.Failed, false)]
    [InlineData(AutomationTaskStatus.Active, AutomationRunStatus.Succeeded, false)]
    [InlineData(AutomationTaskStatus.Active, AutomationRunStatus.Running, false)]
    [InlineData(AutomationTaskStatus.Active, AutomationRunStatus.Retrying, false)]
    [InlineData(AutomationTaskStatus.Active, AutomationRunStatus.Queued, false)]
    public void ManualRetryOnlyForFailedRunsOfActiveTasks(AutomationTaskStatus task, AutomationRunStatus run, bool expected) =>
        Assert.Equal(expected, TaskLifecycle.CanRetry(task, run));

    [Fact]
    public void ManualRetryCreatesNewQueuedRunLinkedToFailedRun()
    {
        var failed = new AutomationRun
        {
            Id = Guid.NewGuid(), AutomationTaskId = Guid.NewGuid(), Status = AutomationRunStatus.Failed, Attempt = 2
        };
        var now = DateTimeOffset.UtcNow;
        var retry = TaskLifecycle.CreateRetry(failed, Guid.NewGuid(), now);

        Assert.NotEqual(failed.Id, retry.Id);
        Assert.Equal(failed.AutomationTaskId, retry.AutomationTaskId);
        Assert.Equal(AutomationRunStatus.Queued, retry.Status);
        Assert.Equal(3, retry.Attempt);
        Assert.Equal(failed.Id, retry.RetryOfRunId);
        Assert.Equal(now, retry.QueuedAt);
        Assert.Equal(AutomationRunStatus.Failed, failed.Status);
    }

    [Fact]
    public void ManualRetryOfNonFailedRunThrows()
    {
        var run = new AutomationRun { Id = Guid.NewGuid(), Status = AutomationRunStatus.Succeeded, Attempt = 1 };
        Assert.Throws<InvalidOperationException>(() => TaskLifecycle.CreateRetry(run, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void TruncatesToPostgresMicroseconds()
    {
        var value = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(3)).AddTicks(1234567);
        var truncated = value.TruncateToMicroseconds();
        Assert.Equal(TimeSpan.Zero, truncated.Offset);
        Assert.Equal(0, truncated.UtcTicks % 10);
        Assert.Equal(value.UtcTicks - 7, truncated.UtcTicks);
    }
}
