using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using VehiclePermitSystemWeb.Data;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260713010000_RemoveTenantPrimaryDomain")]
    public partial class RemoveTenantPrimaryDomain : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Tenants_PrimaryDomain"";");
            migrationBuilder.Sql(
                @"ALTER TABLE ""Tenants"" DROP COLUMN IF EXISTS ""PrimaryDomain"";"
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                @"ALTER TABLE ""Tenants"" ADD COLUMN IF NOT EXISTS ""PrimaryDomain"" character varying(256) NOT NULL DEFAULT '';"
            );
            migrationBuilder.Sql(
                @"CREATE INDEX IF NOT EXISTS ""IX_Tenants_PrimaryDomain"" ON ""Tenants"" (""PrimaryDomain"");"
            );
        }
    }
}
