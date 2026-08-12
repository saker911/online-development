using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddSiteEntrancesAndDeviceAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkplaceSiteEntranceId",
                table: "DisplayDevices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WorkplaceSiteId",
                table: "DisplayDevices",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WorkplaceSiteEntrances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "default"),
                    WorkplaceSiteId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LocationDescription = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkplaceSiteEntrances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkplaceSiteEntrances_WorkplaceSites_WorkplaceSiteId",
                        column: x => x.WorkplaceSiteId,
                        principalTable: "WorkplaceSites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DisplayDevices_WorkplaceSiteEntranceId",
                table: "DisplayDevices",
                column: "WorkplaceSiteEntranceId");

            migrationBuilder.CreateIndex(
                name: "IX_DisplayDevices_WorkplaceSiteId",
                table: "DisplayDevices",
                column: "WorkplaceSiteId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkplaceSiteEntrances_TenantId",
                table: "WorkplaceSiteEntrances",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkplaceSiteEntrances_TenantId_WorkplaceSiteId_Code",
                table: "WorkplaceSiteEntrances",
                columns: new[] { "TenantId", "WorkplaceSiteId", "Code" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkplaceSiteEntrances_TenantId_WorkplaceSiteId_Name",
                table: "WorkplaceSiteEntrances",
                columns: new[] { "TenantId", "WorkplaceSiteId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkplaceSiteEntrances_WorkplaceSiteId",
                table: "WorkplaceSiteEntrances",
                column: "WorkplaceSiteId");

            migrationBuilder.AddForeignKey(
                name: "FK_DisplayDevices_WorkplaceSiteEntrances_WorkplaceSiteEntrance~",
                table: "DisplayDevices",
                column: "WorkplaceSiteEntranceId",
                principalTable: "WorkplaceSiteEntrances",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_DisplayDevices_WorkplaceSites_WorkplaceSiteId",
                table: "DisplayDevices",
                column: "WorkplaceSiteId",
                principalTable: "WorkplaceSites",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DisplayDevices_WorkplaceSiteEntrances_WorkplaceSiteEntrance~",
                table: "DisplayDevices");

            migrationBuilder.DropForeignKey(
                name: "FK_DisplayDevices_WorkplaceSites_WorkplaceSiteId",
                table: "DisplayDevices");

            migrationBuilder.DropTable(
                name: "WorkplaceSiteEntrances");

            migrationBuilder.DropIndex(
                name: "IX_DisplayDevices_WorkplaceSiteEntranceId",
                table: "DisplayDevices");

            migrationBuilder.DropIndex(
                name: "IX_DisplayDevices_WorkplaceSiteId",
                table: "DisplayDevices");

            migrationBuilder.DropColumn(
                name: "WorkplaceSiteEntranceId",
                table: "DisplayDevices");

            migrationBuilder.DropColumn(
                name: "WorkplaceSiteId",
                table: "DisplayDevices");
        }
    }
}
