using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddVisitorWorkflowSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VisitorWorkflowSettings",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    ShowNationalId = table.Column<bool>(type: "boolean", nullable: false),
                    RequireNationalId = table.Column<bool>(type: "boolean", nullable: false),
                    ShowHostName = table.Column<bool>(type: "boolean", nullable: false),
                    RequireHostName = table.Column<bool>(type: "boolean", nullable: false),
                    ShowVisitLocation = table.Column<bool>(type: "boolean", nullable: false),
                    RequireVisitLocation = table.Column<bool>(type: "boolean", nullable: false),
                    ShowPurpose = table.Column<bool>(type: "boolean", nullable: false),
                    RequirePurpose = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumLeadMinutes = table.Column<int>(type: "integer", nullable: false),
                    MaximumAdvanceDays = table.Column<int>(type: "integer", nullable: false),
                    WelcomeMessage = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitorWorkflowSettings", x => x.TenantId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VisitorWorkflowSettings");
        }
    }
}
