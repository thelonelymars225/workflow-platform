using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Workflow.Api.Models;

namespace Workflow.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WORKFLOW_TEST_POSTGRES")))
            Skip = "Set WORKFLOW_TEST_POSTGRES to a PostgreSQL test connection with CREATE DATABASE permission.";
    }
}

public class PostgresWorkflowTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase database = new();
    private string ConnectionString => database.ConnectionString;

    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task MigrationCrudRestartAndRepeatedStartupSeedPersistData()
    {
        await MigrateAsync();
        WorkflowResponse saved;
        await using (var app = new WorkflowApiFactory(ConnectionString))
        {
            using var client = app.CreateClient();
            Assert.Empty((await client.GetFromJsonAsync<WorkflowResponse[]>("/api/workflows"))!);
            using var ready = await client.GetAsync("/health/db");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

            using var created = await client.PostAsJsonAsync("/api/workflows", new { name = "  سير عمل 🚀  ", description = "  Persistent  " });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            saved = (await created.Content.ReadFromJsonAsync<WorkflowResponse>())!;
            Assert.NotEqual(Guid.Empty, saved.Id);
            Assert.Equal("سير عمل 🚀", saved.Name);
            Assert.Equal("Persistent", saved.Description);
            Assert.Equal(saved.CreatedAt, saved.UpdatedAt);
            Assert.Equal(TimeSpan.Zero, saved.CreatedAt.Offset);
            Assert.NotNull(created.Headers.Location);
            Assert.EndsWith("/api/workflows/" + saved.Id, created.Headers.Location.ToString());
            Assert.Equal(saved, await client.GetFromJsonAsync<WorkflowResponse>(created.Headers.Location));

            using var missing = await client.GetAsync("/api/workflows/" + Guid.NewGuid());
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }

        WorkflowResponse? firstSeed = null;
        for (var restart = 0; restart < 2; restart++)
        {
            await using var app = new WorkflowApiFactory(ConnectionString, seed: true);
            using var client = app.CreateClient();
            var rows = (await client.GetFromJsonAsync<WorkflowResponse[]>("/api/workflows"))!;
            Assert.Equal(2, rows.Length);
            Assert.Contains(saved, rows);
            var seed = Assert.Single(rows, row => row.Id == Guid.Parse("65f64362-d031-4dca-a7af-85991e7a08c0"));
            if (firstSeed is not null) Assert.Equal(firstSeed, seed);
            firstSeed = seed;
        }
    }

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task RejectsNullCharactersWithoutWritingRowsAndAcceptsBoundaryLengths()
    {
        await MigrateAsync();
        await using var app = new WorkflowApiFactory(ConnectionString);
        using var client = app.CreateClient();
        foreach (var (name, description, field) in new[]
        {
            ("bad\0name", "valid", "Name"),
            ("valid", "bad\0description", "Description")
        })
        {
            using var response = await client.PostAsJsonAsync("/api/workflows", new { name, description });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
            Assert.Contains(field, problem!.Errors.Keys);
        }
        Assert.Empty((await client.GetFromJsonAsync<WorkflowResponse[]>("/api/workflows"))!);

        using var boundary = await client.PostAsJsonAsync("/api/workflows", new { name = new string('n', 200), description = new string('d', 2000) });
        Assert.Equal(HttpStatusCode.Created, boundary.StatusCode);
        using var optional = await client.PostAsJsonAsync("/api/workflows", new { name = "Optional description" });
        Assert.Equal(HttpStatusCode.Created, optional.StatusCode);
        Assert.Null((await optional.Content.ReadFromJsonAsync<WorkflowResponse>())!.Description);
        Assert.Equal(2, (await client.GetFromJsonAsync<WorkflowResponse[]>("/api/workflows"))!.Length);
    }

    private Task MigrateAsync() => database.MigrateAsync();
}
