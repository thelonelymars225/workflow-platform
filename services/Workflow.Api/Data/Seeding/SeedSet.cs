using Workflow.Api.Domain;
using Workflow.Api.Models;

namespace Workflow.Api.Data.Seeding;

// In-memory builder for one or more scenarios. Every row gets an ID derived from a readable key.
public sealed class SeedSet(DateTimeOffset now)
{
    // Creation dates are anchored to a fixed day so repeated builds produce identical rows.
    public static readonly DateTimeOffset Anchor = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    public DateTimeOffset Now { get; } = now.TruncateToMicroseconds();
    public List<Organization> Organizations { get; } = [];
    public List<AppUser> Users { get; } = [];
    public List<Department> Departments { get; } = [];
    public List<Membership> Memberships { get; } = [];
    public List<DepartmentMembership> DepartmentMemberships { get; } = [];
    public List<AccessException> AccessExceptions { get; } = [];
    public List<ActingManagerDelegation> Delegations { get; } = [];
    public List<PersonalTask> PersonalTasks { get; } = [];
    public List<AutomationTask> AutomationTasks { get; } = [];
    public List<AutomationRun> AutomationRuns { get; } = [];

    private readonly HashSet<Guid> ids = [];
    private readonly Dictionary<Guid, Department> departmentsById = [];

    private T Once<T>(Guid id, Func<T> create, List<T> list) where T : class
    {
        if (!ids.Add(id)) return list.First(x => (Guid)x.GetType().GetProperty("Id")!.GetValue(x)! == id);
        var row = create();
        list.Add(row);
        return row;
    }

    public Guid Organization(string key, string name, string slug)
    {
        var id = SeedIds.Of($"org/{key}");
        Once(id, () => new Organization { Id = id, Name = name, Slug = slug, CreatedAt = Anchor }, Organizations);
        return id;
    }

    // Users are global: scenarios that share a person (agency staff, contractors) use the same key.
    public Guid User(string key, string displayName, string? email = null)
    {
        var id = SeedIds.Of($"user/{key}");
        Once(id, () => new AppUser { Id = id, DisplayName = displayName, Email = email ?? $"{key.Replace('/', '.')}@seed.example", CreatedAt = Anchor }, Users);
        return id;
    }

    public Guid Department(Guid organization, string key, string name, Guid? parent = null, Guid? manager = null)
    {
        var id = SeedIds.Of($"dept/{key}");
        Once(id, () =>
        {
            var parentPath = parent is { } p ? departmentsById[p].Path : null;
            var path = DepartmentPaths.For(parentPath, id);
            var department = new Department
            {
                Id = id, OrganizationId = organization, ParentId = parent, Name = name, Path = path,
                Depth = DepartmentPaths.DepthOf(path), ManagerUserId = manager, CreatedAt = Anchor
            };
            departmentsById[id] = department;
            return department;
        }, Departments);
        return id;
    }

    public void SetManager(Guid department, Guid? manager) => departmentsById[department].ManagerUserId = manager;

    public Department GetDepartment(Guid id) => departmentsById[id];

    public Guid Member(Guid organization, Guid user, OrgRole role, Guid primaryDepartment, bool active = true)
    {
        var id = SeedIds.Of($"membership/{organization:N}/{user:N}");
        Once(id, () => new Membership
        {
            Id = id, OrganizationId = organization, UserId = user, Role = role, PrimaryDepartmentId = primaryDepartment,
            IsActive = active, CreatedAt = Anchor, DeactivatedAt = active ? null : Anchor.AddDays(200)
        }, Memberships);
        return id;
    }

    public void ExtraDepartment(Guid organization, Guid user, Guid department)
    {
        if (DepartmentMemberships.Any(x => x.DepartmentId == department && x.UserId == user)) return;
        DepartmentMemberships.Add(new DepartmentMembership { OrganizationId = organization, DepartmentId = department, UserId = user, CreatedAt = Anchor });
    }

    public Guid Grant(string key, Guid organization, Guid user, Guid department, AccessLevel level,
        DateTimeOffset? starts = null, DateTimeOffset? ends = null, string? reason = null) =>
        Exception(key, organization, user, department, AccessExceptionKind.Grant, level, starts, ends, reason);

    public Guid Deny(string key, Guid organization, Guid user, Guid department,
        DateTimeOffset? starts = null, DateTimeOffset? ends = null, string? reason = null) =>
        Exception(key, organization, user, department, AccessExceptionKind.Deny, null, starts, ends, reason);

    private Guid Exception(string key, Guid organization, Guid user, Guid department, AccessExceptionKind kind, AccessLevel? level,
        DateTimeOffset? starts, DateTimeOffset? ends, string? reason)
    {
        var id = SeedIds.Of($"access/{key}");
        Once(id, () => new AccessException
        {
            Id = id, OrganizationId = organization, UserId = user, DepartmentId = department, Kind = kind, Level = level,
            StartsAt = starts?.TruncateToMicroseconds(), EndsAt = ends?.TruncateToMicroseconds(), Reason = reason, CreatedAt = Anchor
        }, AccessExceptions);
        return id;
    }

    public Guid Delegation(string key, Guid organization, Guid department, Guid delegateUser, Guid? delegator,
        DateTimeOffset starts, DateTimeOffset ends)
    {
        var id = SeedIds.Of($"delegation/{key}");
        Once(id, () => new ActingManagerDelegation
        {
            Id = id, OrganizationId = organization, DepartmentId = department, DelegateUserId = delegateUser,
            DelegatorUserId = delegator, StartsAt = starts.TruncateToMicroseconds(), EndsAt = ends.TruncateToMicroseconds(), CreatedAt = Anchor
        }, Delegations);
        return id;
    }

    public Guid Task(string key, Guid organization, Guid department, Guid owner, string title,
        PersonalTaskStatus status = PersonalTaskStatus.ToDo, Guid? assignee = null, int dayOffset = 0, string? description = null)
    {
        var id = SeedIds.Of($"task/{key}");
        var created = Anchor.AddMinutes(dayOffset * 1440 + id.ToByteArray()[0] * 4);
        Once(id, () => new PersonalTask
        {
            Id = id, OrganizationId = organization, DepartmentId = department, OwnerUserId = owner, AssigneeUserId = assignee,
            Title = title, Description = description, Status = status, DueAt = created.AddDays(14), CreatedAt = created,
            UpdatedAt = created.AddHours(status == PersonalTaskStatus.ToDo ? 0 : 6)
        }, PersonalTasks);
        return id;
    }

    public Guid Automation(string key, Guid organization, Guid department, Guid owner, string name,
        AutomationTaskStatus status = AutomationTaskStatus.Active, int dayOffset = 0)
    {
        var id = SeedIds.Of($"automation/{key}");
        var created = Anchor.AddDays(dayOffset);
        Once(id, () => new AutomationTask
        {
            Id = id, OrganizationId = organization, DepartmentId = department, OwnerUserId = owner, Name = name,
            Status = status, CreatedAt = created, UpdatedAt = created
        }, AutomationTasks);
        return id;
    }

    public Guid Run(string key, Guid automationTask, AutomationRunStatus status, int attempt, DateTimeOffset queuedAt,
        Guid? retryOf = null, string? error = null)
    {
        var id = SeedIds.Of($"run/{key}");
        Once(id, () => new AutomationRun
        {
            Id = id, AutomationTaskId = automationTask, Status = status, Attempt = attempt, RetryOfRunId = retryOf,
            Error = error ?? (status == AutomationRunStatus.Failed ? "Step 'deliver' timed out after 30s." : null),
            QueuedAt = queuedAt,
            StartedAt = status == AutomationRunStatus.Queued ? null : queuedAt.AddSeconds(5),
            FinishedAt = AutomationRunStatusIsTerminal(status) ? queuedAt.AddMinutes(2) : null
        }, AutomationRuns);
        return id;
    }

    private static bool AutomationRunStatusIsTerminal(AutomationRunStatus status) => TaskLifecycle.IsTerminal(status);
}
