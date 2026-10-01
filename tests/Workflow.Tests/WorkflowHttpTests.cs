using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Workflow.Tests;

public class WorkflowHttpTests
{
    public const string UnreachableDatabase =
        "Host=127.0.0.1;Port=1;Database=workflow_tests;Username=review;Password=review-only-secret;Timeout=1";

    public static TheoryData<string, string, string> InvalidRequests()
    {
        var cases = new TheoryData<string, string, string>();
        foreach (var connection in new[] { "", UnreachableDatabase })
        {
            cases.Add(connection, "{}", "Name");
            cases.Add(connection, "{\"name\":null}", "Name");
            cases.Add(connection, "{\"name\":\" \"}", "Name");
            cases.Add(connection, "{\"name\":\"bad\\u0000name\"}", "Name");
            cases.Add(connection, "{\"name\":\"valid\",\"description\":\"bad\\u0000description\"}", "Description");
            cases.Add(connection, "{\"name\":\"" + new string('n', 201) + "\"}", "Name");
            cases.Add(connection, "{\"name\":\"valid\",\"description\":\"" + new string('d', 2001) + "\"}", "Description");
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidFieldsReturn400BeforeDatabaseChecks(string connection, string json, string field)
    {
        await using var app = new WorkflowApiFactory(connection);
        using var client = app.CreateClient();
        using var response = await client.PostAsync("/api/workflows", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(field, problem.Errors.Keys);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    public async Task InvalidJsonReturns400WithoutDatabase(string json)
    {
        await using var app = new WorkflowApiFactory("");
        using var client = app.CreateClient();
        using var response = await client.PostAsync("/api/workflows", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(UnreachableDatabase)]
    public async Task DatabaseOperationsReturnSafe503WhileLivenessStaysAvailable(string connection)
    {
        await using var app = new WorkflowApiFactory(connection);
        using var client = app.CreateClient();
        var expectedTitle = connection.Length == 0 ? "Database is not configured" : "Database is unavailable";

        foreach (var path in new[] { "/health", "/api/health" })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        foreach (var path in new[] { "/health/db", "/api/workflows", "/api/workflows/65f64362-d031-4dca-a7af-85991e7a08c0" })
        {
            using var response = await client.GetAsync(path);
            await AssertSafeUnavailable(response, expectedTitle);
        }
        using var create = await client.PostAsJsonAsync("/api/workflows", new { name = "Valid workflow" });
        await AssertSafeUnavailable(create, expectedTitle);
    }

    [Fact]
    public async Task UnknownWorkflowRouteReturns404WithoutDatabase()
    {
        await using var app = new WorkflowApiFactory("");
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/api/workflows/not-a-guid");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task AssertSafeUnavailable(HttpResponseMessage response, string expectedTitle)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("review-only-secret", body);
        Assert.DoesNotContain("Host=", body);
        Assert.DoesNotContain("Exception", body);
        Assert.DoesNotContain(" at ", body);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(503, problem.Status);
        Assert.Equal(expectedTitle, problem.Title);
    }
}

public class TaskIdentityHttpTests
{
    private static readonly string[] Paths = ["/api/tasks", "/api/automation-tasks", "/api/departments", $"/api/tasks/{Guid.NewGuid()}"];

    [Theory]
    [InlineData("")]
    [InlineData(WorkflowHttpTests.UnreachableDatabase)]
    public async Task MissingOrMalformedDemoHeadersReturn401BeforeDatabaseChecks(string connection)
    {
        await using var app = new WorkflowApiFactory(connection);
        using var client = app.CreateClient();
        foreach (var path in Paths)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.Contains("X-Demo-User-Id", problem!.Detail);
        }
        using var malformed = new HttpRequestMessage(HttpMethod.Get, "/api/tasks");
        malformed.Headers.Add("X-Demo-User-Id", "not-a-guid");
        malformed.Headers.Add("X-Organization-Id", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(malformed)).StatusCode);
    }

    [Fact]
    public async Task DemoHeadersAreIgnoredOutsideDevelopment()
    {
        await using var app = new WorkflowApiFactory("", environment: "Production");
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Demo-User-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Organization-Id", Guid.NewGuid().ToString());
        foreach (var path in Paths)
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.DoesNotContain("X-Demo-User-Id", problem!.Detail);
        }
    }

    [Fact]
    public async Task IdentifiedCallerWithoutDatabaseGetsSafe503()
    {
        await using var app = new WorkflowApiFactory("");
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add("X-Demo-User-Id", Guid.NewGuid().ToString());
        client.DefaultRequestHeaders.Add("X-Organization-Id", Guid.NewGuid().ToString());
        using var response = await client.GetAsync("/api/tasks");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/tasks?status=Archived")]
    [InlineData("/api/tasks?page=0")]
    [InlineData("/api/tasks?pageSize=201")]
    [InlineData("/api/automation-tasks?status=Done")]
    public async Task InvalidQueryValuesReturn400(string path)
    {
        await using var app = new WorkflowApiFactory("");
        using var client = app.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
