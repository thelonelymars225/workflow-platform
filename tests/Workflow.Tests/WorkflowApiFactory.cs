using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Workflow.Tests;

internal sealed class WorkflowApiFactory(string connectionString, bool seed = false) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:WorkflowDatabase", connectionString);
        builder.UseSetting("Development:SeedData", seed.ToString());
    }
}
