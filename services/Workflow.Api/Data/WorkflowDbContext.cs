using Microsoft.EntityFrameworkCore;
using Workflow.Api.Models;

namespace Workflow.Api.Data;

public class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options) : DbContext(options)
{
    public DbSet<WorkflowDefinition> Workflows => Set<WorkflowDefinition>();
    public DbSet<PersonalTask> PersonalTasks => Set<PersonalTask>();
    public DbSet<AutomationTask> AutomationTasks => Set<AutomationTask>();
    public DbSet<AutomationRun> AutomationRuns => Set<AutomationRun>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<DepartmentMembership> DepartmentMemberships => Set<DepartmentMembership>();
    public DbSet<AccessException> AccessExceptions => Set<AccessException>();
    public DbSet<ActingManagerDelegation> ActingManagerDelegations => Set<ActingManagerDelegation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkflowDefinition>(entity =>
        {
            entity.ToTable("Workflows");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
        });

        // Enums are stored as readable strings so manual SQL and seeds stay legible.
        modelBuilder.Entity<PersonalTask>(entity =>
        {
            entity.ToTable("PersonalTasks", table => table.HasCheckConstraint("CK_PersonalTasks_Status",
                "\"Status\" IN ('ToDo', 'InProgress', 'Done', 'Cancelled')"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Department>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.DepartmentId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.DepartmentId });
            entity.HasIndex(x => new { x.OrganizationId, x.OwnerUserId });
            entity.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.AssigneeUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.AssigneeUserId });
        });

        modelBuilder.Entity<AutomationTask>(entity =>
        {
            entity.ToTable("AutomationTasks", table => table.HasCheckConstraint("CK_AutomationTasks_Status",
                "\"Status\" IN ('Active', 'Paused')"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Department>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.DepartmentId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.OwnerUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.DepartmentId });
            entity.HasIndex(x => new { x.OrganizationId, x.OwnerUserId });
            entity.HasMany(x => x.Runs).WithOne().HasForeignKey(x => x.AutomationTaskId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AutomationRun>(entity =>
        {
            entity.ToTable("AutomationRuns", table =>
            {
                table.HasCheckConstraint("CK_AutomationRuns_Status",
                    "\"Status\" IN ('Queued', 'Running', 'Succeeded', 'Failed', 'Retrying')");
                table.HasCheckConstraint("CK_AutomationRuns_Attempt", "\"Attempt\" >= 1");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Error).HasMaxLength(2000);
            entity.HasIndex(x => new { x.AutomationTaskId, x.QueuedAt });
            entity.HasOne<AutomationRun>().WithMany().HasForeignKey(x => x.RetryOfRunId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("Organizations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Slug).IsUnique();
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DisplayName).HasMaxLength(400).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("Departments", table =>
            {
                table.HasCheckConstraint("CK_Departments_Depth", "\"Depth\" >= 0");
                table.HasCheckConstraint("CK_Departments_NotOwnParent", "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\"");
            });
            entity.HasKey(x => x.Id);
            // (OrganizationId, Id) lets every child row reference a department of its own organization only.
            entity.HasAlternateKey(x => new { x.OrganizationId, x.Id });
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Path).HasMaxLength(2000).IsRequired();
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Department>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ParentId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ManagerUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.UserId }).OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName(ManagerForeignKey);
            // Prefix (subtree) searches on the path use LIKE 'prefix%'.
            entity.HasIndex(x => x.Path).IsUnique().HasOperators("text_pattern_ops");
            entity.HasIndex(x => new { x.OrganizationId, x.ParentId, x.Name }).IsUnique().AreNullsDistinct(false);
            entity.HasIndex(x => new { x.OrganizationId, x.ManagerUserId });
        });

        modelBuilder.Entity<Membership>(entity =>
        {
            entity.ToTable("Memberships");
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.OrganizationId, x.UserId });
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            entity.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Department>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PrimaryDepartmentId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.UserId);
            entity.ToTable(table => table.HasCheckConstraint("CK_Memberships_Role", "\"Role\" IN ('Member', 'Manager', 'Admin')"));
        });

        modelBuilder.Entity<DepartmentMembership>(entity =>
        {
            entity.ToTable("DepartmentMemberships");
            entity.HasKey(x => new { x.DepartmentId, x.UserId });
            entity.HasOne<Department>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.DepartmentId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.UserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.UserId }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.UserId });
        });

        modelBuilder.Entity<AccessException>(entity =>
        {
            entity.ToTable("AccessExceptions", table =>
            {
                table.HasCheckConstraint("CK_AccessExceptions_Kind", "\"Kind\" IN ('Grant', 'Deny')");
                // A grant needs a level (Edit implies View); a deny blocks everything and has none.
                table.HasCheckConstraint("CK_AccessExceptions_Level",
                    "(\"Kind\" = 'Grant' AND \"Level\" IN ('View', 'Edit')) OR (\"Kind\" = 'Deny' AND \"Level\" IS NULL)");
                table.HasCheckConstraint("CK_AccessExceptions_Window",
                    "\"StartsAt\" IS NULL OR \"EndsAt\" IS NULL OR \"StartsAt\" < \"EndsAt\"");
            });
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10);
            entity.Property(x => x.Level).HasConversion<string>().HasMaxLength(10);
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.HasOne<Department>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.DepartmentId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.UserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.UserId }).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(x => new { x.OrganizationId, x.UserId });
        });

        modelBuilder.Entity<ActingManagerDelegation>(entity =>
        {
            entity.ToTable("ActingManagerDelegations", table => table.HasCheckConstraint(
                "CK_ActingManagerDelegations_Window", "\"StartsAt\" < \"EndsAt\""));
            entity.HasKey(x => x.Id);
            entity.HasOne<Department>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.DepartmentId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.DelegateUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.UserId }).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Membership>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.DelegatorUserId })
                .HasPrincipalKey(x => new { x.OrganizationId, x.UserId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.OrganizationId, x.DelegateUserId });
            entity.HasIndex(x => new { x.OrganizationId, x.DepartmentId });
        });
    }

    // Deferred so a department and its manager's membership (which points back at a department) can be inserted in one transaction.
    public const string ManagerForeignKey = "FK_Departments_Memberships_Manager";

}
