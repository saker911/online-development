using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VehiclePermitSystemWeb.Data;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260626120000_AddTenantSubscriptionControls")]
    public partial class AddTenantSubscriptionControls : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                ALTER TABLE "Tenants"
                ADD COLUMN IF NOT EXISTS "SubscriptionStatus" character varying(32) NOT NULL DEFAULT 'Active';
                """
            );
            migrationBuilder.Sql(
                """
                ALTER TABLE "Tenants"
                ADD COLUMN IF NOT EXISTS "PlanName" character varying(128) NOT NULL DEFAULT 'أساسية';
                """
            );
            migrationBuilder.Sql(
                """
                ALTER TABLE "Tenants"
                ADD COLUMN IF NOT EXISTS "PrimaryDomain" character varying(256) NOT NULL DEFAULT '';
                """
            );
            migrationBuilder.Sql(
                """
                ALTER TABLE "Tenants"
                ADD COLUMN IF NOT EXISTS "TrialEndsAtUtc" timestamp without time zone NULL;
                """
            );
            migrationBuilder.Sql(
                """
                ALTER TABLE "Tenants"
                ADD COLUMN IF NOT EXISTS "SubscriptionEndsAtUtc" timestamp without time zone NULL;
                """
            );
            migrationBuilder.Sql(
                """
                ALTER TABLE "Tenants"
                ADD COLUMN IF NOT EXISTS "MaxUsers" integer NULL;
                """
            );
            migrationBuilder.Sql(
                """
                ALTER TABLE "Tenants"
                ADD COLUMN IF NOT EXISTS "MaxPermitsPerMonth" integer NULL;
                """
            );
            migrationBuilder.Sql(
                """
                ALTER TABLE "Tenants"
                ADD COLUMN IF NOT EXISTS "MaxVisitsPerMonth" integer NULL;
                """
            );
            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_Tenants_PrimaryDomain"
                ON "Tenants" ("PrimaryDomain");
                """
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Tenants_PrimaryDomain"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Tenants"" DROP COLUMN IF EXISTS ""MaxVisitsPerMonth"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Tenants"" DROP COLUMN IF EXISTS ""MaxPermitsPerMonth"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Tenants"" DROP COLUMN IF EXISTS ""MaxUsers"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Tenants"" DROP COLUMN IF EXISTS ""SubscriptionEndsAtUtc"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Tenants"" DROP COLUMN IF EXISTS ""TrialEndsAtUtc"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Tenants"" DROP COLUMN IF EXISTS ""PrimaryDomain"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Tenants"" DROP COLUMN IF EXISTS ""PlanName"";");
            migrationBuilder.Sql(@"ALTER TABLE ""Tenants"" DROP COLUMN IF EXISTS ""SubscriptionStatus"";");
        }
    }
}
