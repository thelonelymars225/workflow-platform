using Microsoft.EntityFrameworkCore;
using Npgsql;
using Workflow.Api.Data;
using Workflow.Api.Filters;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("WorkflowDatabase");
var databaseConfigured = !string.IsNullOrWhiteSpace(connectionString);
builder.Services.AddDbContext<WorkflowDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<WorkflowDatabaseRequiredFilter>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

var app = builder.Build();

if (!databaseConfigured)
    app.Logger.LogWarning("Database not configured. Set ConnectionStrings:WorkflowDatabase using user secrets or ConnectionStrings__WorkflowDatabase. /health remains available.");

// Return safe errors even in Development; never send exception/connection-string details to clients.
app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (Exception exception) when (!context.Response.HasStarted && !context.RequestAborted.IsCancellationRequested)
    {
        // EF can wrap a transient Npgsql connection failure in InvalidOperationException.
        var databaseError = IsDatabaseError(exception);
        app.Logger.LogError("Request failed ({ErrorType}); trace {TraceId}. Check database connectivity and apply migrations if this is a database error.",
            exception.GetType().Name, context.TraceIdentifier);
        context.Response.Clear();
        await Results.Problem(statusCode: databaseError ? 503 : 500,
            title: databaseError ? "Database is unavailable" : "Request could not be completed",
            detail: databaseError ? "Check the API database configuration, PostgreSQL, and migrations." : "Check the API server logs.",
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
    }
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// The HTTP development profile works without a trusted HTTPS certificate.
if (!app.Environment.IsDevelopment()) app.UseHttpsRedirection();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/db", async (WorkflowDbContext db, CancellationToken cancellationToken) =>
{
    if (!databaseConfigured)
        return Results.Problem(statusCode: 503, title: "Database is not configured",
            detail: "Set ConnectionStrings:WorkflowDatabase on the API. See the backend README.");
    if (await db.Database.CanConnectAsync(cancellationToken)) return Results.Ok(new { status = "ok" });
    app.Logger.LogWarning("Database connectivity check failed. Check ConnectionStrings:WorkflowDatabase and PostgreSQL availability.");
    return Results.Problem(statusCode: 503, title: "Database is unavailable", detail: "Check the API database configuration and PostgreSQL.");
}).Produces(200).ProducesProblem(503);

if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Development:SeedData"))
{
    if (!databaseConfigured) throw new InvalidOperationException("Development seed requires ConnectionStrings:WorkflowDatabase.");
    using var scope = app.Services.CreateScope();
    try
    {
        await DevelopmentSeed.RunAsync(scope.ServiceProvider.GetRequiredService<WorkflowDbContext>());
        app.Logger.LogInformation("Development sample data is ready.");
    }
    catch (Exception exception)
    {
        app.Logger.LogError("Development seed failed ({ErrorType}). Check PostgreSQL and apply migrations first.", exception.GetType().Name);
        throw new InvalidOperationException("Development seed failed. Check PostgreSQL and apply migrations first.");
    }
}
app.MapControllers();
app.Run();

static bool IsDatabaseError(Exception exception)
{
    for (Exception? cause = exception; cause is not null; cause = cause.InnerException)
        if (cause is NpgsqlException or DbUpdateException) return true;
    return false;
}

public partial class Program;
