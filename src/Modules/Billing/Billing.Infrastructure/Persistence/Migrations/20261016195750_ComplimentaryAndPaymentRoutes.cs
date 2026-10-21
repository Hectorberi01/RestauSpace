using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComplimentaryAndPaymentRoutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ending_notice_sent_for_utc",
                schema: "billing",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_complimentary",
                schema: "billing",
                table: "subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "online_payment_routes",
                schema: "billing",
                columns: table => new
                {
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_online_payment_routes", x => x.currency_code);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_complimentary",
                schema: "billing",
                table: "subscriptions",
                sql: "NOT is_complimentary OR status = 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "online_payment_routes",
                schema: "billing");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_complimentary",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "ending_notice_sent_for_utc",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "is_complimentary",
                schema: "billing",
                table: "subscriptions");
        }
    }
}
