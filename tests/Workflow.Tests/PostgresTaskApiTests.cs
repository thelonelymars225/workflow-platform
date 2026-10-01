using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Workflow.Api.Domain;
using Workflow.Api.Models;

namespace Workflow.Tests;

public class PostgresTaskApiTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase database = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow.TruncateToMicroseconds();
    private readonly Guid org = Guid.NewGuid(), otherOrg = Guid.NewGuid();
    private readonly Guid admin = Guid.NewGuid(), manager = Guid.NewGuid(), member = Guid.NewGuid(), colleague = Guid.NewGuid(),
        gone = Guid.NewGuid(), outsider = Guid.NewGuid();
    private readonly Guid root = Guid.NewGuid(), sales = Guid.NewGuid(), ops = Guid.NewGuid(), otherRoot = Guid.NewGuid();
    private readonly Guid salesTask = Guid.NewGuid(), opsTask = Guid.NewGuid(), memberTask = Guid.NewGuid(), otherOrgTask = Guid.NewGuid();
    private readonly Guid automation = Guid.NewGuid(), failedRun = Guid.NewGuid(), succeededRun = Guid.NewGuid();

    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    // org: root(admin) > sales(manager) , root > ops ; member and colleague work in sales; "gone" is deactivated.
    private async Task SeedAsync()
    {
        await database.MigrateAsync();
        await using var db = database.CreateContext();
        db.Organizations.AddRange(new Organization { Id = org, Name = "Org", Slug = $"o-{org:N}", CreatedAt = Now },
            new Organization { Id = otherOrg, Name = "Other", Slug = $"o-{otherOrg:N}", CreatedAt = Now });
        foreach (var user in new[] { admin, manager, member, colleague, gone, outsider })
            db.Users.Add(new AppUser { Id = user, DisplayName = $"User {user:N}", Email = $"{user:N}@example.test", CreatedAt = Now });
        var rootPath = DepartmentPaths.For(null, root);
        db.Departments.AddRange(
            new Department { Id = root, OrganizationId = org, Name = "Root", Path = rootPath, CreatedAt = Now },
            new Department { Id = sales, OrganizationId = org, ParentId = root, Name = "Sales", Path = DepartmentPaths.For(rootPath, sales), Depth = 1, CreatedAt = Now },
            new Department { Id = ops, OrganizationId = org, ParentId = root, Name = "Ops", Path = DepartmentPaths.For(rootPath, ops), Depth = 1, CreatedAt = Now },
            new Department { Id = otherRoot, OrganizationId = otherOrg, Name = "Other", Path = DepartmentPaths.For(null, otherRoot), CreatedAt = Now });
        Membership M(Guid o, Guid u, OrgRole role, Guid d, bool active = true) => new()
            { Id = Guid.NewGuid(), OrganizationId = o, UserId = u, Role = role, PrimaryDepartmentId = d, IsActive = active, CreatedAt = Now };
        db.Memberships.AddRange(M(org, admin, OrgRole.Admin, root), M(org, manager, OrgRole.Manager, sales), M(org, member, OrgRole.Member, sales),
            M(org, colleague, OrgRole.Member, sales), M(org, gone, OrgRole.Manager, sales, active: false),
            M(otherOrg, outsider, OrgRole.Admin, otherRoot), M(otherOrg, member, OrgRole.Admin, otherRoot));
        PersonalTask T(Guid id, Guid o, Guid d, Guid owner, DateTimeOffset created, PersonalTaskStatus status = PersonalTaskStatus.ToDo, Guid? assignee = null) => new()
            { Id = id, OrganizationId = o, DepartmentId = d, OwnerUserId = owner, AssigneeUserId = assignee, Title = $"Task {id:N}", Status = status, CreatedAt = created, UpdatedAt = created };
        db.PersonalTasks.AddRange(
            T(salesTask, org, sales, colleague, Now.AddMinutes(-3)),
            T(opsTask, org, ops, admin, Now.AddMinutes(-2), PersonalTaskStatus.Done, assignee: member),
            T(memberTask, org, sales, member, Now.AddMinutes(-1), PersonalTaskStatus.InProgress),
            T(otherOrgTask, otherOrg, otherRoot, member, Now));
        for (var i = 0; i < 5; i++)
            db.PersonalTasks.Add(T(Guid.NewGuid(), org, sales, gone, Now.AddHours(-1 - i), PersonalTaskStatus.Cancelled));
        db.AutomationTasks.Add(new AutomationTask { Id = automation, OrganizationId = org, DepartmentId = sales, Name = "Sync", Status = AutomationTaskStatus.Active, OwnerUserId = manager, CreatedAt = Now, UpdatedAt = Now });
        db.AutomationRuns.AddRange(
            new AutomationRun { Id = succeededRun, AutomationTaskId = automation, Status = AutomationRunStatus.Succeeded, Attempt = 1, QueuedAt = Now.AddMinutes(-10) },
            new AutomationRun { Id = failedRun, AutomationTaskId = automation, Status = AutomationRunStatus.Failed, Attempt = 1, Error = "Timeout", QueuedAt = Now.AddMinutes(-5) });
        await db.SaveChangesAsync();
        // Managers point back at memberships, so link them once both rows exist.
        (await db.Departments.SingleAsync(x => x.Id == root)).ManagerUserId = admin;
        (await db.Departments.SingleAsync(x => x.Id == sales)).ManagerUserId = manager;
        await db.SaveChangesAsync();
    }

    private async Task<(WorkflowApiFactory App, HttpClient Client)> StartAsync()
    {
        await SeedAsync();
        var app = new WorkflowApiFactory(database.ConnectionString);
        return (app, app.CreateClient());
    }

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task ListIsScopedPaginatedAndFilterable()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        var adminTasks = await client.As(admin, org).AllTasksAsync();
        Assert.Equal(8, adminTasks.Count);
        Assert.DoesNotContain(adminTasks, x => x.Id == otherOrgTask);

        var firstPage = await (await client.GetAsync("/api/tasks?page=1&pageSize=3")).Read<PageDto<TaskDto>>();
        var thirdPage = await (await client.GetAsync("/api/tasks?page=3&pageSize=3")).Read<PageDto<TaskDto>>();
        Assert.Equal((3, 8), (firstPage.Items.Length, firstPage.TotalCount));
        Assert.Equal(2, thirdPage.Items.Length);
        Assert.Equal(memberTask, firstPage.Items[0].Id); // newest first
        Assert.Equal(5, (await client.AllTasksAsync("Cancelled")).Count);

        var managerTasks = await client.As(manager, org).AllTasksAsync();
        Assert.Equal(7, managerTasks.Count); // all of sales, nothing from ops
        Assert.DoesNotContain(managerTasks, x => x.Id == opsTask);

        var memberTasks = await client.As(member, org).AllTasksAsync();
        Assert.Equal([memberTask, opsTask], memberTasks.Select(x => x.Id));
        Assert.All(memberTasks, x => Assert.Equal("Self", x.Access));
        Assert.True(memberTasks[0].CanEdit);
        Assert.False(memberTasks[1].CanEdit); // assignee without Edit scope

        // The same person is an admin in the other organization, and sees only that organization there.
        Assert.Equal([otherOrgTask], (await client.As(member, otherOrg).AllTasksAsync()).Select(x => x.Id));
    }

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task GetHidesInvisibleAndCrossOrgTasksAs404()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        client.As(member, org);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/tasks/{memberTask}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tasks/{salesTask}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tasks/{otherOrgTask}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tasks/{Guid.NewGuid()}")).StatusCode);

        client.As(outsider, org);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/tasks")).StatusCode);
        client.As(gone, org);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/automation-tasks")).StatusCode);
    }

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task CreateChecksDepartmentScopeAndAssignee()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        client.As(member, org);

        using var created = await client.PostAsJsonAsync("/api/tasks", new { title = "  مراجعة العقد  ", departmentId = sales, assigneeUserId = colleague });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var task = await created.Read<TaskDto>();
        Assert.Equal(("مراجعة العقد", "ToDo", member, colleague), (task.Title, task.Status, task.OwnerUserId, task.AssigneeUserId));
        Assert.Equal(task, await (await client.GetAsync(created.Headers.Location)).Read<TaskDto>() with { NextStatuses = task.NextStatuses });

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/tasks", new { title = "x", departmentId = ops })).StatusCode);
        foreach (var body in new object[]
                 {
                     new { title = "x", departmentId = otherRoot },
                     new { title = "x", departmentId = sales, assigneeUserId = outsider },
                     new { title = "x", departmentId = sales, assigneeUserId = gone },
                     new { title = " ", departmentId = sales },
                     new { title = "x" },
                 })
        {
            using var response = await client.PostAsJsonAsync("/api/tasks", body);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        // The admin has Edit scope everywhere, so may file tasks in any department.
        Assert.Equal(HttpStatusCode.Created, (await client.As(admin, org).PostAsJsonAsync("/api/tasks", new { title = "x", departmentId = ops })).StatusCode);
    }

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task StatusUpdatesFollowLifecycleAndEditScope()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        client.As(member, org);
        using var invalid = await client.SetStatusAsync(memberTask, "ToDo");
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        Assert.Contains("allowed", await invalid.Content.ReadAsStringAsync());
        Assert.Equal("Done", (await (await client.SetStatusAsync(memberTask, "Done")).Read<TaskDto>()).Status);
        Assert.Equal("InProgress", (await (await client.SetStatusAsync(memberTask, "InProgress")).Read<TaskDto>()).Status); // reopen
        Assert.Equal(HttpStatusCode.OK, (await client.SetStatusAsync(memberTask, "InProgress")).StatusCode); // no-op
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SetStatusAsync(memberTask, "Archived")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PatchAsJsonAsync($"/api/tasks/{memberTask}/status", new { status = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SetStatusAsync(opsTask, "Cancelled")).StatusCode); // assignee only
        Assert.Equal(HttpStatusCode.NotFound, (await client.SetStatusAsync(salesTask, "Cancelled")).StatusCode);

        client.As(manager, org);
        Assert.Equal("Cancelled", (await (await client.SetStatusAsync(salesTask, "Cancelled")).Read<TaskDto>()).Status);
        Assert.Equal("ToDo", (await (await client.SetStatusAsync(salesTask, "ToDo")).Read<TaskDto>()).Status); // restore

        await using var db = database.CreateContext();
        Assert.Equal(PersonalTaskStatus.ToDo, (await db.PersonalTasks.SingleAsync(x => x.Id == salesTask)).Status);
    }

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task AutomationTasksAreViewOnlyForMembersAndRetryCreatesNewRun()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;

        client.As(member, org);
        Assert.Empty(await client.AllAutomationTasksAsync()); // members see only their own automation tasks
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/automation-tasks/{automation}/runs/{failedRun}/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/automation-tasks", new { name = "Mine", departmentId = sales })).StatusCode);

        client.As(manager, org);
        var runs = await (await client.GetAsync($"/api/automation-tasks/{automation}/runs")).Read<PageDto<RunDto>>();
        Assert.Equal([failedRun, succeededRun], runs.Items.Select(x => x.Id));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/automation-tasks/{automation}/runs/{succeededRun}/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/automation-tasks/{automation}/runs/{Guid.NewGuid()}/retry", null)).StatusCode);

        Assert.Equal("Paused", (await (await client.PatchAsJsonAsync($"/api/automation-tasks/{automation}/status", new { status = "Paused" })).Read<AutomationTaskDto>()).Status);
        using var paused = await client.PostAsync($"/api/automation-tasks/{automation}/runs/{failedRun}/retry", null);
        Assert.Equal(HttpStatusCode.Conflict, paused.StatusCode);
        Assert.Contains("Resume", (await paused.Read<ProblemDetails>()).Detail);
        await client.PatchAsJsonAsync($"/api/automation-tasks/{automation}/status", new { status = "Active" });

        using var retried = await client.PostAsync($"/api/automation-tasks/{automation}/runs/{failedRun}/retry", null);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        var retry = await retried.Read<RunDto>();
        Assert.Equal(("Queued", 2, (Guid?)failedRun), (retry.Status, retry.Attempt, retry.RetryOfRunId));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/automation-tasks/{automation}/runs/{failedRun}/retry", null)).StatusCode);
        Assert.Equal(3, (await (await client.GetAsync($"/api/automation-tasks/{automation}/runs")).Read<PageDto<RunDto>>()).TotalCount);

        using var created = await client.PostAsJsonAsync("/api/automation-tasks", new { name = "Nightly", departmentId = sales, status = "Paused" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/automation-tasks", new { name = "x", departmentId = ops })).StatusCode);
        Assert.Single(await client.AllAutomationTasksAsync(), x => x.Status == "Paused");
    }

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task DepartmentsListShowsManagerFallback()
    {
        var (app, client) = await StartAsync();
        await using var _ = app;
        var departments = await (await client.As(member, org).GetAsync("/api/departments")).Read<DepartmentDto[]>();
        Assert.Equal(3, departments.Length);
        var opsRow = Assert.Single(departments, x => x.Id == ops);
        Assert.Null(opsRow.ManagerUserId);
        Assert.Equal(admin, opsRow.EffectiveManagerUserId);
        Assert.Equal(manager, Assert.Single(departments, x => x.Id == sales).EffectiveManagerUserId);
    }
}
