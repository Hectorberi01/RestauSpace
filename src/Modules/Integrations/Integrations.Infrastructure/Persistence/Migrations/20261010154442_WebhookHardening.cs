using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Integrations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WebhookHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "auto_disabled_at_utc",
                schema: "integrations",
                table: "webhook_endpoints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "consecutive_dead_letters",
                schema: "integrations",
                table: "webhook_endpoints",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "previous_secret_expires_at_utc",
                schema: "integrations",
                table: "webhook_endpoints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "previous_secret_reference",
                schema: "integrations",
                table: "webhook_endpoints",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_used_at_utc",
                schema: "integrations",
                table: "api_credentials",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_previous_secret_expires_at_utc",
                schema: "integrations",
                table: "webhook_endpoints",
                column: "previous_secret_expires_at_utc",
                filter: "previous_secret_expires_at_utc IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_webhook_endpoints_consecutive_dead_letters",
                schema: "integrations",
                table: "webhook_endpoints",
                sql: "consecutive_dead_letters >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_webhook_endpoints_previous_secret",
                schema: "integrations",
                table: "webhook_endpoints",
                sql: "(previous_secret_reference IS NULL) = (previous_secret_expires_at_utc IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_webhook_endpoints_previous_secret_expires_at_utc",
                schema: "integrations",
                table: "webhook_endpoints");

            migrationBuilder.DropCheckConstraint(
                name: "ck_webhook_endpoints_consecutive_dead_letters",
                schema: "integrations",
                table: "webhook_endpoints");

            migrationBuilder.DropCheckConstraint(
                name: "ck_webhook_endpoints_previous_secret",
                schema: "integrations",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "auto_disabled_at_utc",
                schema: "integrations",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "consecutive_dead_letters",
                schema: "integrations",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "previous_secret_expires_at_utc",
                schema: "integrations",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "previous_secret_reference",
                schema: "integrations",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "last_used_at_utc",
                schema: "integrations",
                table: "api_credentials");
        }
    }
}
