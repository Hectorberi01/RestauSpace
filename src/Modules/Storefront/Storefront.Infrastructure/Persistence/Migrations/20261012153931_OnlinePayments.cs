using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storefront.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OnlinePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_online_orders_state_accept_before_utc",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_online_orders_deadline",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_online_orders_state",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.AddColumn<int>(
                name: "acceptance_minutes",
                schema: "storefront",
                table: "online_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_deadline_utc",
                schema: "storefront",
                table: "online_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paid_at_utc",
                schema: "storefront",
                table: "online_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pay_before_utc",
                schema: "storefront",
                table: "online_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_online_orders_next_deadline_utc",
                schema: "storefront",
                table: "online_orders",
                column: "next_deadline_utc",
                filter: "next_deadline_utc IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_online_orders_acceptance_deadline",
                schema: "storefront",
                table: "online_orders",
                sql: "state <> 1 OR accept_before_utc IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_online_orders_acceptance_minutes",
                schema: "storefront",
                table: "online_orders",
                sql: "acceptance_minutes BETWEEN 0 AND 1440");

            migrationBuilder.AddCheckConstraint(
                name: "ck_online_orders_payment_deadline",
                schema: "storefront",
                table: "online_orders",
                sql: "state <> 5 OR pay_before_utc IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_online_orders_state",
                schema: "storefront",
                table: "online_orders",
                sql: "state IN (1, 2, 3, 4, 5)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_online_orders_next_deadline_utc",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_online_orders_acceptance_deadline",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_online_orders_acceptance_minutes",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_online_orders_payment_deadline",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_online_orders_state",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.DropColumn(
                name: "acceptance_minutes",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.DropColumn(
                name: "next_deadline_utc",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.DropColumn(
                name: "paid_at_utc",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.DropColumn(
                name: "pay_before_utc",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.CreateIndex(
                name: "ix_online_orders_state_accept_before_utc",
                schema: "storefront",
                table: "online_orders",
                columns: new[] { "state", "accept_before_utc" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_online_orders_deadline",
                schema: "storefront",
                table: "online_orders",
                sql: "acceptance <> 2 OR accept_before_utc IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_online_orders_state",
                schema: "storefront",
                table: "online_orders",
                sql: "state IN (1, 2, 3, 4)");
        }
    }
}
