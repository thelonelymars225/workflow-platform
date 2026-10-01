namespace Workflow.Api.Domain;

// Materialized paths look like "/<root>/<child>/<self>/" using 32-hex-digit department IDs.
public static class DepartmentPaths
{
    public const string Root = "/";

    public static string Segment(Guid id) => id.ToString("N");

    public static string For(string? parentPath, Guid id) => (parentPath ?? Root) + Segment(id) + "/";

    public static int DepthOf(string path) => path.Count(c => c == '/') - 2;

    // Root first, excluding the department itself.
    public static IReadOnlyList<Guid> AncestorIds(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Take(segments.Length - 1).Select(segment => Guid.ParseExact(segment, "N")).ToArray();
    }

    // True when the department at path is the root of the subtree or one of its descendants.
    public static bool IsWithin(string path, string subtreeRootPath) => path.StartsWith(subtreeRootPath, StringComparison.Ordinal);

    // Moving a department under itself or one of its descendants would create a cycle.
    public static bool WouldCreateCycle(string movingPath, string? newParentPath) =>
        newParentPath is not null && IsWithin(newParentPath, movingPath);
}
