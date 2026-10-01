using Workflow.Api.Data;
using Workflow.Api.Data.Seeding;

namespace Workflow.Tests.Scenarios;

// Seeds one scenario into a fresh database once per test class and starts the API against it.
public abstract class ScenarioFixture(params string[] scenarios) : IAsyncLifetime
{
    private readonly PostgresTestDatabase database = new();
    private WorkflowApiFactory? app;

    public DateTimeOffset SeededAt { get; } = DateTimeOffset.UtcNow;
    public SeedSet Set { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Set = ScenarioSeeder.Build(scenarios, SeededAt);
        await database.InitializeAsync();
        if (string.IsNullOrEmpty(database.ConnectionString)) return; // [PostgresFact] skips the tests
        await database.MigrateAsync();
        await using (var db = database.CreateContext())
            await ScenarioSeeder.RunAsync(db, scenarios, reset: false, SeededAt);
        app = new WorkflowApiFactory(database.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (app is not null) await app.DisposeAsync();
        await database.DisposeAsync();
    }

    // A new client per call so headers never leak between callers.
    public HttpClient As(Guid user, Guid organization) => app!.CreateClient().As(user, organization);

    public WorkflowDbContext CreateContext() => database.CreateContext();

    public Guid OrgBySlug(string slug) => Set.Organizations.Single(x => x.Slug == slug).Id;

    // Independent of AccessPolicy: tasks whose department lies under any of the given roots.
    public HashSet<Guid> TasksUnder(params Guid[] roots)
    {
        var paths = roots.Select(r => Set.GetDepartment(r).Path).ToList();
        return Set.PersonalTasks.Where(t => paths.Any(p => Set.GetDepartment(t.DepartmentId).Path.StartsWith(p, StringComparison.Ordinal)))
            .Select(t => t.Id).ToHashSet();
    }

    public HashSet<Guid> OwnOrAssigned(Guid organization, Guid user) =>
        Set.PersonalTasks.Where(t => t.OrganizationId == organization && (t.OwnerUserId == user || t.AssigneeUserId == user))
            .Select(t => t.Id).ToHashSet();
}

public sealed class StartupFixture() : ScenarioFixture("startup");
public sealed class MidsizeFixture() : ScenarioFixture("midsize");
public sealed class EnterpriseFixture() : ScenarioFixture("enterprise");
public sealed class AgencyFixture() : ScenarioFixture("agency");
public sealed class MessyFixture() : ScenarioFixture("messy");
