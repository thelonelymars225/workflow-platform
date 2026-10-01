using Microsoft.EntityFrameworkCore;
using Workflow.Api.Data;
using Workflow.Api.Domain;
using Workflow.Api.Models;

namespace Workflow.Api.Services;

public sealed class HierarchyException(string message) : Exception(message);

public sealed record TeamMember(Guid UserId, string DisplayName, OrgRole Role, bool IsPrimaryDepartment);

public sealed record EffectiveManager(Guid UserId, Guid ManagedDepartmentId, bool IsInherited);

// Department tree operations over the materialized path.
public sealed class OrgHierarchyService(WorkflowDbContext db, TimeProvider clock)
{
    public async Task<Department> CreateDepartmentAsync(Guid organizationId, string name, Guid? parentId,
        Guid? managerUserId = null, Guid? id = null, CancellationToken cancellationToken = default)
    {
        string? parentPath = null;
        if (parentId is { } pid)
        {
            var parent = await db.Departments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == pid, cancellationToken)
                ?? throw new HierarchyException("Parent department does not exist.");
            if (parent.OrganizationId != organizationId)
                throw new HierarchyException("Parent department belongs to another organization.");
            parentPath = parent.Path;
        }
        var departmentId = id ?? Guid.NewGuid();
        var path = DepartmentPaths.For(parentPath, departmentId);
        var department = new Department
        {
            Id = departmentId, OrganizationId = organizationId, ParentId = parentId, Name = name.Trim(),
            Path = path, Depth = DepartmentPaths.DepthOf(path), ManagerUserId = managerUserId,
            CreatedAt = clock.GetUtcNow().TruncateToMicroseconds()
        };
        db.Departments.Add(department);
        await db.SaveChangesAsync(cancellationToken);
        return department;
    }

    // Re-parents a department and rewrites the paths of its whole subtree; rejects cycles and cross-org moves.
    public async Task MoveDepartmentAsync(Guid departmentId, Guid? newParentId, CancellationToken cancellationToken = default)
    {
        var department = await db.Departments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == departmentId, cancellationToken)
            ?? throw new HierarchyException("Department does not exist.");
        string? newParentPath = null;
        if (newParentId is { } pid)
        {
            var parent = await db.Departments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == pid, cancellationToken)
                ?? throw new HierarchyException("Parent department does not exist.");
            if (parent.OrganizationId != department.OrganizationId)
                throw new HierarchyException("Parent department belongs to another organization.");
            newParentPath = parent.Path;
        }
        if (DepartmentPaths.WouldCreateCycle(department.Path, newParentPath))
            throw new HierarchyException("A department cannot be moved under itself or one of its descendants.");

        var oldPath = department.Path;
        var newPath = DepartmentPaths.For(newParentPath, department.Id);
        var depthDelta = DepartmentPaths.DepthOf(newPath) - department.Depth;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Departments"
            SET "Path" = {newPath} || substr("Path", {oldPath.Length + 1}), "Depth" = "Depth" + {depthDelta}
            WHERE "OrganizationId" = {department.OrganizationId} AND "Path" LIKE {oldPath + "%"}
            """, cancellationToken);
        await db.Departments.Where(x => x.Id == departmentId)
            .ExecuteUpdateAsync(x => x.SetProperty(d => d.ParentId, newParentId), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetManagerAsync(Guid departmentId, Guid? managerUserId, CancellationToken cancellationToken = default)
    {
        var department = await db.Departments.SingleOrDefaultAsync(x => x.Id == departmentId, cancellationToken)
            ?? throw new HierarchyException("Department does not exist.");
        if (managerUserId is { } uid && !await db.Memberships.AnyAsync(
                x => x.OrganizationId == department.OrganizationId && x.UserId == uid, cancellationToken))
            throw new HierarchyException("A manager must be a member of the department's organization.");
        department.ManagerUserId = managerUserId;
        await db.SaveChangesAsync(cancellationToken);
    }

    // The department and all of its descendants, ordered by path.
    public async Task<List<Department>> GetSubtreeAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        var root = await FindAsync(departmentId, cancellationToken);
        return await db.Departments.AsNoTracking()
            .Where(x => x.OrganizationId == root.OrganizationId && x.Path.StartsWith(root.Path))
            .OrderBy(x => x.Path).ToListAsync(cancellationToken);
    }

    // Ancestors nearest first, excluding the department itself.
    public async Task<List<Department>> GetAncestorsAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        var department = await FindAsync(departmentId, cancellationToken);
        var ids = DepartmentPaths.AncestorIds(department.Path);
        var rows = await db.Departments.AsNoTracking()
            .Where(x => x.OrganizationId == department.OrganizationId && ids.Contains(x.Id)).ToListAsync(cancellationToken);
        return rows.OrderByDescending(x => x.Depth).ToList();
    }

    // Active members whose primary department is this one, plus active extra (dual-hat) members.
    public async Task<List<TeamMember>> GetTeamAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        var department = await FindAsync(departmentId, cancellationToken);
        var extraUserIds = db.DepartmentMemberships.Where(x => x.DepartmentId == departmentId).Select(x => x.UserId);
        return await (
            from membership in db.Memberships
            join user in db.Users on membership.UserId equals user.Id
            where membership.OrganizationId == department.OrganizationId && membership.IsActive
                && (membership.PrimaryDepartmentId == departmentId || extraUserIds.Contains(membership.UserId))
            orderby user.DisplayName, user.Id
            select new TeamMember(user.Id, user.DisplayName, membership.Role, membership.PrimaryDepartmentId == departmentId))
            .ToListAsync(cancellationToken);
    }

    // The department's own active manager, otherwise the nearest ancestor's active manager.
    public async Task<EffectiveManager?> GetEffectiveManagerAsync(Guid departmentId, CancellationToken cancellationToken = default)
    {
        var department = await FindAsync(departmentId, cancellationToken);
        var chain = new List<Department> { department };
        chain.AddRange(await GetAncestorsAsync(departmentId, cancellationToken));
        var managerIds = chain.Where(x => x.ManagerUserId is not null).Select(x => x.ManagerUserId!.Value).Distinct().ToList();
        var active = (await db.Memberships.AsNoTracking()
            .Where(x => x.OrganizationId == department.OrganizationId && x.IsActive && managerIds.Contains(x.UserId))
            .Select(x => x.UserId).ToListAsync(cancellationToken)).ToHashSet();
        return ResolveEffectiveManager(chain, active);
    }

    // chain: the department first, then its ancestors nearest first.
    public static EffectiveManager? ResolveEffectiveManager(IReadOnlyList<Department> chain, IReadOnlySet<Guid> activeUserIds)
    {
        for (var i = 0; i < chain.Count; i++)
            if (chain[i].ManagerUserId is { } manager && activeUserIds.Contains(manager))
                return new EffectiveManager(manager, chain[i].Id, i > 0);
        return null;
    }

    private async Task<Department> FindAsync(Guid departmentId, CancellationToken cancellationToken) =>
        await db.Departments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == departmentId, cancellationToken)
        ?? throw new HierarchyException("Department does not exist.");
}
