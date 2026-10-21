using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Delivery.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "delivery");

            migrationBuilder.CreateTable(
                name: "delivery_settings",
                schema: "delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    pickup_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    pickup_lead_minutes = table.Column<int>(type: "integer", nullable: false),
                    delivery_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    delivery_lead_minutes = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_settings", x => x.id);
                    table.CheckConstraint("ck_delivery_settings_delivery_lead", "delivery_lead_minutes BETWEEN 0 AND 240");
                    table.CheckConstraint("ck_delivery_settings_pickup_lead", "pickup_lead_minutes BETWEEN 0 AND 240");
                });

            migrationBuilder.CreateTable(
                name: "fulfillments",
                schema: "delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    customer_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    promised_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    address_line = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    landmark = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    instructions = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: true),
                    zone_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    delivery_fee = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: true),
                    fee_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: true),
                    courier_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    courier_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ready_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assigned_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    departed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fulfillments", x => x.id);
                    table.CheckConstraint("ck_fulfillments_delivery_details", "(mode = 2 AND address_line IS NOT NULL AND customer_phone IS NOT NULL AND zone_id IS NOT NULL AND delivery_fee IS NOT NULL AND fee_currency IS NOT NULL) OR (mode = 1 AND address_line IS NULL AND zone_id IS NULL AND delivery_fee IS NULL AND courier_name IS NULL)");
                    table.CheckConstraint("ck_fulfillments_failed", "status <> 6 OR failure_reason IS NOT NULL");
                    table.CheckConstraint("ck_fulfillments_mode", "mode IN (1, 2)");
                    table.CheckConstraint("ck_fulfillments_status", "status IN (1, 2, 3, 4, 5, 6, 7)");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "delivery",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    dead_lettered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    trace_parent = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                    table.CheckConstraint("ck_outbox_messages_attempt_count", "attempt_count >= 0");
                    table.CheckConstraint("ck_outbox_messages_schema_version", "schema_version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "delivery_zones",
                schema: "delivery",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    estimated_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    settings_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fee = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    fee_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    minimum_order = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    minimum_order_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_zones", x => x.id);
                    table.CheckConstraint("ck_delivery_zones_amounts", "fee >= 0 AND minimum_order >= 0");
                    table.CheckConstraint("ck_delivery_zones_minutes", "estimated_minutes BETWEEN 5 AND 240");
                    table.ForeignKey(
                        name: "FK_delivery_zones_delivery_settings_settings_id",
                        column: x => x.settings_id,
                        principalSchema: "delivery",
                        principalTable: "delivery_settings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "outbox_deliveries",
                schema: "delivery",
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
                        principalSchema: "delivery",
                        principalTable: "outbox_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_delivery_settings_tenant_id_location_id",
                schema: "delivery",
                table: "delivery_settings",
                columns: new[] { "tenant_id", "location_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_delivery_zones_settings_id",
                schema: "delivery",
                table: "delivery_zones",
                column: "settings_id");

            migrationBuilder.CreateIndex(
                name: "ix_fulfillments_tenant_id_created_at_utc",
                schema: "delivery",
                table: "fulfillments",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_fulfillments_tenant_id_location_id_status_promised_at_utc",
                schema: "delivery",
                table: "fulfillments",
                columns: new[] { "tenant_id", "location_id", "status", "promised_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_fulfillments_tenant_id_order_id_active",
                schema: "delivery",
                table: "fulfillments",
                columns: new[] { "tenant_id", "order_id" },
                unique: true,
                filter: "status <> 7");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_lettered",
                schema: "delivery",
                table: "outbox_messages",
                column: "id",
                filter: "dead_lettered_at_utc IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_zones",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "fulfillments",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "outbox_deliveries",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "delivery_settings",
                schema: "delivery");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "delivery");
        }
    }
}
