using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storefront.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PendingPaymentLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "client_key",
                schema: "storefront",
                table: "online_orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "customer_id",
                schema: "storefront",
                table: "online_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_online_orders_awaiting_payment",
                schema: "storefront",
                table: "online_orders",
                columns: new[] { "tenant_id", "app_id", "state" },
                filter: "state = 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_online_orders_awaiting_payment",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.DropColumn(
                name: "client_key",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.DropColumn(
                name: "customer_id",
                schema: "storefront",
                table: "online_orders");
        }
    }
}
