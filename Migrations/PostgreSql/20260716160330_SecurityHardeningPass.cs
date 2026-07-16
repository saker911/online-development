using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class SecurityHardeningPass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SignupExpiresAtUtc",
                table: "Tenants",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SignatureImageContentType",
                table: "AdministrationSettings",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<byte[]>(
                name: "SignatureImageData",
                table: "AdministrationSettings",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExternalUserLogins",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "default"),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Issuer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EmailAtLinkTime = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalUserLogins", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LoginAttemptRecords",
                columns: table => new
                {
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    FailureCount = table.Column<int>(type: "integer", nullable: false),
                    WindowStartedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    BlockedUntilUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginAttemptRecords", x => x.KeyHash);
                });

            migrationBuilder.CreateTable(
                name: "SignupAttemptRecords",
                columns: table => new
                {
                    KeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SuccessCount = table.Column<int>(type: "integer", nullable: false),
                    WindowStartedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignupAttemptRecords", x => x.KeyHash);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUserLogins_Provider_Issuer_Subject",
                table: "ExternalUserLogins",
                columns: new[] { "Provider", "Issuer", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUserLogins_TenantId",
                table: "ExternalUserLogins",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalUserLogins_TenantId_Username_Provider",
                table: "ExternalUserLogins",
                columns: new[] { "TenantId", "Username", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalUserLogins");

            migrationBuilder.DropTable(
                name: "LoginAttemptRecords");

            migrationBuilder.DropTable(
                name: "SignupAttemptRecords");

            migrationBuilder.DropColumn(
                name: "SignupExpiresAtUtc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SignatureImageContentType",
                table: "AdministrationSettings");

            migrationBuilder.DropColumn(
                name: "SignatureImageData",
                table: "AdministrationSettings");
        }
    }
}
