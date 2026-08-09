using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddTenantServiceControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GateServiceEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PermitsServiceEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "QueueServiceEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SelfServiceEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "VisitsServiceEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GateServiceEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PermitsServiceEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "QueueServiceEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SelfServiceEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "VisitsServiceEnabled",
                table: "Tenants");
        }
    }
}
