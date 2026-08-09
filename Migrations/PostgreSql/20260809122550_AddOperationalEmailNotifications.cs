using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddOperationalEmailNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VisitorEmail",
                table: "Visits",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HolderEmail",
                table: "Permits",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailNotificationOutbox",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "default"),
                    NotificationType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ReferenceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReferenceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeduplicationKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RecipientEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    RecipientName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    HtmlBody = table.Column<string>(type: "text", nullable: false),
                    TextBody = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastAttemptAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LockToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LockedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailNotificationOutbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailNotificationOutbox_Status_NextAttemptAtUtc",
                table: "EmailNotificationOutbox",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailNotificationOutbox_TenantId",
                table: "EmailNotificationOutbox",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_EmailNotificationOutbox_TenantId_DeduplicationKey",
                table: "EmailNotificationOutbox",
                columns: new[] { "TenantId", "DeduplicationKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailNotificationOutbox");

            migrationBuilder.DropColumn(
                name: "VisitorEmail",
                table: "Visits");

            migrationBuilder.DropColumn(
                name: "HolderEmail",
                table: "Permits");
        }
    }
}
