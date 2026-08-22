using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddTrustedLoginDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TrustedLoginDevices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "default"),
                    Username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeviceDescription = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrustedLoginDevices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrustedLoginDevices_TenantId",
                table: "TrustedLoginDevices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TrustedLoginDevices_TenantId_Username_ExpiresAtUtc",
                table: "TrustedLoginDevices",
                columns: new[] { "TenantId", "Username", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TrustedLoginDevices_TokenHash",
                table: "TrustedLoginDevices",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrustedLoginDevices");
        }
    }
}
