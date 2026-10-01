using Workflow.Api.Domain;
using Workflow.Api.Models;
using Workflow.Api.Services;

namespace Workflow.Tests;

public class DepartmentPathTests
{
    private static readonly Guid A = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    [Fact]
    public void BuildsPathsAndDepthsFromParent()
    {
        var a = DepartmentPaths.For(null, A);
        var b = DepartmentPaths.For(a, B);
        var c = DepartmentPaths.For(b, C);
        Assert.Equal($"/{A:N}/", a);
        Assert.Equal($"/{A:N}/{B:N}/{C:N}/", c);
        Assert.Equal([0, 1, 2], new[] { a, b, c }.Select(DepartmentPaths.DepthOf));
    }

    [Fact]
    public void AncestorsAreRootFirstAndExcludeSelf()
    {
        var c = DepartmentPaths.For(DepartmentPaths.For(DepartmentPaths.For(null, A), B), C);
        Assert.Equal([A, B], DepartmentPaths.AncestorIds(c));
        Assert.Empty(DepartmentPaths.AncestorIds(DepartmentPaths.For(null, A)));
    }

    [Fact]
    public void SubtreeMembershipUsesWholeSegments()
    {
        var a = DepartmentPaths.For(null, A);
        var b = DepartmentPaths.For(a, B);
        Assert.True(DepartmentPaths.IsWithin(b, a));
        Assert.True(DepartmentPaths.IsWithin(a, a));
        Assert.False(DepartmentPaths.IsWithin(a, b));
        // A sibling whose ID shares a prefix is not inside the subtree because every segment ends with '/'.
        var similar = DepartmentPaths.For(null, Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011"));
        Assert.False(DepartmentPaths.IsWithin(similar, a));
    }

    [Fact]
    public void DetectsCycles()
    {
        var a = DepartmentPaths.For(null, A);
        var b = DepartmentPaths.For(a, B);
        Assert.True(DepartmentPaths.WouldCreateCycle(a, a));
        Assert.True(DepartmentPaths.WouldCreateCycle(a, b));
        Assert.False(DepartmentPaths.WouldCreateCycle(b, a));
        Assert.False(DepartmentPaths.WouldCreateCycle(b, null));
        Assert.False(DepartmentPaths.WouldCreateCycle(b, DepartmentPaths.For(null, C)));
    }

    [Fact]
    public void EffectiveManagerFallsBackToNearestActiveAncestor()
    {
        var boss = Guid.NewGuid();
        var lead = Guid.NewGuid();
        var onLeave = Guid.NewGuid();
        Department Dept(Guid? manager) => new() { Id = Guid.NewGuid(), ManagerUserId = manager };
        var self = Dept(null);
        var parent = Dept(onLeave);
        var grandparent = Dept(lead);
        var root = Dept(boss);
        var active = new HashSet<Guid> { boss, lead };

        var result = OrgHierarchyService.ResolveEffectiveManager([self, parent, grandparent, root], active);
        Assert.Equal(new EffectiveManager(lead, grandparent.Id, true), result);

        var own = Dept(boss);
        Assert.Equal(new EffectiveManager(boss, own.Id, false), OrgHierarchyService.ResolveEffectiveManager([own, root], active));
        Assert.Null(OrgHierarchyService.ResolveEffectiveManager([self, parent], active));
    }
}
