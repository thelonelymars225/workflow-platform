namespace Workflow.Api.Models;

public enum OrgRole { Member, Manager, Admin }

public enum AccessExceptionKind { Grant, Deny }

public enum AccessLevel { View, Edit }

public class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

// A person is global; their role and department live on a per-organization Membership.
public class AppUser
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

public class Department
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? ParentId { get; set; }
    public string Name { get; set; } = "";
    // Materialized path of department IDs from the root, e.g. "/<root>/<child>/<self>/".
    public string Path { get; set; } = "";
    public int Depth { get; set; }
    // At most one manager; must be a member of the same organization.
    public Guid? ManagerUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class Membership
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public OrgRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid PrimaryDepartmentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeactivatedAt { get; set; }
}

// Additional (dual-hat) department membership within the same organization.
public class DepartmentMembership
{
    public Guid OrganizationId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

// Grant (View or Edit) or Deny for one user on a department subtree, with an optional window.
public class AccessException
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public Guid DepartmentId { get; set; }
    public AccessExceptionKind Kind { get; set; }
    public AccessLevel? Level { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public bool IsActiveAt(DateTimeOffset now) => (StartsAt is null || StartsAt <= now) && (EndsAt is null || now < EndsAt);
}

// Time-bound delegation of a department's manager scope to another member.
public class ActingManagerDelegation
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid DelegateUserId { get; set; }
    public Guid? DelegatorUserId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public bool IsActiveAt(DateTimeOffset now) => StartsAt <= now && now < EndsAt;
}
