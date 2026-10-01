using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Workflow.Tests;

// Test-side DTOs mirror the API's JSON (string enums) without depending on server types for responses.
public sealed record TaskDto(Guid Id, Guid OrganizationId, Guid DepartmentId, string Title, string Status, Guid OwnerUserId,
    Guid? AssigneeUserId, bool CanEdit, string Access, string[] NextStatuses);

public sealed record AutomationTaskDto(Guid Id, Guid OrganizationId, Guid DepartmentId, string Name, string Status, Guid OwnerUserId,
    bool CanEdit, string Access);

public sealed record RunDto(Guid Id, Guid AutomationTaskId, string Status, int Attempt, Guid? RetryOfRunId);

public sealed record DepartmentDto(Guid Id, Guid? ParentId, string Name, string Path, int Depth, Guid? ManagerUserId, Guid? EffectiveManagerUserId);

public sealed record PageDto<T>(T[] Items, int Page, int PageSize, int TotalCount);

public static class ApiClient
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public static HttpClient As(this HttpClient client, Guid user, Guid organization)
    {
        client.DefaultRequestHeaders.Remove("X-Demo-User-Id");
        client.DefaultRequestHeaders.Remove("X-Organization-Id");
        client.DefaultRequestHeaders.Add("X-Demo-User-Id", user.ToString());
        client.DefaultRequestHeaders.Add("X-Organization-Id", organization.ToString());
        return client;
    }

    public static async Task<T> Read<T>(this HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(Json))!;

    // Pages through the whole list so tests can assert on complete visibility.
    public static async Task<List<TaskDto>> AllTasksAsync(this HttpClient client, string? status = null)
    {
        var all = new List<TaskDto>();
        for (var page = 1; ; page++)
        {
            var url = $"/api/tasks?page={page}&pageSize=200" + (status is null ? "" : $"&status={status}");
            using var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var body = await response.Read<PageDto<TaskDto>>();
            all.AddRange(body.Items);
            if (all.Count >= body.TotalCount || body.Items.Length == 0) return all;
        }
    }

    public static async Task<List<AutomationTaskDto>> AllAutomationTasksAsync(this HttpClient client)
    {
        var all = new List<AutomationTaskDto>();
        for (var page = 1; ; page++)
        {
            using var response = await client.GetAsync($"/api/automation-tasks?page={page}&pageSize=200");
            response.EnsureSuccessStatusCode();
            var body = await response.Read<PageDto<AutomationTaskDto>>();
            all.AddRange(body.Items);
            if (all.Count >= body.TotalCount || body.Items.Length == 0) return all;
        }
    }

    public static Task<HttpResponseMessage> SetStatusAsync(this HttpClient client, Guid taskId, string status) =>
        client.PatchAsJsonAsync($"/api/tasks/{taskId}/status", new { status });
}
