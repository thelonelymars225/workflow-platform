using Microsoft.EntityFrameworkCore;
using Workflow.Api.Domain;
using Workflow.Api.Models;

namespace Workflow.Tests;

public class PostgresTaskModelTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase database = new();

    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task PersistsTasksRunsAndRetryChainWithStringStatuses()
    {
        await database.MigrateAsync();
        var now = DateTimeOffset.UtcNow.TruncateToMicroseconds();
        MinimalOrg org;
        await using (var setup = database.CreateContext()) org = await MinimalOrg.CreateAsync(setup);
        var owner = org.UserId;
        var personal = new PersonalTask
        {
            Id = Guid.NewGuid(), OrganizationId = org.OrganizationId, DepartmentId = org.DepartmentId, Title = "مهمة شخصية", Status = PersonalTaskStatus.InProgress,
            OwnerUserId = owner, CreatedAt = now, UpdatedAt = now
        };
        var automation = new AutomationTask
        {
            Id = Guid.NewGuid(), OrganizationId = org.OrganizationId, DepartmentId = org.DepartmentId, Name = "Nightly sync", Status = AutomationTaskStatus.Active,
            OwnerUserId = owner, CreatedAt = now, UpdatedAt = now
        };
        var failed = new AutomationRun
        {
            Id = Guid.NewGuid(), AutomationTaskId = automation.Id, Status = AutomationRunStatus.Failed,
            Attempt = 1, Error = "Timeout", QueuedAt = now, StartedAt = now, FinishedAt = now
        };
        await using (var db = database.CreateContext())
        {
            db.AddRange(personal, automation, failed);
            await db.SaveChangesAsync();
            db.AutomationRuns.Add(TaskLifecycle.CreateRetry(failed, Guid.NewGuid(), now.AddMinutes(1)));
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var savedTask = await db.PersonalTasks.SingleAsync();
            Assert.Equal(PersonalTaskStatus.InProgress, savedTask.Status);
            Assert.Equal("مهمة شخصية", savedTask.Title);
            var runs = await db.AutomationRuns.OrderBy(x => x.Attempt).ToListAsync();
            Assert.Equal([1, 2], runs.Select(x => x.Attempt));
            Assert.Equal(failed.Id, runs[1].RetryOfRunId);
            Assert.Equal(AutomationRunStatus.Queued, runs[1].Status);

            var raw = await db.Database.SqlQueryRaw<string>("SELECT \"Status\" AS \"Value\" FROM \"PersonalTasks\"").SingleAsync();
            Assert.Equal("InProgress", raw);

            // Deleting the automation task removes its whole retry chain.
            db.AutomationTasks.Remove(await db.AutomationTasks.SingleAsync());
            await db.SaveChangesAsync();
            Assert.Empty(await db.AutomationRuns.ToListAsync());
        }
    }

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task CheckConstraintsRejectUnknownStatusesAndAttempts()
    {
        await database.MigrateAsync();
        await using var db = database.CreateContext();
        var org = await MinimalOrg.CreateAsync(db);
        var (o, d, u) = (org.OrganizationId, org.DepartmentId, org.UserId);
        var id = Guid.NewGuid();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "PersonalTasks" ("Id", "OrganizationId", "DepartmentId", "Title", "Status", "OwnerUserId", "CreatedAt", "UpdatedAt")
            VALUES ({id}, {o}, {d}, {"Bad"}, {"Archived"}, {u}, now(), now())
            """));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AutomationTasks" ("Id", "OrganizationId", "DepartmentId", "Name", "Status", "OwnerUserId", "CreatedAt", "UpdatedAt")
            VALUES ({id}, {o}, {d}, {"Ok"}, {"Active"}, {u}, now(), now())
            """);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "AutomationRuns" ("Id", "AutomationTaskId", "Status", "Attempt", "QueuedAt")
            VALUES ({Guid.NewGuid()}, {id}, {"Queued"}, {0}, now())
            """));
    }
}
