using Workflow.Api.Domain;
using Workflow.Api.Models;

namespace Workflow.Tests;

// One test group per precedence level: 1) cross-org, 2) self, 3) deny, 4) grant / acting manager, 5) role, 6) nothing.
public class AccessPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.NewGuid(), OtherOrg = Guid.NewGuid();
    private static readonly Guid Me = Guid.NewGuid(), Someone = Guid.NewGuid();

    // root > sales > east ; root > legal ; root > ops
    private static readonly Guid Root = Guid.NewGuid(), Sales = Guid.NewGuid(), East = Guid.NewGuid(), Legal = Guid.NewGuid(), Ops = Guid.NewGuid();
    private static readonly DepartmentNode[] Tree = BuildTree();

    private static DepartmentNode[] BuildTree()
    {
        var root = DepartmentPaths.For(null, Root);
        var sales = DepartmentPaths.For(root, Sales);
        return [new(Root, root), new(Sales, sales), new(East, DepartmentPaths.For(sales, East)),
            new(Legal, DepartmentPaths.For(root, Legal)), new(Ops, DepartmentPaths.For(root, Ops))];
    }

    private static CallerScope Scope(OrgRole role, Guid[]? manages = null, AccessException[]? exceptions = null,
        ActingManagerDelegation[]? delegations = null) =>
        AccessPolicy.Build(new AccessFacts(Org, Me, role, true, Tree, manages ?? [], exceptions ?? [], delegations ?? []), Now)!;

    private static AccessException Grant(Guid department, AccessLevel level, DateTimeOffset? starts = null, DateTimeOffset? ends = null) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = Org, UserId = Me, DepartmentId = department, Kind = AccessExceptionKind.Grant,
        Level = level, StartsAt = starts, EndsAt = ends
    };

    private static AccessException Deny(Guid department, DateTimeOffset? starts = null, DateTimeOffset? ends = null) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = Org, UserId = Me, DepartmentId = department, Kind = AccessExceptionKind.Deny,
        StartsAt = starts, EndsAt = ends
    };

    private static ActingManagerDelegation Acting(Guid department, DateTimeOffset starts, DateTimeOffset ends) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = Org, DepartmentId = department, DelegateUserId = Me, StartsAt = starts, EndsAt = ends
    };

    private static TaskFacts Personal(Guid department, Guid? owner = null, Guid? assignee = null, Guid? org = null) =>
        new(TaskKind.Personal, org ?? Org, department, owner ?? Someone, assignee);

    private static TaskFacts Automation(Guid department, Guid? owner = null) => new(TaskKind.Automation, Org, department, owner ?? Someone, null);

    private static void AssertDecision(AccessDecision decision, bool view, bool edit, AccessReason reason)
    {
        Assert.Equal(view, decision.CanView);
        Assert.Equal(edit, decision.CanEdit);
        Assert.Equal(reason, decision.Reason);
    }

    // Level 1: cross-organization block.
    [Fact]
    public void L1_TaskInAnotherOrganizationIsNeverVisibleEvenToItsOwnerOrAdmin()
    {
        var admin = Scope(OrgRole.Admin);
        AssertDecision(admin.Decide(Personal(Root, owner: Me, org: OtherOrg)), false, false, AccessReason.CrossOrganization);
        AssertDecision(admin.Decide(Personal(Root, assignee: Me, org: OtherOrg)), false, false, AccessReason.CrossOrganization);
    }

    [Fact]
    public void L1_InactiveMembershipYieldsNoScopeAtAll()
    {
        var facts = new AccessFacts(Org, Me, OrgRole.Admin, false, Tree, [], [], []);
        Assert.Null(AccessPolicy.Build(facts, Now));
    }

    [Fact]
    public void L1_ExceptionsAndDelegationsFromAnotherOrganizationAreIgnored()
    {
        var foreignGrant = Grant(Sales, AccessLevel.Edit);
        foreignGrant.OrganizationId = OtherOrg;
        var foreignActing = Acting(Ops, Now.AddDays(-1), Now.AddDays(1));
        foreignActing.OrganizationId = OtherOrg;
        var scope = Scope(OrgRole.Member, exceptions: [foreignGrant], delegations: [foreignActing]);
        Assert.False(scope.Decide(Personal(Sales)).CanView);
        Assert.False(scope.Decide(Personal(Ops)).CanView);
    }

    // Level 2: self.
    [Fact]
    public void L2_OwnerAndAssigneeSeeTheirTasksEvenInsideADeniedDepartment()
    {
        var scope = Scope(OrgRole.Member, exceptions: [Deny(Legal)]);
        AssertDecision(scope.Decide(Personal(Legal, owner: Me)), true, true, AccessReason.Self);
        AssertDecision(scope.Decide(Personal(Legal, assignee: Me)), true, false, AccessReason.Self);
        AssertDecision(scope.Decide(Personal(Legal)), false, false, AccessReason.Denied);
    }

    [Fact]
    public void L2_MemberSeesOwnAndAssignedTasksAnywhereButCannotEditAssignedOnes()
    {
        var scope = Scope(OrgRole.Member);
        AssertDecision(scope.Decide(Personal(Ops, owner: Me)), true, true, AccessReason.Self);
        AssertDecision(scope.Decide(Personal(Ops, assignee: Me)), true, false, AccessReason.Self);
    }

    [Fact]
    public void L2_MemberOwningAnAutomationTaskOnlyViewsIt()
    {
        AssertDecision(Scope(OrgRole.Member).Decide(Automation(Ops, owner: Me)), true, false, AccessReason.Self);
        AssertDecision(Scope(OrgRole.Manager, manages: [Ops]).Decide(Automation(Ops, owner: Me)), true, true, AccessReason.Self);
    }

    // Level 3: explicit deny.
    [Fact]
    public void L3_DenyBeatsAdminRole()
    {
        var scope = Scope(OrgRole.Admin, exceptions: [Deny(Legal)]);
        AssertDecision(scope.Decide(Personal(Legal)), false, false, AccessReason.Denied);
        AssertDecision(scope.Decide(Personal(Sales)), true, true, AccessReason.Role);
        Assert.False(scope.ViewsWholeOrganization);
    }

    [Fact]
    public void L3_DenyBeatsManagerScopeOnTheDeniedSubtreeOnly()
    {
        var scope = Scope(OrgRole.Manager, manages: [Root], exceptions: [Deny(Sales)]);
        Assert.False(scope.Decide(Personal(Sales)).CanView);
        Assert.False(scope.Decide(Personal(East)).CanView);
        Assert.True(scope.Decide(Personal(Root)).CanView);
        Assert.True(scope.Decide(Personal(Legal)).CanView);
    }

    [Fact]
    public void L3_DenyBeatsGrantOnTheSameDepartment()
    {
        var scope = Scope(OrgRole.Member, exceptions: [Grant(Sales, AccessLevel.Edit), Deny(Sales)]);
        AssertDecision(scope.Decide(Personal(Sales)), false, false, AccessReason.Denied);
    }

    [Fact]
    public void L3_DenyBeatsActingManagerDelegation()
    {
        var scope = Scope(OrgRole.Member, exceptions: [Deny(East)], delegations: [Acting(Sales, Now.AddDays(-1), Now.AddDays(1))]);
        Assert.True(scope.Decide(Personal(Sales)).CanEdit);
        Assert.False(scope.Decide(Personal(East)).CanView);
    }

    [Fact]
    public void L3_ExpiredOrFutureDenyHasNoEffect()
    {
        var scope = Scope(OrgRole.Admin, exceptions: [Deny(Legal, ends: Now.AddDays(-1)), Deny(Sales, starts: Now.AddDays(1))]);
        Assert.True(scope.Decide(Personal(Legal)).CanView);
        Assert.True(scope.Decide(Personal(Sales)).CanView);
        Assert.True(scope.ViewsWholeOrganization);
    }

    // Level 4: explicit grant or active acting-manager delegation.
    [Fact]
    public void L4_ViewGrantCoversSubtreeButDoesNotAllowEdit()
    {
        var scope = Scope(OrgRole.Member, exceptions: [Grant(Sales, AccessLevel.View)]);
        AssertDecision(scope.Decide(Personal(Sales)), true, false, AccessReason.Grant);
        AssertDecision(scope.Decide(Personal(East)), true, false, AccessReason.Grant);
        AssertDecision(scope.Decide(Personal(Ops)), false, false, AccessReason.None);
    }

    [Fact]
    public void L4_EditGrantImpliesViewAndAllowsPersonalButNotMemberAutomationEdits()
    {
        var scope = Scope(OrgRole.Member, exceptions: [Grant(Sales, AccessLevel.Edit)]);
        AssertDecision(scope.Decide(Personal(East)), true, true, AccessReason.Grant);
        AssertDecision(scope.Decide(Automation(East)), true, false, AccessReason.Grant);
    }

    [Fact]
    public void L4_ManagerWithEditGrantCanEditAutomationTasksThere()
    {
        var scope = Scope(OrgRole.Manager, manages: [Ops], exceptions: [Grant(Sales, AccessLevel.Edit)]);
        AssertDecision(scope.Decide(Automation(Sales)), true, true, AccessReason.Grant);
    }

    [Fact]
    public void L4_GrantsOutsideTheirWindowHaveNoEffect()
    {
        var scope = Scope(OrgRole.Member, exceptions:
        [
            Grant(Sales, AccessLevel.Edit, ends: Now.AddMinutes(-1)),
            Grant(Ops, AccessLevel.Edit, starts: Now.AddMinutes(1)),
            Grant(Legal, AccessLevel.View, starts: Now.AddDays(-1), ends: Now.AddDays(1)),
        ]);
        Assert.False(scope.Decide(Personal(Sales)).CanView);
        Assert.False(scope.Decide(Personal(Ops)).CanView);
        Assert.True(scope.Decide(Personal(Legal)).CanView);
    }

    [Fact]
    public void L4_ActiveActingManagerGetsManagerScopeIncludingAutomationEdits()
    {
        var scope = Scope(OrgRole.Member, delegations: [Acting(Sales, Now.AddDays(-3), Now.AddDays(3))]);
        AssertDecision(scope.Decide(Personal(East)), true, true, AccessReason.ActingManager);
        AssertDecision(scope.Decide(Automation(East)), true, true, AccessReason.ActingManager);
        Assert.False(scope.Decide(Personal(Ops)).CanView);
    }

    [Fact]
    public void L4_ExpiredOrFutureDelegationGrantsNothing()
    {
        var scope = Scope(OrgRole.Member, delegations:
        [
            Acting(Sales, Now.AddDays(-60), Now.AddDays(-30)),
            Acting(Ops, Now.AddDays(1), Now.AddDays(5)),
            Acting(Legal, Now.AddDays(-1), Now), // the end is exclusive
        ]);
        Assert.Empty(scope.ViewDepartmentIds);
    }

    [Fact]
    public void L4_GrantNeverReducesWhatTheRoleAlreadyGives()
    {
        var scope = Scope(OrgRole.Manager, manages: [Sales], exceptions: [Grant(Sales, AccessLevel.View)]);
        AssertDecision(scope.Decide(Personal(East)), true, true, AccessReason.Grant);
        Assert.True(scope.Decide(Automation(East)).CanEdit);
    }

    // Level 5: role-derived scope.
    [Fact]
    public void L5_AdminSeesAndEditsEverythingInTheOrganization()
    {
        var scope = Scope(OrgRole.Admin);
        Assert.True(scope.ViewsWholeOrganization);
        foreach (var department in Tree)
        {
            AssertDecision(scope.Decide(Personal(department.Id)), true, true, AccessReason.Role);
            AssertDecision(scope.Decide(Automation(department.Id)), true, true, AccessReason.Role);
        }
    }

    [Fact]
    public void L5_ManagerSeesManagedDepartmentsAndAllDescendantsOnly()
    {
        var scope = Scope(OrgRole.Manager, manages: [Sales]);
        AssertDecision(scope.Decide(Personal(Sales)), true, true, AccessReason.Role);
        AssertDecision(scope.Decide(Personal(East)), true, true, AccessReason.Role);
        AssertDecision(scope.Decide(Automation(East)), true, true, AccessReason.Role);
        Assert.False(scope.Decide(Personal(Root)).CanView);
        Assert.False(scope.Decide(Personal(Ops)).CanView);
        Assert.Equal(new[] { Sales, East }.Order(), scope.ViewDepartmentIds.Order());
    }

    [Fact]
    public void L5_ManagerOfTwoBranchesSeesBoth()
    {
        var scope = Scope(OrgRole.Manager, manages: [East, Legal]);
        Assert.Equal(new[] { East, Legal }.Order(), scope.ViewDepartmentIds.Order());
    }

    [Fact]
    public void L5_ManagedDepartmentGivesNoScopeToAMemberRole()
    {
        var scope = Scope(OrgRole.Member, manages: [Sales]);
        Assert.False(scope.Decide(Personal(Sales)).CanView);
    }

    // Level 6: nothing else.
    [Fact]
    public void L6_MemberSeesNothingTheyDoNotOwnOrHold()
    {
        var scope = Scope(OrgRole.Member);
        Assert.Empty(scope.ViewDepartmentIds);
        AssertDecision(scope.Decide(Personal(Root)), false, false, AccessReason.None);
        AssertDecision(scope.Decide(Automation(Root)), false, false, AccessReason.None);
    }

    [Fact]
    public void L6_UnknownDepartmentIsInvisible()
    {
        AssertDecision(Scope(OrgRole.Manager, manages: [Root]).Decide(Personal(Guid.NewGuid())), false, false, AccessReason.None);
    }
}
