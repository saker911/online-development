using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddPlatformSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProviderName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CommercialRegistration = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    VatNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RegisteredAddress = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    City = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Country = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OfficialEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SupportPhone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WhatsAppNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    WorkingHours = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DataHostingLocation = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BackupPolicy = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlatformSettings");
        }
    }
}
