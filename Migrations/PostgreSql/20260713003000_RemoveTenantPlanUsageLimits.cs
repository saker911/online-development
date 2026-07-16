using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VehiclePermitSystemWeb.Data;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260713003000_RemoveTenantPlanUsageLimits")]
    public partial class RemoveTenantPlanUsageLimits : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Tenants"
                SET "MaxUsers" = NULL,
                    "MaxPermitsPerMonth" = NULL,
                    "MaxVisitsPerMonth" = NULL,
                    "PlanName" = CASE "PlanName"
                        WHEN 'زوار' THEN 'اشتراك شهري'
                        WHEN 'تشغيل' THEN 'اشتراك 6 أشهر'
                        WHEN 'مؤسسات' THEN 'اشتراك سنوي'
                        ELSE "PlanName"
                    END;
                """
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
