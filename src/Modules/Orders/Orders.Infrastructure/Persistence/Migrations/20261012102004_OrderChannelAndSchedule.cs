using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderChannelAndSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "channel",
                schema: "orders",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "scheduled_for_utc",
                schema: "orders",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_app_id",
                schema: "orders",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_channel_created_at_utc",
                schema: "orders",
                table: "orders",
                columns: new[] { "tenant_id", "channel", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_tenant_id_channel_created_at_utc",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "channel",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "scheduled_for_utc",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "source_app_id",
                schema: "orders",
                table: "orders");
        }
    }
}
