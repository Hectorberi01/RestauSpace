using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Floor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OutboxDeliveriesAndProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_pending",
                schema: "floor",
                table: "outbox_messages");

            migrationBuilder.AddColumn<int>(
                name: "attempt_count",
                schema: "floor",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dead_lettered_at_utc",
                schema: "floor",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_error",
                schema: "floor",
                table: "outbox_messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at_utc",
                schema: "floor",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "schema_version",
                schema: "floor",
                table: "outbox_messages",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "trace_parent",
                schema: "floor",
                table: "outbox_messages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "outbox_deliveries",
                schema: "floor",
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
                        principalSchema: "floor",
                        principalTable: "outbox_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "session_orders",
                schema: "floor",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_closed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_orders", x => new { x.tenant_id, x.order_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_lettered",
                schema: "floor",
                table: "outbox_messages",
                column: "id",
                filter: "dead_lettered_at_utc IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_messages_attempt_count",
                schema: "floor",
                table: "outbox_messages",
                sql: "attempt_count >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_messages_schema_version",
                schema: "floor",
                table: "outbox_messages",
                sql: "schema_version >= 1");

            migrationBuilder.CreateIndex(
                name: "ix_session_orders_open",
                schema: "floor",
                table: "session_orders",
                columns: new[] { "tenant_id", "table_session_id" },
                filter: "is_closed = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_deliveries",
                schema: "floor");

            migrationBuilder.DropTable(
                name: "session_orders",
                schema: "floor");

            migrationBuilder.DropIndex(
                name: "ix_outbox_messages_dead_lettered",
                schema: "floor",
                table: "outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_messages_attempt_count",
                schema: "floor",
                table: "outbox_messages");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_messages_schema_version",
                schema: "floor",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "attempt_count",
                schema: "floor",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "dead_lettered_at_utc",
                schema: "floor",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "last_error",
                schema: "floor",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "next_attempt_at_utc",
                schema: "floor",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "schema_version",
                schema: "floor",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "trace_parent",
                schema: "floor",
                table: "outbox_messages");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "floor",
                table: "outbox_messages",
                column: "id",
                filter: "processed_at_utc IS NULL");
        }
    }
}
