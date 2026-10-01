using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workflow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersonalTaskListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PersonalTasks_Org_CreatedAt_Id",
                table: "PersonalTasks",
                columns: new[] { "OrganizationId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalTasks_Org_Status_CreatedAt_Id",
                table: "PersonalTasks",
                columns: new[] { "OrganizationId", "Status", "CreatedAt", "Id" },
                descending: new[] { false, false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PersonalTasks_Org_CreatedAt_Id",
                table: "PersonalTasks");

            migrationBuilder.DropIndex(
                name: "IX_PersonalTasks_Org_Status_CreatedAt_Id",
                table: "PersonalTasks");
        }
    }
}
