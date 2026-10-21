using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TwoFactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_two_factor_step",
                schema: "identity",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pending_two_factor_secret",
                schema: "identity",
                table: "users",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "recovery_code_hashes",
                schema: "identity",
                table: "users",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "two_factor_enabled_at_utc",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "two_factor_secret",
                schema: "identity",
                table: "users",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_two_factor_step",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "pending_two_factor_secret",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "recovery_code_hashes",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_enabled_at_utc",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "two_factor_secret",
                schema: "identity",
                table: "users");
        }
    }
}
