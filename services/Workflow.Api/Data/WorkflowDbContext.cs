using Microsoft.EntityFrameworkCore;
using Workflow.Api.Models;

namespace Workflow.Api.Data;

public class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options) : DbContext(options)
{
    public DbSet<WorkflowDefinition> Workflows => Set<WorkflowDefinition>();
    public DbSet<PersonalTask> PersonalTasks => Set<PersonalTask>();
    public DbSet<AutomationTask> AutomationTasks => Set<AutomationTask>();
    public DbSet<AutomationRun> AutomationRuns => Set<AutomationRun>();

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
        });

        modelBuilder.Entity<AutomationTask>(entity =>
        {
            entity.ToTable("AutomationTasks", table => table.HasCheckConstraint("CK_AutomationTasks_Status",
                "\"Status\" IN ('Active', 'Paused')"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
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
    }
}
