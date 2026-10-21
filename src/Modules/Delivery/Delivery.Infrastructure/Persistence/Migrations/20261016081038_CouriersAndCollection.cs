using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Delivery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CouriersAndCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "amount_collected",
                schema: "delivery",
                table: "fulfillments",
                type: "numeric(19,3)",
                precision: 19,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "amount_to_collect",
                schema: "delivery",
                table: "fulfillments",
                type: "numeric(19,3)",
                precision: 19,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "collection_method",
                schema: "delivery",
                table: "fulfillments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "courier_fee",
                schema: "delivery",
                table: "fulfillments",
                type: "numeric(19,3)",
                precision: 19,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "courier_id",
                schema: "delivery",
                table: "fulfillments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "remittance_id",
                schema: "delivery",
                table: "fulfillments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "remitted_at_utc",
                schema: "delivery",
                table: "fulfillments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "courier_remittances",
                schema: "delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    courier_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    cash_collected = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    courier_fees = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    expected = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    counted = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_courier_remittances", x => x.id);
                    table.CheckConstraint("ck_courier_remittances_amounts", "cash_collected >= 0 AND courier_fees >= 0 AND expected = cash_collected - courier_fees");
                });

            migrationBuilder.CreateTable(
                name: "couriers",
                schema: "delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    fee = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: true),
                    fee_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_couriers", x => x.id);
                    table.CheckConstraint("ck_couriers_fee", "(fee IS NULL AND fee_currency IS NULL) OR (fee >= 0 AND fee_currency IS NOT NULL)");
                    table.CheckConstraint("ck_couriers_kind", "kind IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "courier_remittance_items",
                schema: "delivery",
                columns: table => new
                {
                    fulfillment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    remittance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cash_collected = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    courier_fee = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_courier_remittance_items", x => new { x.remittance_id, x.fulfillment_id });
                    table.ForeignKey(
                        name: "FK_courier_remittance_items_courier_remittances_remittance_id",
                        column: x => x.remittance_id,
                        principalSchema: "delivery",
                        principalTable: "courier_remittances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fulfillments_awaiting_remittance",
                schema: "delivery",
                table: "fulfillments",
                columns: new[] { "tenant_id", "location_id", "completed_at_utc" },
                filter: "mode = 2 AND status = 5 AND remittance_id IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fulfillments_amounts",
                schema: "delivery",
                table: "fulfillments",
                sql: "(amount_to_collect IS NULL OR amount_to_collect > 0) AND (courier_fee IS NULL OR courier_fee >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fulfillments_collection",
                schema: "delivery",
                table: "fulfillments",
                sql: "(amount_collected IS NULL AND collection_method IS NULL) OR (amount_collected > 0 AND collection_method IS NOT NULL AND amount_to_collect IS NOT NULL AND amount_collected <= amount_to_collect)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_fulfillments_collection_method",
                schema: "delivery",
                table: "fulfillments",
                sql: "collection_method IS NULL OR collection_method IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_courier_remittance_items_fulfillment_id",
                schema: "delivery",
                table: "courier_remittance_items",
                column: "fulfillment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_courier_remittances_tenant_id_location_id_recorded_at_utc",
                schema: "delivery",
                table: "courier_remittances",
                columns: new[] { "tenant_id", "location_id", "recorded_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_couriers_tenant_id_is_active",
                schema: "delivery",
                table: "couriers",
                columns: new[] { "tenant_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "courier_remittance_items",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "couriers",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "courier_remittances",
                schema: "delivery");

            migrationBuilder.DropIndex(
                name: "ix_fulfillments_awaiting_remittance",
                schema: "delivery",
                table: "fulfillments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fulfillments_amounts",
                schema: "delivery",
                table: "fulfillments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fulfillments_collection",
                schema: "delivery",
                table: "fulfillments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_fulfillments_collection_method",
                schema: "delivery",
                table: "fulfillments");

            migrationBuilder.DropColumn(
                name: "amount_collected",
                schema: "delivery",
                table: "fulfillments");

            migrationBuilder.DropColumn(
                name: "amount_to_collect",
                schema: "delivery",
                table: "fulfillments");

            migrationBuilder.DropColumn(
                name: "collection_method",
                schema: "delivery",
                table: "fulfillments");

            migrationBuilder.DropColumn(
                name: "courier_fee",
                schema: "delivery",
                table: "fulfillments");

            migrationBuilder.DropColumn(
                name: "courier_id",
                schema: "delivery",
                table: "fulfillments");

            migrationBuilder.DropColumn(
                name: "remittance_id",
                schema: "delivery",
                table: "fulfillments");

            migrationBuilder.DropColumn(
                name: "remitted_at_utc",
                schema: "delivery",
                table: "fulfillments");
        }
    }
}
