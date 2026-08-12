using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddTenantNotificationCenterAndRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AuditLogRetentionDays",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 365);

            migrationBuilder.AddColumn<int>(
                name: "EmailOutboxRetentionDays",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<bool>(
                name: "FailedOperationAlertsEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastRetentionRunAtUtc",
                table: "Tenants",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotificationCenterEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "NotificationRetentionDays",
                table: "Tenants",
                type: "integer",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.AddColumn<bool>(
                name: "PermitNotificationsEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "SecurityAlertsEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "UnauthorizedMovementAlertsEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "VisitNotificationsEnabled",
                table: "Tenants",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "InAppNotifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "default"),
                    RecipientUsername = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ActionUrl = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    DismissedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InAppNotifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InAppNotifications_TenantId",
                table: "InAppNotifications",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_InAppNotifications_TenantId_RecipientUsername_DismissedAtUt~",
                table: "InAppNotifications",
                columns: new[] { "TenantId", "RecipientUsername", "DismissedAtUtc", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InAppNotifications_TenantId_RecipientUsername_SourceKey",
                table: "InAppNotifications",
                columns: new[] { "TenantId", "RecipientUsername", "SourceKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InAppNotifications");

            migrationBuilder.DropColumn(
                name: "AuditLogRetentionDays",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "EmailOutboxRetentionDays",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "FailedOperationAlertsEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LastRetentionRunAtUtc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "NotificationCenterEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "NotificationRetentionDays",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PermitNotificationsEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SecurityAlertsEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "UnauthorizedMovementAlertsEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "VisitNotificationsEnabled",
                table: "Tenants");
        }
    }
}
