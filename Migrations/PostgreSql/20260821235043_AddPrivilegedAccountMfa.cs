using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VehiclePermitSystemWeb.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddPrivilegedAccountMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MfaEnabled",
                table: "UserAccounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "MfaEnrolledAtUtc",
                table: "UserAccounts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MfaLastVerifiedStep",
                table: "UserAccounts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MfaRecoveryCodeHashesJson",
                table: "UserAccounts",
                type: "character varying(4096)",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MfaSecretProtected",
                table: "UserAccounts",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MfaEnabled",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "MfaEnrolledAtUtc",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "MfaLastVerifiedStep",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "MfaRecoveryCodeHashesJson",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "MfaSecretProtected",
                table: "UserAccounts");

        }
    }
}
