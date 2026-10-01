namespace Workflow.Api.Domain;

public static class Clock
{
    // PostgreSQL timestamptz keeps microseconds; .NET keeps 100 ns ticks.
    public static DateTimeOffset TruncateToMicroseconds(this DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
}
