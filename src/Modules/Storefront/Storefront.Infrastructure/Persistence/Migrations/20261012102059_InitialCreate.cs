using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Storefront.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "storefront");

            migrationBuilder.CreateTable(
                name: "online_ordering_settings",
                schema: "storefront",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_paused = table.Column<bool>(type: "boolean", nullable: false),
                    preparation_minutes = table.Column<int>(type: "integer", nullable: false),
                    minimum_order_amount = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: true),
                    manual_acceptance_minutes = table.Column<int>(type: "integer", nullable: false),
                    unpaid_expiry_minutes = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    channel_policies = table.Column<string>(type: "jsonb", nullable: false),
                    service_modes = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_online_ordering_settings", x => x.id);
                    table.CheckConstraint("ck_online_ordering_settings_acceptance", "manual_acceptance_minutes BETWEEN 2 AND 60");
                    table.CheckConstraint("ck_online_ordering_settings_expiry", "unpaid_expiry_minutes BETWEEN 5 AND 120");
                    table.CheckConstraint("ck_online_ordering_settings_minimum", "minimum_order_amount IS NULL OR minimum_order_amount > 0");
                    table.CheckConstraint("ck_online_ordering_settings_preparation", "preparation_minutes BETWEEN 5 AND 240");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "storefront",
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
                name: "sales_apps",
                schema: "storefront",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    key_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    key_hint = table.Column<string>(type: "character(4)", fixedLength: true, maxLength: 4, nullable: false),
                    key_issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    allowed_origins = table.Column<string>(type: "jsonb", nullable: false),
                    location_ids = table.Column<string>(type: "jsonb", nullable: false),
                    return_urls = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales_apps", x => x.id);
                    table.CheckConstraint("ck_sales_apps_kind", "kind IN (1, 2, 3)");
                    table.CheckConstraint("ck_sales_apps_status", "status IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "outbox_deliveries",
                schema: "storefront",
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
                        principalSchema: "storefront",
                        principalTable: "outbox_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_online_ordering_settings_tenant_id_location_id",
                schema: "storefront",
                table: "online_ordering_settings",
                columns: new[] { "tenant_id", "location_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_lettered",
                schema: "storefront",
                table: "outbox_messages",
                column: "id",
                filter: "dead_lettered_at_utc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_apps_tenant_id",
                schema: "storefront",
                table: "sales_apps",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_sales_apps_key_hash",
                schema: "storefront",
                table: "sales_apps",
                column: "key_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "online_ordering_settings",
                schema: "storefront");

            migrationBuilder.DropTable(
                name: "outbox_deliveries",
                schema: "storefront");

            migrationBuilder.DropTable(
                name: "sales_apps",
                schema: "storefront");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "storefront");
        }
    }
}
