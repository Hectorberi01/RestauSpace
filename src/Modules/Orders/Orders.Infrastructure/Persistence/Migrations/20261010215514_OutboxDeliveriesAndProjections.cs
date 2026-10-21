using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxDeliveriesAndProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_pending",
                schema: "orders",
                table: "outbox_messages");

            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                schema: "orders",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dead_lettered_at_utc",
                schema: "orders",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_error",
                schema: "orders",
                table: "outbox_messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at_utc",
                schema: "orders",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "schema_version",
                schema: "orders",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "trace_parent",
                schema: "orders",
                table: "outbox_messages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_payment_settlements",
                schema: "orders",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    settled_amount = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_order_payment_settlements", x => new { x.tenant_id, x.payment_id });
                    table.CheckConstraint("ck_order_payment_settlements_amount", "settled_amount >= 0");
                });

            migrationBuilder.CreateTable(
                name: "outbox_deliveries",
                schema: "orders",
                columns: table => new
                {
                    message_id = table.Column<long>(type: "bigint", nullable: false),
                    consumer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_deliveries", x => new { x.message_id, x.consumer });
                    table.CheckConstraint("ck_outbox_deliveries_status", "status IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "fk_outbox_deliveries_message",
                        column: x => x.message_id,
                        principalSchema: "orders",
                        principalTable: "outbox_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_lettered",
                schema: "orders",
                table: "outbox_messages",
                column: "id",
                filter: "dead_lettered_at_utc IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_messages_attempt_count",
                schema: "orders",
                table: "outbox_messages",
                sql: "attempt_count >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_messages_schema_version",
                schema: "orders",
                table: "outbox_messages",
                sql: "schema_version >= 1");

            migrationBuilder.CreateIndex(
                name: "ix_order_payment_settlements_order",
                schema: "orders",
                table: "order_payment_settlements",
                columns: new[] { "tenant_id", "order_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_payment_settlements",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "outbox_deliveries",
                schema: "orders");

            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_dead_lettered",
                schema: "orders",
                table: "outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_messages_attempt_count",
                schema: "orders",
                table: "outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_messages_schema_version",
                schema: "orders",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                schema: "orders",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "dead_lettered_at_utc",
                schema: "orders",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "last_error",
                schema: "orders",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "next_attempt_at_utc",
                schema: "orders",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "schema_version",
                schema: "orders",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "trace_parent",
                schema: "orders",
                table: "outbox_messages");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "orders",
                table: "outbox_messages",
                column: "id",
                filter: "processed_at_utc IS NULL");
        }
    }
}
