using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddVisitQueueWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CalledAtUtc",
                table: "Visits",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QueueCompletedAtUtc",
                table: "Visits",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QueueStatus",
                table: "Visits",
                type: "character varying(24)",
                maxLength: 24,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "QueuedAtUtc",
                table: "Visits",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ServiceStartedAtUtc",
                table: "Visits",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Visits_TenantId_QueueStatus",
                table: "Visits",
                columns: new[] { "TenantId", "QueueStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Visits_TenantId_QueueStatus",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "CalledAtUtc",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "QueueCompletedAtUtc",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "QueueStatus",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "QueuedAtUtc",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "ServiceStartedAtUtc",
                table: "Visits");
        }
    }
}
