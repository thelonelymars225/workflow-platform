using System.Net;
using Workflow.Api.Data.Seeding;
using Workflow.Api.Models;

namespace Workflow.Tests.Scenarios;

[Trait("Category", "PostgreSQL")]
public class StartupScenarioTests(StartupFixture fixture) : IClassFixture<StartupFixture>
{
    private readonly Guid org = SeedIds.Of("org/startup");

    [PostgresFact]
    public async Task AdminSeesAllTasksAndMembersSeeOnlyTheirOwn()
    {
        var founder = SeedIds.Of("user/startup/founder");
        var all = await fixture.As(founder, org).AllTasksAsync();
        Assert.Equal(25, all.Count);
        Assert.All(all, t => Assert.True(t.CanEdit));

        foreach (var member in Enumerable.Range(1, 4).Select(i => SeedIds.Of($"user/startup/member-{i}")))
        {
            var visible = (await fixture.As(member, org).AllTasksAsync()).Select(t => t.Id).ToHashSet();
            Assert.Equal(fixture.OwnOrAssigned(org, member), visible);
        }
    }

    [PostgresFact]
    public async Task MembersOnlyViewAutomationTheyDoNotOwn()
    {
        var member = SeedIds.Of("user/startup/member-1");
        Assert.Empty(await fixture.As(member, org).AllAutomationTasksAsync());
        Assert.Equal(2, (await fixture.As(SeedIds.Of("user/startup/founder"), org).AllAutomationTasksAsync()).Count);
    }
}

[Trait("Category", "PostgreSQL")]
public class MidsizeScenarioTests(MidsizeFixture fixture) : IClassFixture<MidsizeFixture>
{
    private readonly Guid org = SeedIds.Of("org/midsize");

    [PostgresFact]
    public async Task EachManagerSeesExactlyTheirDepartment()
    {
        foreach (var name in MidsizeScenario.DepartmentNames)
        {
            var slug = name.ToLowerInvariant();
            var manager = SeedIds.Of($"user/midsize/{slug}/manager");
            var visible = (await fixture.As(manager, org).AllTasksAsync()).Select(t => t.Id).ToHashSet();
            var expected = fixture.TasksUnder(SeedIds.Of($"dept/midsize/{slug}"));
            expected.UnionWith(fixture.OwnOrAssigned(org, manager));
            Assert.Equal(expected, visible);
            Assert.Equal(80, fixture.TasksUnder(SeedIds.Of($"dept/midsize/{slug}")).Count);
        }
    }

    [PostgresFact]
    public async Task CeoSeesEverythingAndStatusFilterMatches()
    {
        using var ceo = fixture.As(SeedIds.Of("user/midsize/ceo"), org);
        Assert.Equal(400, (await ceo.AllTasksAsync()).Count);
        foreach (var status in Enum.GetValues<PersonalTaskStatus>())
        {
            var filtered = await ceo.AllTasksAsync(status.ToString());
            Assert.Equal(fixture.Set.PersonalTasks.Count(t => t.Status == status), filtered.Count);
            Assert.All(filtered, t => Assert.Equal(status.ToString(), t.Status));
        }
    }
}

[Trait("Category", "PostgreSQL")]
public class EnterpriseScenarioTests(EnterpriseFixture fixture) : IClassFixture<EnterpriseFixture>
{
    private readonly Guid org = SeedIds.Of("org/enterprise");

    [PostgresFact]
    public async Task AdminPagesThroughAllTasksWithoutDuplicates()
    {
        var all = await fixture.As(SeedIds.Of("user/enterprise/admin-1"), org).AllTasksAsync();
        Assert.Equal(EnterpriseScenario.PersonalTaskCount, all.Count);
        Assert.Equal(all.Count, all.Select(t => t.Id).Distinct().Count());
    }

    [PostgresFact]
    public async Task ManagersAtEveryLevelSeeTheirSubtree()
    {
        string[] departments = ["enterprise/Technology", "enterprise/Technology/Central", "enterprise/Technology/Central/Alpha",
            "enterprise/Technology/Central/Alpha/squad"];
        var previous = int.MaxValue;
        foreach (var key in departments)
        {
            var department = SeedIds.Of($"dept/{key}");
            var manager = fixture.Set.GetDepartment(department).ManagerUserId!.Value;
            var visible = (await fixture.As(manager, org).AllTasksAsync()).Select(t => t.Id).ToHashSet();
            var expected = fixture.TasksUnder(department);
            expected.UnionWith(fixture.OwnOrAssigned(org, manager));
            Assert.Equal(expected, visible);
            Assert.True(fixture.TasksUnder(department).Count < previous);
            previous = fixture.TasksUnder(department).Count;
        }
    }

    [PostgresFact]
    public async Task MemberSeesOnlyOwnedOrAssigned()
    {
        var member = SeedIds.Of("user/enterprise/member-7");
        var visible = (await fixture.As(member, org).AllTasksAsync()).Select(t => t.Id).ToHashSet();
        Assert.NotEmpty(visible);
        Assert.Equal(fixture.OwnOrAssigned(org, member), visible);
    }

    [PostgresFact]
    public async Task AutomationRunsCoverEveryOutcome()
    {
        using var admin = fixture.As(SeedIds.Of("user/enterprise/admin-0"), org);
        var automations = await admin.AllAutomationTasksAsync();
        Assert.Equal(EnterpriseScenario.AutomationTaskCount, automations.Count);
        var withRetry = fixture.Set.AutomationRuns.First(r => r.RetryOfRunId is not null);
        var runs = await (await admin.GetAsync($"/api/automation-tasks/{withRetry.AutomationTaskId}/runs?pageSize=200")).Read<PageDto<RunDto>>();
        Assert.Contains(runs.Items, r => r.RetryOfRunId == withRetry.RetryOfRunId && r.Attempt == 2);
    }
}

[Trait("Category", "PostgreSQL")]
public class AgencyScenarioTests(AgencyFixture fixture) : IClassFixture<AgencyFixture>
{
    [PostgresFact]
    public async Task SharedStaffHaveDifferentScopesPerOrganization()
    {
        var checkedSomeone = false;
        foreach (var group in fixture.Set.Memberships.GroupBy(m => m.UserId).Where(g => g.Select(m => m.Role).Distinct().Count() > 1))
        {
            foreach (var membership in group)
            {
                var visible = await fixture.As(group.Key, membership.OrganizationId).AllTasksAsync();
                Assert.All(visible, t => Assert.Equal(membership.OrganizationId, t.OrganizationId));
                var orgTasks = fixture.Set.PersonalTasks.Count(t => t.OrganizationId == membership.OrganizationId);
                if (membership.Role == OrgRole.Admin) Assert.Equal(orgTasks, visible.Count);
                if (membership.Role == OrgRole.Member)
                    Assert.Equal(fixture.OwnOrAssigned(membership.OrganizationId, group.Key), visible.Select(t => t.Id).ToHashSet());
            }
            checkedSomeone = true;
        }
        Assert.True(checkedSomeone);
    }

    [PostgresFact]
    public async Task ClientAdminCannotEnterTheAgencyOrOtherClients()
    {
        var acmeAdmin = SeedIds.Of("user/agency-client/acme/admin");
        Assert.Equal(40, (await fixture.As(acmeAdmin, AgencyScenario.ClientOrg("acme")).AllTasksAsync()).Count);
        foreach (var other in new[] { AgencyScenario.AgencyOrg, AgencyScenario.ClientOrg("nimbus") })
            Assert.Equal(HttpStatusCode.Forbidden, (await fixture.As(acmeAdmin, other).GetAsync("/api/tasks")).StatusCode);
        var nimbusTask = fixture.Set.PersonalTasks.First(t => t.OrganizationId == AgencyScenario.ClientOrg("nimbus"));
        Assert.Equal(HttpStatusCode.NotFound, (await fixture.As(acmeAdmin, AgencyScenario.ClientOrg("acme")).GetAsync($"/api/tasks/{nimbusTask.Id}")).StatusCode);
    }
}
