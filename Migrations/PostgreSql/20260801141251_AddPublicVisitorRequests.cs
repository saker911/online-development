using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddPublicVisitorRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RequestSource",
                table: "Visits",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Internal");

            migrationBuilder.AddColumn<DateTime>(
                name: "RequestedAtUtc",
                table: "Visits",
                type: "timestamp without time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequestSource",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "RequestedAtUtc",
                table: "Visits");
        }
    }
}
