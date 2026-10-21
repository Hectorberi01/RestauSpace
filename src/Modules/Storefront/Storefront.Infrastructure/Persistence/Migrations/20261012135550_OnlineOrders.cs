using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storefront.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OnlineOrders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "online_orders",
                schema: "storefront",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_mode = table.Column<int>(type: "integer", nullable: false),
                    acceptance = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    placed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accept_before_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_online_orders", x => x.id);
                    table.CheckConstraint("ck_online_orders_acceptance", "acceptance IN (1, 2)");
                    table.CheckConstraint("ck_online_orders_channel", "channel IN (1, 2, 3)");
                    table.CheckConstraint("ck_online_orders_deadline", "acceptance <> 2 OR accept_before_utc IS NOT NULL");
                    table.CheckConstraint("ck_online_orders_payment_mode", "payment_mode IN (1, 2)");
                    table.CheckConstraint("ck_online_orders_state", "state IN (1, 2, 3, 4)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_online_orders_state_accept_before_utc",
                schema: "storefront",
                table: "online_orders",
                columns: new[] { "state", "accept_before_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_online_orders_tenant_id_order_id",
                schema: "storefront",
                table: "online_orders",
                columns: new[] { "tenant_id", "order_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "online_orders",
                schema: "storefront");
        }
    }
}
