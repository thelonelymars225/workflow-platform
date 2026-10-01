using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workflow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersonalAndAutomationTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AutomationTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationTasks", x => x.Id);
                    table.CheckConstraint("CK_AutomationTasks_Status", "\"Status\" IN ('Active', 'Paused')");
                });

            migrationBuilder.CreateTable(
                name: "PersonalTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonalTasks", x => x.Id);
                    table.CheckConstraint("CK_PersonalTasks_Status", "\"Status\" IN ('ToDo', 'InProgress', 'Done', 'Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "AutomationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AutomationTaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    RetryOfRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    QueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationRuns", x => x.Id);
                    table.CheckConstraint("CK_AutomationRuns_Attempt", "\"Attempt\" >= 1");
                    table.CheckConstraint("CK_AutomationRuns_Status", "\"Status\" IN ('Queued', 'Running', 'Succeeded', 'Failed', 'Retrying')");
                    table.ForeignKey(
                        name: "FK_AutomationRuns_AutomationRuns_RetryOfRunId",
                        column: x => x.RetryOfRunId,
                        principalTable: "AutomationRuns",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AutomationRuns_AutomationTasks_AutomationTaskId",
                        column: x => x.AutomationTaskId,
                        principalTable: "AutomationTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRuns_AutomationTaskId_QueuedAt",
                table: "AutomationRuns",
                columns: new[] { "AutomationTaskId", "QueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationRuns_RetryOfRunId",
                table: "AutomationRuns",
                column: "RetryOfRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationRuns");

            migrationBuilder.DropTable(
                name: "PersonalTasks");

            migrationBuilder.DropTable(
                name: "AutomationTasks");
        }
    }
}
