using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class RemoveLegacyClientIpRanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AllowedClientIpRanges",
                table: "AdministrationSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AllowedClientIpRanges",
                table: "AdministrationSettings",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
