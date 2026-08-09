using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddSubscriptionPricingManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SignupPlanCode",
                table: "Tenants",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SignupPlanDurationMonths",
                table: "Tenants",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SignupPlanPrice",
                table: "Tenants",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SubscriptionPlans",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    Summary = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DurationMonths = table.Column<int>(type: "integer", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    OriginalPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    OfferLabel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OfferStartsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    OfferEndsAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    IsFeatured = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    FeaturesJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlans", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlans_SortOrder",
                table: "SubscriptionPlans",
                column: "SortOrder");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "SignupPlanCode",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SignupPlanDurationMonths",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SignupPlanPrice",
                table: "Tenants");
        }
    }
}
