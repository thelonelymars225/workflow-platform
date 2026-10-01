using Workflow.Api.Models;

namespace Workflow.Api.Domain;

public enum TaskKind { Personal, Automation }

// Why a caller can (or cannot) see a task, in precedence order.
public enum AccessReason { CrossOrganization, Self, Denied, Grant, ActingManager, Role, None }

public sealed record DepartmentNode(Guid Id, string Path);

// Everything the policy needs about one caller in one organization, loaded by TaskScopeResolver.
public sealed record AccessFacts(
    Guid OrganizationId,
    Guid UserId,
    OrgRole Role,
    bool IsActive,
    IReadOnlyList<DepartmentNode> Departments,
    IReadOnlyCollection<Guid> ManagedDepartmentIds,
    IReadOnlyList<AccessException> Exceptions,
    IReadOnlyList<ActingManagerDelegation> Delegations);

public sealed record TaskFacts(TaskKind Kind, Guid OrganizationId, Guid DepartmentId, Guid OwnerUserId, Guid? AssigneeUserId);

public sealed record AccessDecision(bool CanView, bool CanEdit, AccessReason Reason)
{
    public static readonly AccessDecision Nothing = new(false, false, AccessReason.None);
}

// Per-department outcome of rules 3-5 (deny, grant/acting manager, role).
public sealed record DepartmentAccess(bool CanView, bool CanEditPersonal, bool CanEditAutomation, AccessReason Reason);

// A caller's resolved scope in one organization. Department sets are precomputed so list queries can filter with "= ANY".
public sealed class CallerScope
{
    public required Guid OrganizationId { get; init; }
    public required Guid UserId { get; init; }
    public required OrgRole Role { get; init; }
    public required IReadOnlyDictionary<Guid, DepartmentAccess> Departments { get; init; }

    public IReadOnlyCollection<Guid> ViewDepartmentIds => [.. Departments.Where(x => x.Value.CanView).Select(x => x.Key)];
    // True when department filtering can be skipped entirely (an admin without any active deny).
    public bool ViewsWholeOrganization => Departments.Values.All(x => x.CanView);

    public DepartmentAccess For(Guid departmentId) =>
        Departments.TryGetValue(departmentId, out var access) ? access : new(false, false, false, AccessReason.None);

    // Precedence: 1) cross-org block, 2) self, 3) deny, 4) grant / acting manager, 5) role, 6) nothing.
    public AccessDecision Decide(TaskFacts task)
    {
        if (task.OrganizationId != OrganizationId) return new(false, false, AccessReason.CrossOrganization);
        var department = For(task.DepartmentId);
        var departmentEdit = task.Kind == TaskKind.Personal ? department.CanEditPersonal : department.CanEditAutomation;

        var isOwner = task.OwnerUserId == UserId;
        if (isOwner || task.AssigneeUserId == UserId)
        {
            // The owner edits their personal task; members never edit automation tasks (they only view them).
            var selfEdit = task.Kind == TaskKind.Personal && isOwner;
            return new(true, selfEdit || departmentEdit, AccessReason.Self);
        }
        return department.CanView ? new(true, departmentEdit, department.Reason) : new(false, false, department.Reason);
    }
}

public static class AccessPolicy
{
    // Returns null when the caller has no active membership: a deactivated person sees nothing at all.
    public static CallerScope? Build(AccessFacts facts, DateTimeOffset now)
    {
        if (!facts.IsActive) return null;
        var paths = facts.Departments.ToDictionary(x => x.Id, x => x.Path);
        string? PathOf(Guid id) => paths.GetValueOrDefault(id);

        var active = facts.Exceptions.Where(x => x.OrganizationId == facts.OrganizationId && x.UserId == facts.UserId && x.IsActiveAt(now)).ToList();
        var denyRoots = active.Where(x => x.Kind == AccessExceptionKind.Deny).Select(x => PathOf(x.DepartmentId)).OfType<string>().ToList();
        var grants = active.Where(x => x.Kind == AccessExceptionKind.Grant)
            .Select(x => (Path: PathOf(x.DepartmentId), Level: x.Level ?? AccessLevel.View)).Where(x => x.Path is not null).ToList();
        var delegationRoots = facts.Delegations
            .Where(x => x.OrganizationId == facts.OrganizationId && x.DelegateUserId == facts.UserId && x.IsActiveAt(now))
            .Select(x => PathOf(x.DepartmentId)).OfType<string>().ToList();
        // Role-derived manager scope applies to the Manager role; an Admin already covers the whole organization.
        var managedRoots = facts.Role == OrgRole.Manager
            ? facts.ManagedDepartmentIds.Select(PathOf).OfType<string>().ToList()
            : [];

        var result = new Dictionary<Guid, DepartmentAccess>();
        foreach (var department in facts.Departments)
        {
            bool Covers(string root) => DepartmentPaths.IsWithin(department.Path, root);

            if (denyRoots.Any(Covers))
            {
                result[department.Id] = new(false, false, false, AccessReason.Denied);
                continue;
            }
            var grantLevel = grants.Where(x => Covers(x.Path!)).Select(x => (AccessLevel?)x.Level).Max();
            var acting = delegationRoots.Any(Covers);
            var roleEdit = facts.Role == OrgRole.Admin || managedRoots.Any(Covers);

            // Grants and delegations only add access; they never reduce what the role already gives.
            var canView = acting || roleEdit || grantLevel is not null;
            var canEditPersonal = acting || roleEdit || grantLevel == AccessLevel.Edit;
            var canEditAutomation = acting || roleEdit
                || (facts.Role == OrgRole.Manager && grantLevel == AccessLevel.Edit);
            var reason = acting ? AccessReason.ActingManager
                : grantLevel is not null ? AccessReason.Grant
                : roleEdit ? AccessReason.Role
                : AccessReason.None;
            result[department.Id] = new(canView, canEditPersonal, canEditAutomation, reason);
        }

        return new CallerScope
        {
            OrganizationId = facts.OrganizationId, UserId = facts.UserId, Role = facts.Role, Departments = result
        };
    }
}
