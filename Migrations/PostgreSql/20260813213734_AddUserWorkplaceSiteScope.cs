using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddUserWorkplaceSiteScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkplaceSiteId",
                table: "UserAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "UserAccounts" AS account
                SET "WorkplaceSiteId" = site."Id"
                FROM (
                    SELECT "TenantId", MIN("Id") AS "Id"
                    FROM "WorkplaceSites"
                    WHERE "IsActive" = TRUE
                    GROUP BY "TenantId"
                ) AS site
                WHERE account."TenantId" = site."TenantId"
                  AND account."WorkplaceSiteId" IS NULL
                  AND account."IsSuperAdmin" = FALSE
                  AND account."Role" <> 'GeneralManager';
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_TenantId_WorkplaceSiteId",
                table: "UserAccounts",
                columns: new[] { "TenantId", "WorkplaceSiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_WorkplaceSiteId",
                table: "UserAccounts",
                column: "WorkplaceSiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserAccounts_WorkplaceSites_WorkplaceSiteId",
                table: "UserAccounts",
                column: "WorkplaceSiteId",
                principalTable: "WorkplaceSites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserAccounts_WorkplaceSites_WorkplaceSiteId",
                table: "UserAccounts");

            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_TenantId_WorkplaceSiteId",
                table: "UserAccounts");

            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_WorkplaceSiteId",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "WorkplaceSiteId",
                table: "UserAccounts");
        }
    }
}
