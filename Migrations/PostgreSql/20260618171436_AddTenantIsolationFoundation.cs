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
            var tenantScopedTables = new[]
            {
                "Visits",
                "VisitCompanions",
                "UserActivities",
                "UserAccounts",
                "SessionRecords",
                "Permits",
                "PermitActivities",
                "DisplaySecuritySettings",
                "DisplayDevices",
                "Departments",
                "Delegations",
                "DelegationPermissions",
                "AuditLogs",
                "AdministrationSettings",
            };

            foreach (var table in tenantScopedTables)
            {
                migrationBuilder.Sql(
                    $"""
                    ALTER TABLE "{table}"
                    ADD COLUMN IF NOT EXISTS "TenantId" character varying(64) NOT NULL DEFAULT 'default';
                    """
                );
            }

            migrationBuilder.Sql(
                """
                CREATE TABLE IF NOT EXISTS "Tenants" (
                    "TenantId" character varying(64) NOT NULL,
                    "Name" character varying(256) NOT NULL,
                    "Slug" character varying(256) NOT NULL,
                    "IsActive" boolean NOT NULL,
                    "CreatedAtUtc" timestamp without time zone NOT NULL,
                    CONSTRAINT "PK_Tenants" PRIMARY KEY ("TenantId")
                );
                """
            );

            migrationBuilder.Sql(
                """
                INSERT INTO "Tenants" ("TenantId", "Name", "Slug", "IsActive", "CreatedAtUtc")
                VALUES ('default', 'الجهة الافتراضية', 'default', TRUE, TIMESTAMP '2026-06-18 00:00:00')
                ON CONFLICT ("TenantId") DO NOTHING;
                """
            );

            foreach (var table in tenantScopedTables)
            {
                migrationBuilder.Sql(
                    $"""
                    CREATE INDEX IF NOT EXISTS "IX_{table}_TenantId"
                    ON "{table}" ("TenantId");
                    """
                );
            }

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_Tenants_Slug"
                ON "Tenants" ("Slug");
                """
            );
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
