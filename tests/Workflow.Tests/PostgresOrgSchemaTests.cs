using Microsoft.EntityFrameworkCore;
using Npgsql;
using Workflow.Api.Data;
using Workflow.Api.Models;
using Workflow.Api.Services;

namespace Workflow.Tests;

public class PostgresOrgSchemaTests : IAsyncLifetime
{
    private readonly PostgresTestDatabase database = new();
    private readonly DateTimeOffset now = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);
    private readonly Guid org = Guid.NewGuid();
    private readonly Guid otherOrg = Guid.NewGuid();
    private readonly Guid boss = Guid.NewGuid(), lead = Guid.NewGuid(), member = Guid.NewGuid(), outsider = Guid.NewGuid();
    private Department root = null!, sales = null!, salesEast = null!, salesEastRetail = null!, support = null!, otherRoot = null!;

    public Task InitializeAsync() => database.InitializeAsync();
    public Task DisposeAsync() => database.DisposeAsync();

    private OrgHierarchyService Hierarchy(WorkflowDbContext db) => new(db, TimeProvider.System);

    // root(boss) > sales(no manager) > salesEast(lead) > salesEastRetail ; root > support
    private async Task SeedAsync()
    {
        await database.MigrateAsync();
        await using var db = database.CreateContext();
        db.Organizations.AddRange(
            new Organization { Id = org, Name = "Org", Slug = "org-" + org.ToString("N")[..8], CreatedAt = now },
            new Organization { Id = otherOrg, Name = "Other", Slug = "other-" + otherOrg.ToString("N")[..8], CreatedAt = now });
        foreach (var (id, name) in new[] { (boss, "Boss"), (lead, "Lead"), (member, "Member"), (outsider, "Outsider") })
            db.Users.Add(new AppUser { Id = id, DisplayName = name, Email = $"{id:N}@example.test", CreatedAt = now });
        await db.SaveChangesAsync();

        var hierarchy = Hierarchy(db);
        root = await hierarchy.CreateDepartmentAsync(org, "Head office", null);
        sales = await hierarchy.CreateDepartmentAsync(org, "Sales", root.Id);
        salesEast = await hierarchy.CreateDepartmentAsync(org, "East", sales.Id);
        salesEastRetail = await hierarchy.CreateDepartmentAsync(org, "Retail", salesEast.Id);
        support = await hierarchy.CreateDepartmentAsync(org, "Support", root.Id);
        otherRoot = await hierarchy.CreateDepartmentAsync(otherOrg, "Other head office", null);

        db.Memberships.AddRange(
            Membership(org, boss, OrgRole.Admin, root.Id),
            Membership(org, lead, OrgRole.Manager, salesEast.Id),
            Membership(org, member, OrgRole.Member, salesEastRetail.Id),
            Membership(otherOrg, outsider, OrgRole.Admin, otherRoot.Id));
        db.DepartmentMemberships.Add(new DepartmentMembership { OrganizationId = org, DepartmentId = salesEast.Id, UserId = member, CreatedAt = now });
        await db.SaveChangesAsync();
        await hierarchy.SetManagerAsync(root.Id, boss);
        await hierarchy.SetManagerAsync(salesEast.Id, lead);
    }

    private Membership Membership(Guid organization, Guid user, OrgRole role, Guid department) => new()
    {
        Id = Guid.NewGuid(), OrganizationId = organization, UserId = user, Role = role,
        IsActive = true, PrimaryDepartmentId = department, CreatedAt = now
    };

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task MigrationsApplyToEmptyDatabase()
    {
        await database.MigrateAsync();
        await using var db = database.CreateContext();
        var tables = await db.Database.SqlQueryRaw<string>(
            "SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'").ToListAsync();
        foreach (var table in new[] { "Organizations", "Users", "Departments", "Memberships", "DepartmentMemberships",
                     "AccessExceptions", "ActingManagerDelegations", "PersonalTasks", "AutomationTasks", "AutomationRuns" })
            Assert.Contains(table, tables);
    }

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task SubtreeAncestorsTeamAndManagerFallback()
    {
        await SeedAsync();
        await using var db = database.CreateContext();
        var hierarchy = Hierarchy(db);

        Assert.Equal([sales.Id, salesEast.Id, salesEastRetail.Id], (await hierarchy.GetSubtreeAsync(sales.Id)).Select(x => x.Id));
        Assert.Equal([support.Id], (await hierarchy.GetSubtreeAsync(support.Id)).Select(x => x.Id));
        Assert.Equal(6, (await hierarchy.GetSubtreeAsync(root.Id)).Count + 1); // other org's root is excluded
        Assert.Equal([salesEast.Id, sales.Id, root.Id], (await hierarchy.GetAncestorsAsync(salesEastRetail.Id)).Select(x => x.Id));
        Assert.Equal(3, (await db.Departments.SingleAsync(x => x.Id == salesEastRetail.Id)).Depth);

        var team = await hierarchy.GetTeamAsync(salesEast.Id);
        Assert.Equal([(lead, true), (member, false)], team.Select(x => (x.UserId, x.IsPrimaryDepartment)).OrderBy(x => x.Item2 ? 0 : 1));
        Assert.Equal([member], (await hierarchy.GetTeamAsync(salesEastRetail.Id)).Select(x => x.UserId));

        Assert.Equal(new EffectiveManager(lead, salesEast.Id, true), await hierarchy.GetEffectiveManagerAsync(salesEastRetail.Id));
        Assert.Equal(new EffectiveManager(lead, salesEast.Id, false), await hierarchy.GetEffectiveManagerAsync(salesEast.Id));
        Assert.Equal(new EffectiveManager(boss, root.Id, true), await hierarchy.GetEffectiveManagerAsync(sales.Id));

        // A deactivated manager no longer counts; the nearest active ancestor manager takes over.
        await db.Memberships.Where(x => x.UserId == lead).ExecuteUpdateAsync(x => x.SetProperty(m => m.IsActive, false));
        Assert.Equal(new EffectiveManager(boss, root.Id, true), await hierarchy.GetEffectiveManagerAsync(salesEastRetail.Id));
        Assert.DoesNotContain(lead, (await hierarchy.GetTeamAsync(salesEast.Id)).Select(x => x.UserId));
    }

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task MovesRewriteSubtreePathsAndCyclesAreRejected()
    {
        await SeedAsync();
        await using var db = database.CreateContext();
        var hierarchy = Hierarchy(db);

        var self = await Assert.ThrowsAsync<HierarchyException>(() => hierarchy.MoveDepartmentAsync(sales.Id, sales.Id));
        Assert.Contains("cannot be moved under itself", self.Message);
        await Assert.ThrowsAsync<HierarchyException>(() => hierarchy.MoveDepartmentAsync(sales.Id, salesEastRetail.Id));
        await Assert.ThrowsAsync<HierarchyException>(() => hierarchy.MoveDepartmentAsync(sales.Id, otherRoot.Id));
        await Assert.ThrowsAsync<HierarchyException>(() => hierarchy.CreateDepartmentAsync(org, "Cross", otherRoot.Id));

        await hierarchy.MoveDepartmentAsync(salesEast.Id, support.Id);
        Assert.Equal([support.Id, salesEast.Id, salesEastRetail.Id], (await hierarchy.GetSubtreeAsync(support.Id)).Select(x => x.Id));
        Assert.Equal([sales.Id], (await hierarchy.GetSubtreeAsync(sales.Id)).Select(x => x.Id));
        var retail = await db.Departments.AsNoTracking().SingleAsync(x => x.Id == salesEastRetail.Id);
        Assert.Equal($"/{root.Id:N}/{support.Id:N}/{salesEast.Id:N}/{salesEastRetail.Id:N}/", retail.Path);
        Assert.Equal(3, retail.Depth);
        Assert.Equal(support.Id, (await db.Departments.AsNoTracking().SingleAsync(x => x.Id == salesEast.Id)).ParentId);

        await hierarchy.MoveDepartmentAsync(salesEast.Id, null);
        Assert.Equal(0, (await db.Departments.AsNoTracking().SingleAsync(x => x.Id == salesEast.Id)).Depth);
        Assert.Equal([salesEast.Id], (await hierarchy.GetAncestorsAsync(salesEastRetail.Id)).Select(x => x.Id));
    }

    [PostgresFact]
    [Trait("Category", "PostgreSQL")]
    public async Task ForeignKeysKeepEverythingInsideOneOrganization()
    {
        await SeedAsync();
        await using var db = database.CreateContext();

        // Task in another organization's department, owner or assignee from another organization.
        foreach (var (department, owner, assignee) in new (Guid, Guid, Guid?)[]
                 {
                     (otherRoot.Id, boss, null), (root.Id, outsider, null), (root.Id, boss, outsider)
                 })
        {
            db.ChangeTracker.Clear();
            db.PersonalTasks.Add(new PersonalTask
            {
                Id = Guid.NewGuid(), OrganizationId = org, DepartmentId = department, OwnerUserId = owner,
                AssigneeUserId = assignee, Title = "Cross-org", Status = PersonalTaskStatus.ToDo, CreatedAt = now, UpdatedAt = now
            });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        }

        // Manager and access exceptions must also come from the same organization.
        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<HierarchyException>(() => Hierarchy(db).SetManagerAsync(root.Id, outsider));
        db.ChangeTracker.Clear();
        db.AccessExceptions.Add(new AccessException
        {
            Id = Guid.NewGuid(), OrganizationId = org, UserId = outsider, DepartmentId = root.Id,
            Kind = AccessExceptionKind.Grant, Level = AccessLevel.View, CreatedAt = now
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        // A deny never carries a level; a grant always does.
        db.ChangeTracker.Clear();
        db.AccessExceptions.Add(new AccessException
        {
            Id = Guid.NewGuid(), OrganizationId = org, UserId = member, DepartmentId = root.Id,
            Kind = AccessExceptionKind.Deny, Level = AccessLevel.Edit, CreatedAt = now
        });
        var check = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(check.InnerException).SqlState);

        // Valid task in the right organization is accepted.
        db.ChangeTracker.Clear();
        db.PersonalTasks.Add(new PersonalTask
        {
            Id = Guid.NewGuid(), OrganizationId = org, DepartmentId = salesEastRetail.Id, OwnerUserId = member,
            AssigneeUserId = lead, Title = "Valid", Status = PersonalTaskStatus.ToDo, CreatedAt = now, UpdatedAt = now
        });
        await db.SaveChangesAsync();
    }
}
