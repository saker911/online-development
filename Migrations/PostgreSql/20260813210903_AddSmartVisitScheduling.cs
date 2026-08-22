using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddSmartVisitScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RequestedVisitDate",
                table: "Visits",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ServiceDurationMinutes",
                table: "Visits",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<string>(
                name: "ServiceOperatorDisplayName",
                table: "Visits",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ServiceOperatorUsername",
                table: "Visits",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(
                "UPDATE \"Visits\" SET \"RequestedVisitDate\" = \"VisitDate\" WHERE \"RequestedVisitDate\" IS NULL;"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Visits_TenantId_DepartmentId_VisitDate",
                table: "Visits",
                columns: new[] { "TenantId", "DepartmentId", "VisitDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Visits_TenantId_DepartmentId_VisitDate",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "RequestedVisitDate",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "ServiceDurationMinutes",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "ServiceOperatorDisplayName",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "ServiceOperatorUsername",
                table: "Visits");
        }
    }
}
