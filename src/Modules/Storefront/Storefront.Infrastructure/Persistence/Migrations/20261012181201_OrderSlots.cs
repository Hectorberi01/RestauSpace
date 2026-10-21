using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storefront.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "slot_start_utc",
                schema: "storefront",
                table: "online_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "slot_capacity",
                schema: "storefront",
                table: "online_ordering_settings",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_online_orders_tenant_id_location_id_slot_start_utc",
                schema: "storefront",
                table: "online_orders",
                columns: new[] { "tenant_id", "location_id", "slot_start_utc" },
                filter: "slot_start_utc IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_online_ordering_settings_slot_capacity",
                schema: "storefront",
                table: "online_ordering_settings",
                sql: "slot_capacity IS NULL OR slot_capacity BETWEEN 1 AND 500");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_online_orders_tenant_id_location_id_slot_start_utc",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.DropCheckConstraint(
                name: "ck_online_ordering_settings_slot_capacity",
                schema: "storefront",
                table: "online_ordering_settings");

            migrationBuilder.DropColumn(
                name: "slot_start_utc",
                schema: "storefront",
                table: "online_orders");

            migrationBuilder.DropColumn(
                name: "slot_capacity",
                schema: "storefront",
                table: "online_ordering_settings");
        }
    }
}
