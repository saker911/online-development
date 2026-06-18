using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddTenantIsolationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Visits",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "VisitCompanions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "UserActivities",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "UserAccounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "SessionRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Permits",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "PermitActivities",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "DisplaySecuritySettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "DisplayDevices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Departments",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "Delegations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "DelegationPermissions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AuditLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AdministrationSettings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Slug = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.TenantId);
                });

            migrationBuilder.InsertData(
                table: "Tenants",
                columns: new[] { "TenantId", "Name", "Slug", "IsActive", "CreatedAtUtc" },
                values: new object[]
                {
                    "default",
                    "الجهة الافتراضية",
                    "default",
                    true,
                    new DateTime(2026, 6, 18, 0, 0, 0, DateTimeKind.Unspecified),
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Visits_TenantId",
                table: "Visits",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitCompanions_TenantId",
                table: "VisitCompanions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserActivities_TenantId",
                table: "UserActivities",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_TenantId",
                table: "UserAccounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionRecords_TenantId",
                table: "SessionRecords",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_TenantId",
                table: "Permits",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PermitActivities_TenantId",
                table: "PermitActivities",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DisplaySecuritySettings_TenantId",
                table: "DisplaySecuritySettings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DisplayDevices_TenantId",
                table: "DisplayDevices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_TenantId",
                table: "Departments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_TenantId",
                table: "Delegations",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DelegationPermissions_TenantId",
                table: "DelegationPermissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AdministrationSettings_TenantId",
                table: "AdministrationSettings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Slug",
                table: "Tenants",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Visits_TenantId",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_VisitCompanions_TenantId",
                table: "VisitCompanions");

            migrationBuilder.DropIndex(
                name: "IX_UserActivities_TenantId",
                table: "UserActivities");

            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_TenantId",
                table: "UserAccounts");

            migrationBuilder.DropIndex(
                name: "IX_SessionRecords_TenantId",
                table: "SessionRecords");

            migrationBuilder.DropIndex(
                name: "IX_Permits_TenantId",
                table: "Permits");

            migrationBuilder.DropIndex(
                name: "IX_PermitActivities_TenantId",
                table: "PermitActivities");

            migrationBuilder.DropIndex(
                name: "IX_DisplaySecuritySettings_TenantId",
                table: "DisplaySecuritySettings");

            migrationBuilder.DropIndex(
                name: "IX_DisplayDevices_TenantId",
                table: "DisplayDevices");

            migrationBuilder.DropIndex(
                name: "IX_Departments_TenantId",
                table: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Delegations_TenantId",
                table: "Delegations");

            migrationBuilder.DropIndex(
                name: "IX_DelegationPermissions_TenantId",
                table: "DelegationPermissions");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AdministrationSettings_TenantId",
                table: "AdministrationSettings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "VisitCompanions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "UserActivities");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SessionRecords");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Permits");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PermitActivities");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DisplaySecuritySettings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DisplayDevices");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Delegations");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "DelegationPermissions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AdministrationSettings");
        }
    }
}
