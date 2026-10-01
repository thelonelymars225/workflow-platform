using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Workflow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationsDepartmentsAndAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tasks gain required organization/department columns. Earlier slices had no API that created tasks,
            // so the tables are expected to be empty; rows left by manual inserts would fail the new foreign keys.
            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "PersonalTasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "PersonalTasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "AutomationTasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "OrganizationId",
                table: "AutomationTasks",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccessExceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Level = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessExceptions", x => x.Id);
                    table.CheckConstraint("CK_AccessExceptions_Kind", "\"Kind\" IN ('Grant', 'Deny')");
                    table.CheckConstraint("CK_AccessExceptions_Level", "(\"Kind\" = 'Grant' AND \"Level\" IN ('View', 'Edit')) OR (\"Kind\" = 'Deny' AND \"Level\" IS NULL)");
                    table.CheckConstraint("CK_AccessExceptions_Window", "\"StartsAt\" IS NULL OR \"EndsAt\" IS NULL OR \"StartsAt\" < \"EndsAt\"");
                });

            migrationBuilder.CreateTable(
                name: "ActingManagerDelegations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    DelegateUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DelegatorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActingManagerDelegations", x => x.Id);
                    table.CheckConstraint("CK_ActingManagerDelegations_Window", "\"StartsAt\" < \"EndsAt\"");
                });

            migrationBuilder.CreateTable(
                name: "DepartmentMemberships",
                columns: table => new
                {
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentMemberships", x => new { x.DepartmentId, x.UserId });
                });

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Path = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false),
                    ManagerUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                    table.UniqueConstraint("AK_Departments_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Departments_Depth", "\"Depth\" >= 0");
                    table.CheckConstraint("CK_Departments_NotOwnParent", "\"ParentId\" IS NULL OR \"ParentId\" <> \"Id\"");
                    table.ForeignKey(
                        name: "FK_Departments_Departments_OrganizationId_ParentId",
                        columns: x => new { x.OrganizationId, x.ParentId },
                        principalTable: "Departments",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Departments_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PrimaryDepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeactivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memberships", x => x.Id);
                    table.UniqueConstraint("AK_Memberships_OrganizationId_UserId", x => new { x.OrganizationId, x.UserId });
                    table.CheckConstraint("CK_Memberships_Role", "\"Role\" IN ('Member', 'Manager', 'Admin')");
                    table.ForeignKey(
                        name: "FK_Memberships_Departments_OrganizationId_PrimaryDepartmentId",
                        columns: x => new { x.OrganizationId, x.PrimaryDepartmentId },
                        principalTable: "Departments",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Memberships_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Memberships_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalTasks_OrganizationId_AssigneeUserId",
                table: "PersonalTasks",
                columns: new[] { "OrganizationId", "AssigneeUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalTasks_OrganizationId_DepartmentId",
                table: "PersonalTasks",
                columns: new[] { "OrganizationId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonalTasks_OrganizationId_OwnerUserId",
                table: "PersonalTasks",
                columns: new[] { "OrganizationId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationTasks_OrganizationId_DepartmentId",
                table: "AutomationTasks",
                columns: new[] { "OrganizationId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationTasks_OrganizationId_OwnerUserId",
                table: "AutomationTasks",
                columns: new[] { "OrganizationId", "OwnerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessExceptions_OrganizationId_DepartmentId",
                table: "AccessExceptions",
                columns: new[] { "OrganizationId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_AccessExceptions_OrganizationId_UserId",
                table: "AccessExceptions",
                columns: new[] { "OrganizationId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActingManagerDelegations_OrganizationId_DelegateUserId",
                table: "ActingManagerDelegations",
                columns: new[] { "OrganizationId", "DelegateUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActingManagerDelegations_OrganizationId_DelegatorUserId",
                table: "ActingManagerDelegations",
                columns: new[] { "OrganizationId", "DelegatorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ActingManagerDelegations_OrganizationId_DepartmentId",
                table: "ActingManagerDelegations",
                columns: new[] { "OrganizationId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentMemberships_OrganizationId_DepartmentId",
                table: "DepartmentMemberships",
                columns: new[] { "OrganizationId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentMemberships_OrganizationId_UserId",
                table: "DepartmentMemberships",
                columns: new[] { "OrganizationId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Departments_OrganizationId_ManagerUserId",
                table: "Departments",
                columns: new[] { "OrganizationId", "ManagerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Departments_OrganizationId_ParentId_Name",
                table: "Departments",
                columns: new[] { "OrganizationId", "ParentId", "Name" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Path",
                table: "Departments",
                column: "Path",
                unique: true)
                .Annotation("Npgsql:IndexOperators", new[] { "text_pattern_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_OrganizationId_PrimaryDepartmentId",
                table: "Memberships",
                columns: new[] { "OrganizationId", "PrimaryDepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_UserId",
                table: "Memberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_Slug",
                table: "Organizations",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AutomationTasks_Departments_OrganizationId_DepartmentId",
                table: "AutomationTasks",
                columns: new[] { "OrganizationId", "DepartmentId" },
                principalTable: "Departments",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AutomationTasks_Memberships_OrganizationId_OwnerUserId",
                table: "AutomationTasks",
                columns: new[] { "OrganizationId", "OwnerUserId" },
                principalTable: "Memberships",
                principalColumns: new[] { "OrganizationId", "UserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AutomationTasks_Organizations_OrganizationId",
                table: "AutomationTasks",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PersonalTasks_Departments_OrganizationId_DepartmentId",
                table: "PersonalTasks",
                columns: new[] { "OrganizationId", "DepartmentId" },
                principalTable: "Departments",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PersonalTasks_Memberships_OrganizationId_AssigneeUserId",
                table: "PersonalTasks",
                columns: new[] { "OrganizationId", "AssigneeUserId" },
                principalTable: "Memberships",
                principalColumns: new[] { "OrganizationId", "UserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PersonalTasks_Memberships_OrganizationId_OwnerUserId",
                table: "PersonalTasks",
                columns: new[] { "OrganizationId", "OwnerUserId" },
                principalTable: "Memberships",
                principalColumns: new[] { "OrganizationId", "UserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PersonalTasks_Organizations_OrganizationId",
                table: "PersonalTasks",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AccessExceptions_Departments_OrganizationId_DepartmentId",
                table: "AccessExceptions",
                columns: new[] { "OrganizationId", "DepartmentId" },
                principalTable: "Departments",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AccessExceptions_Memberships_OrganizationId_UserId",
                table: "AccessExceptions",
                columns: new[] { "OrganizationId", "UserId" },
                principalTable: "Memberships",
                principalColumns: new[] { "OrganizationId", "UserId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ActingManagerDelegations_Departments_OrganizationId_Departm~",
                table: "ActingManagerDelegations",
                columns: new[] { "OrganizationId", "DepartmentId" },
                principalTable: "Departments",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ActingManagerDelegations_Memberships_OrganizationId_Delegat~",
                table: "ActingManagerDelegations",
                columns: new[] { "OrganizationId", "DelegateUserId" },
                principalTable: "Memberships",
                principalColumns: new[] { "OrganizationId", "UserId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ActingManagerDelegations_Memberships_OrganizationId_Delega~1",
                table: "ActingManagerDelegations",
                columns: new[] { "OrganizationId", "DelegatorUserId" },
                principalTable: "Memberships",
                principalColumns: new[] { "OrganizationId", "UserId" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DepartmentMemberships_Departments_OrganizationId_Department~",
                table: "DepartmentMemberships",
                columns: new[] { "OrganizationId", "DepartmentId" },
                principalTable: "Departments",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DepartmentMemberships_Memberships_OrganizationId_UserId",
                table: "DepartmentMemberships",
                columns: new[] { "OrganizationId", "UserId" },
                principalTable: "Memberships",
                principalColumns: new[] { "OrganizationId", "UserId" },
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Departments_Memberships_Manager",
                table: "Departments",
                columns: new[] { "OrganizationId", "ManagerUserId" },
                principalTable: "Memberships",
                principalColumns: new[] { "OrganizationId", "UserId" },
                onDelete: ReferentialAction.Restrict);

            // A department's manager membership points back at a department; defer the check to commit
            // so both rows can be inserted in one transaction.
            migrationBuilder.Sql("ALTER TABLE \"Departments\" ALTER CONSTRAINT \"FK_Departments_Memberships_Manager\" DEFERRABLE INITIALLY DEFERRED;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AutomationTasks_Departments_OrganizationId_DepartmentId",
                table: "AutomationTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_AutomationTasks_Memberships_OrganizationId_OwnerUserId",
                table: "AutomationTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_AutomationTasks_Organizations_OrganizationId",
                table: "AutomationTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonalTasks_Departments_OrganizationId_DepartmentId",
                table: "PersonalTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonalTasks_Memberships_OrganizationId_AssigneeUserId",
                table: "PersonalTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonalTasks_Memberships_OrganizationId_OwnerUserId",
                table: "PersonalTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_PersonalTasks_Organizations_OrganizationId",
                table: "PersonalTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_Memberships_Departments_OrganizationId_PrimaryDepartmentId",
                table: "Memberships");

            migrationBuilder.DropTable(
                name: "AccessExceptions");

            migrationBuilder.DropTable(
                name: "ActingManagerDelegations");

            migrationBuilder.DropTable(
                name: "DepartmentMemberships");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "Memberships");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_PersonalTasks_OrganizationId_AssigneeUserId",
                table: "PersonalTasks");

            migrationBuilder.DropIndex(
                name: "IX_PersonalTasks_OrganizationId_DepartmentId",
                table: "PersonalTasks");

            migrationBuilder.DropIndex(
                name: "IX_PersonalTasks_OrganizationId_OwnerUserId",
                table: "PersonalTasks");

            migrationBuilder.DropIndex(
                name: "IX_AutomationTasks_OrganizationId_DepartmentId",
                table: "AutomationTasks");

            migrationBuilder.DropIndex(
                name: "IX_AutomationTasks_OrganizationId_OwnerUserId",
                table: "AutomationTasks");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "PersonalTasks");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "PersonalTasks");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "AutomationTasks");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "AutomationTasks");
        }
    }
}
