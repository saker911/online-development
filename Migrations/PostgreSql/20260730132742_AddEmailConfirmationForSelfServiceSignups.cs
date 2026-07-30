using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddEmailConfirmationForSelfServiceSignups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsEmailConfirmed",
                table: "UserAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAccounts_TenantId_Email",
                table: "UserAccounts",
                columns: new[] { "TenantId", "Email" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserAccounts_TenantId_Email",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "IsEmailConfirmed",
                table: "UserAccounts");
        }
    }
}
