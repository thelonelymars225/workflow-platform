using Workflow.Api.Models;

namespace Workflow.Api.Data.Seeding;

internal static class ScenarioHelpers
{
    private static readonly string[] FirstNames =
    [
        "Amelia", "Noah", "Olivia", "Liam", "Ava", "Ethan", "Sofia", "Lucas", "Mia", "Mason", "Aisha", "Omar", "Leila",
        "Hamza", "Mariam", "Yousef", "Priya", "Arjun", "Mei", "Kenji", "Chloe", "Mateo", "Zara", "Ibrahim", "Elena",
        "Diego", "Hana", "Samir", "Grace", "Tomas"
    ];

    private static readonly string[] LastNames =
    [
        "Anderson", "Haddad", "Nguyen", "Okafor", "Rossi", "Kowalski", "Tanaka", "Silva", "Khan", "Müller", "Dubois",
        "Hernández", "Al-Farsi", "Johansson", "O'Brien", "Petrov", "Mensah", "Kaplan", "Lindqvist", "Sato"
    ];

    private static readonly string[] TaskVerbs = ["Review", "Prepare", "Update", "Draft", "Reconcile", "Plan", "Audit", "Follow up on", "Finalize", "Schedule"];
    private static readonly string[] TaskObjects =
    [
        "quarterly report", "client brief", "budget forecast", "onboarding checklist", "vendor contract", "release notes",
        "risk register", "training plan", "invoice batch", "campaign assets", "support backlog", "hiring pipeline"
    ];

    public static string PersonName(int index) =>
        $"{FirstNames[index % FirstNames.Length]} {LastNames[(index / FirstNames.Length + index * 7) % LastNames.Length]}";

    public static string TaskTitle(Random random) => $"{TaskVerbs[random.Next(TaskVerbs.Length)]} {TaskObjects[random.Next(TaskObjects.Length)]}";

    // Weighted so every status appears, with most work in progress or done.
    public static PersonalTaskStatus TaskStatus(Random random) => random.Next(100) switch
    {
        < 30 => PersonalTaskStatus.ToDo,
        < 55 => PersonalTaskStatus.InProgress,
        < 90 => PersonalTaskStatus.Done,
        _ => PersonalTaskStatus.Cancelled
    };

    public static AutomationRunStatus RunStatus(Random random) => random.Next(100) switch
    {
        < 62 => AutomationRunStatus.Succeeded,
        < 80 => AutomationRunStatus.Failed,
        < 88 => AutomationRunStatus.Running,
        < 94 => AutomationRunStatus.Queued,
        _ => AutomationRunStatus.Retrying
    };

    // Adds runs for an automation task; some failed runs get a manual retry run linked through RetryOfRunId.
    public static void AddRuns(SeedSet set, string key, Guid automationTask, int count, Random random, DateTimeOffset start)
    {
        for (var i = 0; i < count; i++)
        {
            var status = RunStatus(random);
            var queued = start.AddHours(i * 6 + random.Next(6));
            var run = set.Run($"{key}/{i}", automationTask, status, 1, queued);
            if (status == AutomationRunStatus.Failed && random.Next(100) < 40)
                set.Run($"{key}/{i}/retry", automationTask, random.Next(2) == 0 ? AutomationRunStatus.Succeeded : AutomationRunStatus.Queued,
                    2, queued.AddMinutes(30), retryOf: run);
        }
    }
}
