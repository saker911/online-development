using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddOperationalSitesDeviceHealthAndEmergency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "GateEnabled",
                table: "WorkplaceSites",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PermitsEnabled",
                table: "WorkplaceSites",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "QueueEnabled",
                table: "WorkplaceSites",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SelfServiceEnabled",
                table: "WorkplaceSites",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "VisitsEnabled",
                table: "WorkplaceSites",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkplaceSiteEntranceId",
                table: "Visits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkplaceSiteId",
                table: "Visits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkplaceSiteEntranceId",
                table: "Permits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkplaceSiteId",
                table: "Permits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AppVersion",
                table: "DisplayDevices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "AppliedConfigurationVersion",
                table: "DisplayDevices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BatteryLevel",
                table: "DisplayDevices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CameraStatus",
                table: "DisplayDevices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ConfigurationVersion",
                table: "DisplayDevices",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "LastHealthError",
                table: "DisplayDevices",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHealthReportedAtUtc",
                table: "DisplayDevices",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NetworkStatus",
                table: "DisplayDevices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                table: "DisplayDevices",
                type: "character varying(96)",
                maxLength: 96,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "EmergencySessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "default"),
                    WorkplaceSiteId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    StartedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    EndedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencySessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmergencySessions_WorkplaceSites_WorkplaceSiteId",
                        column: x => x.WorkplaceSiteId,
                        principalTable: "WorkplaceSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EmergencySessionMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "default"),
                    EmergencySessionId = table.Column<int>(type: "integer", nullable: false),
                    PersonProfileId = table.Column<long>(type: "bigint", nullable: false),
                    FullName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PersonType = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Reference = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Location = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmergencySessionMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmergencySessionMembers_EmergencySessions_EmergencySessionId",
                        column: x => x.EmergencySessionId,
                        principalTable: "EmergencySessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_TenantId_WorkplaceSiteId",
                table: "Visits",
                columns: new[] { "TenantId", "WorkplaceSiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Visits_WorkplaceSiteEntranceId",
                table: "Visits",
                column: "WorkplaceSiteEntranceId");

            migrationBuilder.CreateIndex(
                name: "IX_Visits_WorkplaceSiteId",
                table: "Visits",
                column: "WorkplaceSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_TenantId_WorkplaceSiteId",
                table: "Permits",
                columns: new[] { "TenantId", "WorkplaceSiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_Permits_WorkplaceSiteEntranceId",
                table: "Permits",
                column: "WorkplaceSiteEntranceId");

            migrationBuilder.CreateIndex(
                name: "IX_Permits_WorkplaceSiteId",
                table: "Permits",
                column: "WorkplaceSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencySessionMembers_EmergencySessionId",
                table: "EmergencySessionMembers",
                column: "EmergencySessionId");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencySessionMembers_TenantId",
                table: "EmergencySessionMembers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencySessionMembers_TenantId_EmergencySessionId_Status",
                table: "EmergencySessionMembers",
                columns: new[] { "TenantId", "EmergencySessionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmergencySessions_TenantId",
                table: "EmergencySessions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EmergencySessions_TenantId_WorkplaceSiteId_Status",
                table: "EmergencySessions",
                columns: new[] { "TenantId", "WorkplaceSiteId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EmergencySessions_WorkplaceSiteId",
                table: "EmergencySessions",
                column: "WorkplaceSiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_Permits_WorkplaceSiteEntrances_WorkplaceSiteEntranceId",
                table: "Permits",
                column: "WorkplaceSiteEntranceId",
                principalTable: "WorkplaceSiteEntrances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Permits_WorkplaceSites_WorkplaceSiteId",
                table: "Permits",
                column: "WorkplaceSiteId",
                principalTable: "WorkplaceSites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Visits_WorkplaceSiteEntrances_WorkplaceSiteEntranceId",
                table: "Visits",
                column: "WorkplaceSiteEntranceId",
                principalTable: "WorkplaceSiteEntrances",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Visits_WorkplaceSites_WorkplaceSiteId",
                table: "Visits",
                column: "WorkplaceSiteId",
                principalTable: "WorkplaceSites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Permits_WorkplaceSiteEntrances_WorkplaceSiteEntranceId",
                table: "Permits");

            migrationBuilder.DropForeignKey(
                name: "FK_Permits_WorkplaceSites_WorkplaceSiteId",
                table: "Permits");

            migrationBuilder.DropForeignKey(
                name: "FK_Visits_WorkplaceSiteEntrances_WorkplaceSiteEntranceId",
                table: "Visits");

            migrationBuilder.DropForeignKey(
                name: "FK_Visits_WorkplaceSites_WorkplaceSiteId",
                table: "Visits");

            migrationBuilder.DropTable(
                name: "EmergencySessionMembers");

            migrationBuilder.DropTable(
                name: "EmergencySessions");

            migrationBuilder.DropIndex(
                name: "IX_Visits_TenantId_WorkplaceSiteId",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_Visits_WorkplaceSiteEntranceId",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_Visits_WorkplaceSiteId",
                table: "Visits");

            migrationBuilder.DropIndex(
                name: "IX_Permits_TenantId_WorkplaceSiteId",
                table: "Permits");

            migrationBuilder.DropIndex(
                name: "IX_Permits_WorkplaceSiteEntranceId",
                table: "Permits");

            migrationBuilder.DropIndex(
                name: "IX_Permits_WorkplaceSiteId",
                table: "Permits");

            migrationBuilder.DropColumn(
                name: "GateEnabled",
                table: "WorkplaceSites");

            migrationBuilder.DropColumn(
                name: "PermitsEnabled",
                table: "WorkplaceSites");

            migrationBuilder.DropColumn(
                name: "QueueEnabled",
                table: "WorkplaceSites");

            migrationBuilder.DropColumn(
                name: "SelfServiceEnabled",
                table: "WorkplaceSites");

            migrationBuilder.DropColumn(
                name: "VisitsEnabled",
                table: "WorkplaceSites");

            migrationBuilder.DropColumn(
                name: "WorkplaceSiteEntranceId",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "WorkplaceSiteId",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "WorkplaceSiteEntranceId",
                table: "Permits");

            migrationBuilder.DropColumn(
                name: "WorkplaceSiteId",
                table: "Permits");

            migrationBuilder.DropColumn(
                name: "AppVersion",
                table: "DisplayDevices");

            migrationBuilder.DropColumn(
                name: "AppliedConfigurationVersion",
                table: "DisplayDevices");

            migrationBuilder.DropColumn(
                name: "BatteryLevel",
                table: "DisplayDevices");

            migrationBuilder.DropColumn(
                name: "CameraStatus",
                table: "DisplayDevices");

            migrationBuilder.DropColumn(
                name: "ConfigurationVersion",
                table: "DisplayDevices");

            migrationBuilder.DropColumn(
                name: "LastHealthError",
                table: "DisplayDevices");

            migrationBuilder.DropColumn(
                name: "LastHealthReportedAtUtc",
                table: "DisplayDevices");

            migrationBuilder.DropColumn(
                name: "NetworkStatus",
                table: "DisplayDevices");

            migrationBuilder.DropColumn(
                name: "Platform",
                table: "DisplayDevices");
        }
    }
}
