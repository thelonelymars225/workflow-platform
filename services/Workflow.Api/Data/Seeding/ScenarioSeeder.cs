using Microsoft.EntityFrameworkCore;

namespace Workflow.Api.Data.Seeding;

public sealed record SeedSummary(IReadOnlyList<string> Scenarios, bool Reset, int RowsInserted);

// Development-only scenario seeding: deterministic IDs + ON CONFLICT DO NOTHING keep it idempotent.
public static class ScenarioSeeder
{
    public static readonly IReadOnlyDictionary<string, Action<SeedSet>> Scenarios = new Dictionary<string, Action<SeedSet>>(StringComparer.OrdinalIgnoreCase)
    {
        ["startup"] = StartupScenario.Build,
        ["midsize"] = MidsizeScenario.Build,
        ["enterprise"] = EnterpriseScenario.Build,
        ["agency"] = AgencyScenario.Build,
        ["messy"] = MessyScenario.Build,
    };

    // Org and task tables only; Workflows are never touched.
    private const string ResetSql = """
        TRUNCATE "AutomationRuns", "AutomationTasks", "PersonalTasks", "ActingManagerDelegations", "AccessExceptions",
            "DepartmentMemberships", "Memberships", "Departments", "Users", "Organizations" CASCADE
        """;

    // Accepts a comma-separated list ("startup,messy") or "all"; throws before touching the database if a name is unknown.
    public static IReadOnlyList<string> ParseScenarios(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var names = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Any(x => x.Equals("all", StringComparison.OrdinalIgnoreCase))) return [.. Scenarios.Keys];
        var unknown = names.Where(x => !Scenarios.ContainsKey(x)).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException($"Unknown seed scenario '{string.Join("', '", unknown)}'. Use one of: {string.Join(", ", Scenarios.Keys)}, all.");
        return names.Select(x => x.ToLowerInvariant()).Distinct().ToList();
    }

    public static SeedSet Build(IEnumerable<string> scenarios, DateTimeOffset now)
    {
        var set = new SeedSet(now);
        foreach (var scenario in scenarios) Scenarios[scenario](set);
        return set;
    }

    public static async Task<SeedSummary> RunAsync(WorkflowDbContext db, IReadOnlyList<string> scenarios, bool reset, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var set = Build(scenarios, now);
        var previousTimeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(5));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (reset)
            await db.Database.ExecuteSqlRawAsync(ResetSql, cancellationToken);

        var rows = 0;
        rows += await InsertIgnore.RunAsync(db, set.Organizations, cancellationToken);
        rows += await InsertIgnore.RunAsync(db, set.Users, cancellationToken);
        // Parents before children; the manager foreign key is deferred until commit.
        rows += await InsertIgnore.RunAsync(db, set.Departments.OrderBy(x => x.Depth).ToList(), cancellationToken);
        rows += await InsertIgnore.RunAsync(db, set.Memberships, cancellationToken);
        rows += await InsertIgnore.RunAsync(db, set.DepartmentMemberships, cancellationToken);
        rows += await InsertIgnore.RunAsync(db, set.AccessExceptions, cancellationToken);
        rows += await InsertIgnore.RunAsync(db, set.Delegations, cancellationToken);
        rows += await InsertIgnore.RunAsync(db, set.PersonalTasks, cancellationToken);
        rows += await InsertIgnore.RunAsync(db, set.AutomationTasks, cancellationToken);
        rows += await InsertIgnore.RunAsync(db, set.AutomationRuns.OrderBy(x => x.Attempt).ToList(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        db.Database.SetCommandTimeout(previousTimeout);
        return new SeedSummary(scenarios, reset, rows);
    }
}
