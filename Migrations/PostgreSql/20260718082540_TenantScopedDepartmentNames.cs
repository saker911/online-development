using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class TenantScopedDepartmentNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Departments_Name",
                table: "Departments");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_TenantId_Name",
                table: "Departments",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.Sql(
                """
                WITH tenant_departments AS (
                    SELECT
                        tenant."TenantId",
                        COALESCE(
                            (
                                SELECT NULLIF(BTRIM(settings."DepartmentName"), '')
                                FROM "AdministrationSettings" AS settings
                                WHERE settings."TenantId" = tenant."TenantId"
                                ORDER BY settings."Id"
                                LIMIT 1
                            ),
                            'الإدارة العامة'
                        ) AS "DepartmentName"
                    FROM "Tenants" AS tenant
                )
                INSERT INTO "Departments" (
                    "TenantId",
                    "Name",
                    "ManagerUsername",
                    "ManagerDisplayName",
                    "IsActive"
                )
                SELECT
                    source."TenantId",
                    source."DepartmentName",
                    '',
                    '',
                    TRUE
                FROM tenant_departments AS source
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM "Departments" AS department
                    WHERE department."TenantId" = source."TenantId"
                      AND department."Name" = source."DepartmentName"
                );
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Departments_TenantId_Name",
                table: "Departments");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);
        }
    }
}
