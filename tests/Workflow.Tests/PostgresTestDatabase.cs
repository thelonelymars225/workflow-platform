using Microsoft.EntityFrameworkCore;
using Npgsql;
using Workflow.Api.Data;

namespace Workflow.Tests;

// Creates and drops one randomly named database; the supplied WORKFLOW_TEST_POSTGRES database is never touched.
public sealed class PostgresTestDatabase : IAsyncLifetime
{
    private readonly string databaseName = "workflow_tests_" + Guid.NewGuid().ToString("N");
    private readonly string? adminConnection = Environment.GetEnvironmentVariable("WORKFLOW_TEST_POSTGRES");
    private bool databaseCreated;

    public string ConnectionString { get; private set; } = "";

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(adminConnection)) return;
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
        await command.ExecuteNonQueryAsync();
        databaseCreated = true;
        ConnectionString = new NpgsqlConnectionStringBuilder(adminConnection)
        {
            Database = databaseName,
            Pooling = false
        }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        if (!databaseCreated) return;
        await using var admin = new NpgsqlConnection(adminConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE \"{databaseName}\" WITH (FORCE)", admin);
        await command.ExecuteNonQueryAsync();
    }

    public WorkflowDbContext CreateContext() => new(new DbContextOptionsBuilder<WorkflowDbContext>()
        .UseNpgsql(ConnectionString).Options);

    public async Task MigrateAsync()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}

// One organization with one department and one admin member, for tests that need valid task foreign keys.
public sealed record MinimalOrg(Guid OrganizationId, Guid DepartmentId, Guid UserId)
{
    public static async Task<MinimalOrg> CreateAsync(WorkflowDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var org = new MinimalOrg(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        db.Organizations.Add(new() { Id = org.OrganizationId, Name = "Test org", Slug = "test-" + org.OrganizationId.ToString("N"), CreatedAt = now });
        db.Users.Add(new() { Id = org.UserId, DisplayName = "Test user", Email = $"{org.UserId:N}@example.test", CreatedAt = now });
        db.Departments.Add(new()
        {
            Id = org.DepartmentId, OrganizationId = org.OrganizationId, Name = "Root",
            Path = Workflow.Api.Domain.DepartmentPaths.For(null, org.DepartmentId), CreatedAt = now
        });
        db.Memberships.Add(new()
        {
            Id = Guid.NewGuid(), OrganizationId = org.OrganizationId, UserId = org.UserId, Role = Workflow.Api.Models.OrgRole.Admin,
            IsActive = true, PrimaryDepartmentId = org.DepartmentId, CreatedAt = now
        });
        await db.SaveChangesAsync();
        return org;
    }
}
