using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Workflow.Api.Data.Seeding;
using static Workflow.Api.Data.Seeding.MessyFixtures;

namespace Workflow.Tests.Scenarios;

// One test per messy quirk, all through the HTTP API with the Development demo headers.
[Trait("Category", "PostgreSQL")]
public class MessyScenarioTests(MessyFixture fixture) : IClassFixture<MessyFixture>
{
    private HttpClient As(Guid user) => fixture.As(user, Organization);

    private async Task<HashSet<Guid>> VisibleTo(Guid user) => (await As(user).AllTasksAsync()).Select(t => t.Id).ToHashSet();

    private async Task<HttpStatusCode> GetStatus(Guid user, Guid task)
    {
        using var client = As(user);
        return (await client.GetAsync($"/api/tasks/{task}")).StatusCode;
    }

    private async Task<TaskDto> Get(Guid user, Guid task)
    {
        using var client = As(user);
        using var response = await client.GetAsync($"/api/tasks/{task}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Read<TaskDto>();
    }

    private async Task<DepartmentDto[]> DepartmentsAs(Guid user)
    {
        using var client = As(user);
        return await (await client.GetAsync("/api/departments")).Read<DepartmentDto[]>();
    }

    [PostgresFact]
    public async Task M1_TwoOperationsDepartmentsAtDifferentLevelsStaySeparate()
    {
        var operations = (await DepartmentsAs(People.Ceo)).Where(d => d.Name == "Operations").ToList();
        Assert.Equal(2, operations.Count);
        Assert.Equal([1, 2], operations.Select(d => d.Depth).Order());
        Assert.Equal(Departments.Sales, operations.Single(d => d.Depth == 2).ParentId);

        var opsDirector = await VisibleTo(People.OperationsDirector);
        Assert.Contains(Tasks.TopOperations, opsDirector);
        Assert.DoesNotContain(Tasks.SalesOperations, opsDirector);
        var salesOpsLead = await VisibleTo(People.SalesOperationsLead);
        Assert.Contains(Tasks.SalesOperations, salesOpsLead);
        Assert.DoesNotContain(Tasks.TopOperations, salesOpsLead);
        Assert.Contains(Tasks.SalesOperations, await VisibleTo(People.SalesDirector));
    }

    [PostgresFact]
    public async Task M2_DepartmentWithoutManagerFallsBackToParentManager()
    {
        var retail = Assert.Single(await DepartmentsAs(People.RetailMember), d => d.Id == Departments.SalesRetail);
        Assert.Null(retail.ManagerUserId);
        Assert.Equal(People.SalesDirector, retail.EffectiveManagerUserId);

        var task = await Get(People.SalesDirector, Tasks.Retail);
        Assert.True(task.CanEdit);
        Assert.Equal("Role", task.Access);
    }

    [PostgresFact]
    public async Task M3_OneManagerOfTwoBranchesSeesBothAndNothingBetween()
    {
        var visible = await VisibleTo(People.DualManager);
        Assert.Contains(Tasks.Procurement, visible);
        Assert.Contains(Tasks.MetroLine3, visible);
        Assert.DoesNotContain(Tasks.TopOperations, visible);   // parent of Procurement
        Assert.DoesNotContain(Tasks.ContractorMessy, visible); // Riyadh, an ancestor of Metro Line 3
        Assert.Equal(fixture.TasksUnder(Departments.Procurement, Departments.ProjectsMetroLine3).Union(fixture.OwnOrAssigned(Organization, People.DualManager)).ToHashSet(), visible);
        var departments = await DepartmentsAs(People.DualManager);
        Assert.Equal(2, departments.Count(d => d.ManagerUserId == People.DualManager));
    }

    [PostgresFact]
    public async Task M4_ActingManagerCoversLeaveAndExpiredDelegationGrantsNothing()
    {
        var hrTask = await Get(People.ActingHrManager, Tasks.HumanResources);
        Assert.Equal("ActingManager", hrTask.Access);
        Assert.True(hrTask.CanEdit);
        using (var acting = As(People.ActingHrManager))
        {
            Assert.Equal(HttpStatusCode.OK, (await acting.SetStatusAsync(Tasks.HumanResources, "Cancelled")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await acting.SetStatusAsync(Tasks.HumanResources, "ToDo")).StatusCode);
            var payroll = (await acting.AllAutomationTasksAsync()).Single(a => a.Name == "Payroll export");
            Assert.True(payroll.CanEdit);
        }
        Assert.Equal(HttpStatusCode.NotFound, await GetStatus(People.ExpiredActingHrManager, Tasks.HumanResources));
        Assert.Equal(fixture.OwnOrAssigned(Organization, People.ExpiredActingHrManager), await VisibleTo(People.ExpiredActingHrManager));
        // The manager on leave keeps their own scope.
        Assert.Equal("Role", (await Get(People.HrManagerOnLeave, Tasks.HumanResources)).Access);
    }

    [PostgresFact]
    public async Task M5_ConfidentialDepartmentDeniesParentManagerAndAdmin()
    {
        Assert.Equal(HttpStatusCode.NotFound, await GetStatus(People.LegalDirector, Tasks.Investigations));
        Assert.Equal(HttpStatusCode.OK, await GetStatus(People.LegalDirector, Tasks.Legal));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatus(People.AuditorAdmin, Tasks.Investigations));
        var auditor = await VisibleTo(People.AuditorAdmin);
        Assert.Contains(Tasks.Sales, auditor);
        Assert.Contains(Tasks.Legal, auditor);
        Assert.Empty(auditor.Intersect(fixture.TasksUnder(Departments.LegalInvestigations)));
        Assert.Equal(HttpStatusCode.OK, await GetStatus(People.Ceo, Tasks.Investigations));
        Assert.Equal(HttpStatusCode.OK, await GetStatus(People.InvestigationsLead, Tasks.Investigations));
    }

    [PostgresFact]
    public async Task M6_ViewOnlyGrantOnSalesAllowsViewingButNotEditing()
    {
        using var analyst = As(People.FinanceAnalyst);
        var visible = await analyst.AllTasksAsync();
        var sales = fixture.TasksUnder(Departments.Sales);
        Assert.Equal(sales.Union(fixture.OwnOrAssigned(Organization, People.FinanceAnalyst)).ToHashSet(), visible.Select(t => t.Id).ToHashSet());
        Assert.All(visible.Where(t => sales.Contains(t.Id)), t => Assert.Equal(("Grant", false), (t.Access, t.CanEdit)));

        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.SetStatusAsync(Tasks.Sales, "Done")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await analyst.SetStatusAsync(Tasks.Retail, "Cancelled")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await analyst.PostAsync($"/api/automation-tasks/{Tasks.SalesAutomation}/runs/{Tasks.SalesAutomationFailedRun}/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await analyst.PostAsJsonAsync("/api/tasks", new { title = "Not allowed", departmentId = Departments.Sales })).StatusCode);
        Assert.Equal("InProgress", (await Get(People.SalesDirector, Tasks.Sales)).Status);
    }

    [PostgresFact]
    public async Task M7_DenyBeatsGrantOnSameDepartment()
    {
        Assert.Equal(HttpStatusCode.NotFound, await GetStatus(People.GrantAndDenyUser, Tasks.HumanResources));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatus(People.GrantAndDenyUser, Tasks.GrantAndDenyHr));
        Assert.Empty((await VisibleTo(People.GrantAndDenyUser)).Intersect(fixture.TasksUnder(Departments.HumanResources)));
    }

    [PostgresFact]
    public async Task M8_ContractorInTwoOrganizationsSeesNothingCrossOrg()
    {
        using var inMessy = fixture.As(People.Contractor, Organization);
        var messyTasks = await inMessy.AllTasksAsync();
        Assert.Equal([Tasks.ContractorMessy], messyTasks.Select(t => t.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await inMessy.GetAsync($"/api/tasks/{Tasks.ContractorAgency}")).StatusCode);

        using var inAgency = fixture.As(People.Contractor, AgencyScenario.AgencyOrg);
        var agencyTasks = await inAgency.AllTasksAsync();
        Assert.Contains(Tasks.ContractorAgency, agencyTasks.Select(t => t.Id));
        Assert.All(agencyTasks, t => Assert.Equal(AgencyScenario.AgencyOrg, t.OrganizationId));
        Assert.Equal(HttpStatusCode.NotFound, (await inAgency.GetAsync($"/api/tasks/{Tasks.ContractorMessy}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await inAgency.SetStatusAsync(Tasks.ContractorMessy, "Done")).StatusCode);

        // Different roles: plain member in messy, manager of a team in the agency.
        Assert.DoesNotContain((await DepartmentsAs(People.Contractor)), d => d.ManagerUserId == People.Contractor);
        var agencyDepartments = await (await inAgency.GetAsync("/api/departments")).Read<DepartmentDto[]>();
        Assert.Contains(agencyDepartments, d => d.Id == Departments.AgencyFieldDelivery && d.ManagerUserId == People.Contractor);
        Assert.Equal(HttpStatusCode.Forbidden, (await fixture.As(People.Contractor, AgencyScenario.ClientOrg("acme")).GetAsync("/api/tasks")).StatusCode);
    }

    [PostgresFact]
    public async Task M9_DeactivatedMemberSeesNothingButTasksStayVisibleToManagersAbove()
    {
        using var deactivated = As(People.Deactivated);
        Assert.Equal(HttpStatusCode.Forbidden, (await deactivated.GetAsync("/api/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await deactivated.GetAsync($"/api/tasks/{Tasks.DeactivatedOwned1}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await deactivated.GetAsync("/api/departments")).StatusCode);

        foreach (var manager in new[] { People.SalesDirector, People.Ceo })
        {
            var visible = await VisibleTo(manager);
            Assert.Contains(Tasks.DeactivatedOwned1, visible);
            Assert.Contains(Tasks.DeactivatedOwned2, visible);
        }
        Assert.Equal(People.Deactivated, (await Get(People.SalesDirector, Tasks.DeactivatedOwned1)).OwnerUserId);
    }

    [PostgresFact]
    public async Task M10_MovedOwnersOldTasksStayWithOldDepartmentAndItsManager()
    {
        var old = await Get(People.OperationsDirector, Tasks.MoverOld1);
        Assert.Equal(Departments.Operations, old.DepartmentId);
        var opsDirector = await VisibleTo(People.OperationsDirector);
        Assert.Contains(Tasks.MoverOld2, opsDirector);
        Assert.DoesNotContain(Tasks.MoverNew, opsDirector);

        var financeDirector = await VisibleTo(People.FinanceDirector);
        Assert.Contains(Tasks.MoverNew, financeDirector);
        Assert.DoesNotContain(Tasks.MoverOld1, financeDirector);
        Assert.DoesNotContain(Tasks.MoverOld2, financeDirector);

        Assert.Equal(new HashSet<Guid> { Tasks.MoverOld1, Tasks.MoverOld2, Tasks.MoverNew }, await VisibleTo(People.Mover));
        await using var db = fixture.CreateContext();
        Assert.Equal(Departments.Finance, (await db.Memberships.SingleAsync(m => m.UserId == People.Mover)).PrimaryDepartmentId);
    }

    [PostgresFact]
    public async Task M11_TaskAssignedOutsideItsDepartmentIsVisibleToTheAssignee()
    {
        var task = await Get(People.OutsideAssignee, Tasks.AssignedOutsideDepartment);
        Assert.Equal(("Self", false, Departments.Legal), (task.Access, task.CanEdit, task.DepartmentId));
        Assert.Contains(Tasks.AssignedOutsideDepartment, await VisibleTo(People.OutsideAssignee));
        // The assignee's own manager gains nothing from the assignment.
        Assert.Equal(HttpStatusCode.NotFound, await GetStatus(People.ProjectsDirector, Tasks.AssignedOutsideDepartment));
    }

    [PostgresFact]
    public async Task M12_ManagerWhoIsPlainMemberInSiblingDepartmentSeesOnlyOwnTasksThere()
    {
        var visible = await VisibleTo(People.SalesOperationsLead);
        var retail = fixture.TasksUnder(Departments.SalesRetail);
        Assert.Equal(new HashSet<Guid> { Tasks.SalesOpsLeadOwnRetail }, visible.Intersect(retail).ToHashSet());
        Assert.Equal(HttpStatusCode.NotFound, await GetStatus(People.SalesOperationsLead, Tasks.Retail));
        Assert.Equal(HttpStatusCode.NotFound, await GetStatus(People.SalesOperationsLead, Tasks.DeactivatedOwned1));
        Assert.True(fixture.TasksUnder(Departments.SalesOperations).IsSubsetOf(visible));
    }

    [PostgresFact]
    public async Task M13_SixLevelBranchIsVisibleFromItsTop()
    {
        var departments = await DepartmentsAs(People.ProjectsDirector);
        var metro = Assert.Single(departments, d => d.Id == Departments.ProjectsMetroLine3);
        Assert.Equal(6, metro.Depth);
        Assert.Equal(People.DualManager, metro.EffectiveManagerUserId);
        var infrastructure = Assert.Single(departments, d => d.Id == Departments.ProjectsInfrastructure);
        Assert.Equal(People.ProjectsDirector, infrastructure.EffectiveManagerUserId); // 4 levels of fallback

        var visible = await VisibleTo(People.ProjectsDirector);
        Assert.Contains(Tasks.MetroLine3, visible);
        Assert.True(fixture.TasksUnder(Departments.Projects).IsSubsetOf(visible));
    }

    [PostgresFact]
    public async Task M14_EmptyDepartmentHasNoPeopleOrTasksAndStillResolvesAManager()
    {
        var archive = Assert.Single(await DepartmentsAs(People.Ceo), d => d.Id == Departments.Archive);
        Assert.Null(archive.ManagerUserId);
        Assert.Equal(People.Ceo, archive.EffectiveManagerUserId);
        Assert.DoesNotContain(await As(People.Ceo).AllTasksAsync(), t => t.DepartmentId == Departments.Archive);
        await using var db = fixture.CreateContext();
        Assert.False(await db.Memberships.AnyAsync(m => m.PrimaryDepartmentId == Departments.Archive));
        Assert.False(await db.DepartmentMemberships.AnyAsync(m => m.DepartmentId == Departments.Archive));
    }

    [PostgresFact]
    public async Task M15_DeniedUserStillSeesTheirOwnTaskInsideTheDeniedDepartment()
    {
        var own = await Get(People.LegalDirector, Tasks.InvestigationsOwnedByLegalDirector);
        Assert.Equal(("Self", true), (own.Access, own.CanEdit));
        var visible = await VisibleTo(People.LegalDirector);
        Assert.Equal(new HashSet<Guid> { Tasks.InvestigationsOwnedByLegalDirector }, visible.Intersect(fixture.TasksUnder(Departments.LegalInvestigations)).ToHashSet());
    }

    [PostgresFact]
    public async Task M16_MixedScriptLongAndDuplicateNamesAreKeptApart()
    {
        await using var db = fixture.CreateContext();
        var sameName = await db.Users.Where(u => u.DisplayName == People.SharedName).Select(u => u.Id).ToListAsync();
        Assert.Equal(new[] { People.SameNameOperations, People.SameNameSales }.Order(), sameName.Order());
        var longName = await db.Users.SingleAsync(u => u.Id == People.LongName);
        Assert.Equal(People.LongDisplayName, longName.DisplayName);
        Assert.True(longName.DisplayName.Length > 150);
        Assert.Equal(OrganizationName, (await db.Organizations.SingleAsync(o => o.Id == Organization)).Name);

        var first = await VisibleTo(People.SameNameOperations);
        var second = await VisibleTo(People.SameNameSales);
        Assert.Contains(Tasks.SameNameOperations, first);
        Assert.DoesNotContain(Tasks.SameNameSales, first);
        Assert.Contains(Tasks.SameNameSales, second);
        Assert.DoesNotContain(Tasks.SameNameOperations, second);

        var longTask = await Get(People.LongName, Tasks.LongName);
        Assert.Equal(200, longTask.Title.Length);
        Assert.Contains("تنسيق", longTask.Title);
        Assert.Contains("المحطة", (await Get(People.LongName, Tasks.MetroLine3)).Title);
    }
}
