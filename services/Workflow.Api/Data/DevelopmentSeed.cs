using Microsoft.EntityFrameworkCore;

namespace Workflow.Api.Data;

public static class DevelopmentSeed
{
    public static async Task RunAsync(WorkflowDbContext db)
    {
        // A stable primary key and ON CONFLICT make repeat/concurrent development starts safe.
        var id = Guid.Parse("65f64362-d031-4dca-a7af-85991e7a08c0");
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Workflows" ("Id", "Name", "Description", "CreatedAt", "UpdatedAt")
            VALUES ({id}, {"Sample workflow"}, {"An opt-in development example."}, {now}, {now})
            ON CONFLICT ("Id") DO NOTHING
            """);
    }
}
