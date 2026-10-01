using System.Net;
using Microsoft.EntityFrameworkCore;
using Workflow.Api.Data.Seeding;
using Workflow.Api.Domain;
using Workflow.Api.Models;

namespace Workflow.Tests;

public class ScenarioBuildTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SeedIdsAreDeterministicVersion5Guids()
    {
        var id = SeedIds.Of("user/messy/ceo");
        Assert.Equal(id, SeedIds.Of("user/messy/ceo"));
        Assert.NotEqual(id, SeedIds.Of("user/messy/CEO"));
        Assert.Equal('5', id.ToString()[14]);
        Assert.Contains(id.ToString()[19], "89ab");
    }

    [Theory]
    [InlineData("messy", new[] { "messy" })]
    [InlineData(" Startup , messy ", new[] { "startup", "messy" })]
    [InlineData("all", new[] { "startup", "midsize", "enterprise", "agency", "messy" })]
    [InlineData("", new string[0])]
    public void ParsesScenarioNames(string value, string[] expected) => Assert.Equal(expected, ScenarioSeeder.ParseScenarios(value));

    [Fact]
    public void UnknownScenarioNameListsValidNames()
    {
        var error = Assert.Throws<ArgumentException>(() => ScenarioSeeder.ParseScenarios("messy,bogus"));
        Assert.Contains("'bogus'", error.Message);
        Assert.Contains("enterprise", error.Message);
    }

    [Fact]
    public void BuildingTwiceYieldsIdenticalRows()
    {
        var first = ScenarioSeeder.Build(["enterprise", "messy"], Now);
        var second = ScenarioSeeder.Build(["enterprise", "messy"], Now);
        Assert.Equal(first.PersonalTasks.Select(x => (x.Id, x.Title, x.Status, x.DepartmentId, x.OwnerUserId, x.CreatedAt)),
            second.PersonalTasks.Select(x => (x.Id, x.Title, x.Status, x.DepartmentId, x.OwnerUserId, x.CreatedAt)));
        Assert.Equal(first.AutomationRuns.Select(x => (x.Id, x.Status)), second.AutomationRuns.Select(x => (x.Id, x.Status)));
        Assert.Equal(first.PersonalTasks.Count, first.PersonalTasks.Select(x => x.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("startup", 5, 5, 0)]
    [InlineData("midsize", 51, 51, 1)]
    [InlineData("enterprise", 300, 300, 4)]
    [InlineData("messy", 45, 78, 6)] // messy also seeds the agency (30 staff + 3 client admins) for M8
    public void ScenarioShapes(string scenario, int people, int users, int maxDepth)
    {
        var set = ScenarioSeeder.Build([scenario], Now);
        var org = set.Organizations.Single(x => x.Slug == (scenario == "messy" ? "al-noor" : scenario)).Id;
        Assert.Equal(people, set.Memberships.Count(x => x.OrganizationId == org));
        Assert.Equal(users, set.Users.Count);
        Assert.Equal(maxDepth, set.Departments.Where(x => x.OrganizationId == org).Max(x => x.Depth));
        // The root department is managed by an admin in every scenario.
        var rootManager = set.Departments.Single(d => d.OrganizationId == org && d.ParentId == null).ManagerUserId;
        Assert.Contains(set.Memberships, x => x.OrganizationId == org && x.Role == OrgRole.Admin && x.UserId == rootManager);
    }

    [Fact]
    public void EnterpriseHasThousandsOfTasksInEveryStatusAndEveryRunOutcome()
    {
        var set = ScenarioSeeder.Build(["enterprise"], Now);
        Assert.Equal(EnterpriseScenario.PersonalTaskCount, set.PersonalTasks.Count);
        Assert.Equal(Enum.GetValues<PersonalTaskStatus>(), set.PersonalTasks.Select(x => x.Status).Distinct().Order());
        Assert.Equal(Enum.GetValues<AutomationRunStatus>(), set.AutomationRuns.Select(x => x.Status).Distinct().Order());
        Assert.Equal(Enum.GetValues<AutomationTaskStatus>(), set.AutomationTasks.Select(x => x.Status).Distinct().Order());
        Assert.Contains(set.AutomationRuns, x => x.RetryOfRunId is not null);
        Assert.True(set.Departments.Count >= 50);
    }

    [Fact]
    public void AgencyStaffHoldDifferentRolesAcrossOrganizations()
    {
        var set = ScenarioSeeder.Build(["agency"], Now);
        Assert.Equal(4, set.Organizations.Count);
        var multiOrg = set.Memberships.GroupBy(x => x.UserId).Where(g => g.Count() > 1).ToList();
        Assert.True(multiOrg.Count >= 20);
        Assert.Contains(multiOrg, g => g.Select(x => x.Role).Distinct().Count() > 1);
        Assert.All(set.Memberships, m => Assert.Equal(m.OrganizationId, set.GetDepartment(m.PrimaryDepartmentId).OrganizationId));
    }

    [Fact]
    public void EveryReferenceStaysInsideItsOrganization()
    {
        var set = ScenarioSeeder.Build(ScenarioSeeder.Scenarios.Keys, Now);
        var members = set.Memberships.Select(x => (x.OrganizationId, x.UserId)).ToHashSet();
        foreach (var department in set.Departments)
        {
            if (department.ParentId is { } parent) Assert.Equal(department.OrganizationId, set.GetDepartment(parent).OrganizationId);
            if (department.ManagerUserId is { } manager) Assert.Contains((department.OrganizationId, manager), members);
            Assert.Equal(DepartmentPaths.DepthOf(department.Path), department.Depth);
        }
        foreach (var task in set.PersonalTasks)
        {
            Assert.Equal(task.OrganizationId, set.GetDepartment(task.DepartmentId).OrganizationId);
            Assert.Contains((task.OrganizationId, task.OwnerUserId), members);
            if (task.AssigneeUserId is { } assignee) Assert.Contains((task.OrganizationId, assignee), members);
            Assert.InRange(task.Title.Length, 1, 200);
        }
        Assert.All(set.Users, user => Assert.InRange(user.DisplayName.Length, 1, 400));
    }

    [Fact]
    public void MessyDelegationWindowsAreRelativeToSeedTime()
    {
        var set = ScenarioSeeder.Build(["messy"], Now);
        Assert.Single(set.Delegations, x => x.IsActiveAt(Now));
        var expired = Assert.Single(set.Delegations, x => !x.IsActiveAt(Now));
        Assert.True(expired.EndsAt < Now && expired.EndsAt > Now.AddDays(-45));
    }
}

public class PostgresScenarioSeedTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase database = new();

    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task SeedingIsIdempotentAndResetOnlyClearsOrgData()
    {
        await database.MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        await using var db = database.CreateContext();
        await db.Database.ExecuteSqlRawAsync("""INSERT INTO "Workflows" ("Id", "Name", "CreatedAt", "UpdatedAt") VALUES (gen_random_uuid(), 'Keep me', now(), now())""");

        var first = await ScenarioSeeder.RunAsync(db, ScenarioSeeder.ParseScenarios("all"), reset: false, now);
        var expected = ScenarioSeeder.Build(ScenarioSeeder.ParseScenarios("all"), now);
        Assert.Equal(expected.PersonalTasks.Count, await db.PersonalTasks.CountAsync());
        Assert.Equal(expected.AutomationRuns.Count, await db.AutomationRuns.CountAsync());
        Assert.True(first.RowsInserted > 10_000);

        // A second run (also with an overlapping scenario list) inserts nothing and changes nothing.
        await db.PersonalTasks.Where(x => x.Id == MessyFixtures.Tasks.Legal).ExecuteUpdateAsync(x => x.SetProperty(t => t.Title, "Edited locally"));
        Assert.Equal(0, (await ScenarioSeeder.RunAsync(db, ["messy", "agency"], reset: false, now)).RowsInserted);
        Assert.Equal("Edited locally", (await db.PersonalTasks.AsNoTracking().SingleAsync(x => x.Id == MessyFixtures.Tasks.Legal)).Title);

        // Reset + one scenario leaves exactly that scenario (plus its agency dependency) and keeps workflows.
        var reset = await ScenarioSeeder.RunAsync(db, ["startup"], reset: true, now);
        Assert.Equal(ScenarioSeeder.Build(["startup"], now).PersonalTasks.Count, await db.PersonalTasks.CountAsync());
        Assert.Equal(["startup"], await db.Organizations.Select(x => x.Slug).ToListAsync());
        Assert.Equal(1, await db.Workflows.CountAsync());
        Assert.True(reset.RowsInserted > 0);
    }

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task StartupFlagsSeedInDevelopmentAndAreIgnoredElsewhere()
    {
        await database.MigrateAsync();
        var settings = new Dictionary<string, string?> { ["Development:SeedScenario"] = "startup", ["Development:ResetData"] = "true" };

        await using (var production = new WorkflowApiFactory(database.ConnectionString, environment: "Production", settings: settings))
        {
            using var client = production.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        }
        await using (var db = database.CreateContext()) Assert.Equal(0, await db.Organizations.CountAsync());

        for (var restart = 0; restart < 2; restart++)
        {
            await using var app = new WorkflowApiFactory(database.ConnectionString, settings: settings);
            using var client = app.CreateClient();
            var founder = SeedIds.Of("user/startup/founder");
            var tasks = await client.As(founder, SeedIds.Of("org/startup")).AllTasksAsync();
            Assert.Equal(25, tasks.Count);
        }
    }
}
