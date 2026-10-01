using Microsoft.EntityFrameworkCore;
using Workflow.Api.Data;
using Workflow.Api.Domain;
using Workflow.Api.Identity;
using Workflow.Api.Models;

namespace Workflow.Api.Services;

// The one place that turns a caller into a task scope; every task query and mutation goes through it.
public sealed class TaskScopeResolver(WorkflowDbContext db, TimeProvider clock)
{
    public async Task<CallerScope?> ResolveAsync(CallerIdentity caller, CancellationToken cancellationToken = default)
    {
        var membership = await db.Memberships.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrganizationId == caller.OrganizationId && x.UserId == caller.UserId, cancellationToken);
        if (membership is not { IsActive: true }) return null;

        var org = caller.OrganizationId;
        var departments = await db.Departments.AsNoTracking().Where(x => x.OrganizationId == org)
            .Select(x => new { x.Id, x.Path, x.ManagerUserId }).ToListAsync(cancellationToken);
        var exceptions = await db.AccessExceptions.AsNoTracking()
            .Where(x => x.OrganizationId == org && x.UserId == caller.UserId).ToListAsync(cancellationToken);
        var delegations = await db.ActingManagerDelegations.AsNoTracking()
            .Where(x => x.OrganizationId == org && x.DelegateUserId == caller.UserId).ToListAsync(cancellationToken);

        var facts = new AccessFacts(org, caller.UserId, membership.Role, membership.IsActive,
            departments.Select(x => new DepartmentNode(x.Id, x.Path)).ToList(),
            departments.Where(x => x.ManagerUserId == caller.UserId).Select(x => x.Id).ToList(),
            exceptions, delegations);
        return AccessPolicy.Build(facts, clock.GetUtcNow());
    }

    public IQueryable<PersonalTask> VisiblePersonalTasks(CallerScope scope)
    {
        var query = db.PersonalTasks.AsNoTracking().Where(x => x.OrganizationId == scope.OrganizationId);
        if (scope.ViewsWholeOrganization) return query;
        var departments = scope.ViewDepartmentIds.ToList();
        var me = scope.UserId;
        return query.Where(x => x.OwnerUserId == me || x.AssigneeUserId == me || departments.Contains(x.DepartmentId));
    }

    public IQueryable<AutomationTask> VisibleAutomationTasks(CallerScope scope)
    {
        var query = db.AutomationTasks.AsNoTracking().Where(x => x.OrganizationId == scope.OrganizationId);
        if (scope.ViewsWholeOrganization) return query;
        var departments = scope.ViewDepartmentIds.ToList();
        var me = scope.UserId;
        return query.Where(x => x.OwnerUserId == me || departments.Contains(x.DepartmentId));
    }

    public static AccessDecision Decide(CallerScope scope, PersonalTask task) =>
        scope.Decide(new TaskFacts(TaskKind.Personal, task.OrganizationId, task.DepartmentId, task.OwnerUserId, task.AssigneeUserId));

    public static AccessDecision Decide(CallerScope scope, AutomationTask task) =>
        scope.Decide(new TaskFacts(TaskKind.Automation, task.OrganizationId, task.DepartmentId, task.OwnerUserId, null));
}
