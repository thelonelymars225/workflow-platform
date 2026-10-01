using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Workflow.Tests;

internal sealed class WorkflowApiFactory(string connectionString, bool seed = false, string environment = "Development",
    IReadOnlyDictionary<string, string?>? settings = null) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("ConnectionStrings:WorkflowDatabase", connectionString);
        builder.UseSetting("Development:SeedData", seed.ToString());
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>()) builder.UseSetting(key, value);
    }
}
