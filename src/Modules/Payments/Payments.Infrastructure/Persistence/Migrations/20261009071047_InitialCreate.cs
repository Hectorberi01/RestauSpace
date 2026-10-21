using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_providers",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    display_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    capabilities = table.Column<string>(type: "jsonb", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_providers", x => x.id);
                    table.CheckConstraint("ck_payment_providers_capabilities", "jsonb_typeof(capabilities) = 'object'");
                    table.CheckConstraint("ck_payment_providers_type", "type IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "payment_secrets",
                schema: "payments",
                columns: table => new
                {
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cipher_text = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_secrets", x => x.reference);
                    table.CheckConstraint("ck_payment_secrets_cipher_text", "cipher_text <> ''");
                    table.CheckConstraint("ck_payment_secrets_owner", "(tenant_id IS NULL AND reference LIKE 'payments/platform/%') OR (tenant_id IS NOT NULL AND reference LIKE ('payments/tenant/' || replace(tenant_id::text, '-', '') || '/%'))");
                });

            migrationBuilder.CreateTable(
                name: "payments",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    authorized_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    captured_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    captured_amount = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    captured_amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount_currency", "amount_currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_payments_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_payments_captured_amount_currency", "captured_amount_currency = amount_currency");
                    table.CheckConstraint("ck_payments_captured_amount_not_negative", "captured_amount >= 0");
                    table.CheckConstraint("ck_payments_method", "method IN (1, 2, 3, 4, 5, 6, 20, 21, 22, 23)");
                    table.CheckConstraint("ck_payments_status", "status IN (1, 2, 3, 4, 5, 6, 7)");
                });

            migrationBuilder.CreateTable(
                name: "provider_webhook_events",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    configuration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    raw_body = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    refund_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    outcome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_webhook_events", x => x.id);
                    table.CheckConstraint("ck_provider_webhook_events_attempts", "attempts >= 0");
                    table.CheckConstraint("ck_provider_webhook_events_kind", "kind IN (1, 2, 3, 4, 5)");
                });

            migrationBuilder.CreateTable(
                name: "restaurant_payment_configurations",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_type = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    secret_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    enabled_methods = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurant_payment_configurations", x => x.id);
                    table.CheckConstraint("ck_restaurant_payment_configurations_enabled_methods", "jsonb_typeof(enabled_methods) = 'array'");
                    table.CheckConstraint("ck_restaurant_payment_configurations_mode", "mode IN (1, 2)");
                    table.CheckConstraint("ck_restaurant_payment_configurations_provider_type", "provider_type IN (1, 2)");
                    table.CheckConstraint("ck_restaurant_payment_configurations_secret_reference", "NOT (provider_type = 2 AND mode = 2 AND status = 2) OR secret_reference IS NOT NULL");
                    table.CheckConstraint("ck_restaurant_payment_configurations_secret_reference_tenant", "secret_reference IS NULL OR secret_reference LIKE ('payments/tenant/' || replace(tenant_id::text, '-', '') || '/%')");
                    table.CheckConstraint("ck_restaurant_payment_configurations_status", "status IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "payment_attempts",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    authorized_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    captured_after_cancellation_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_attempts", x => x.id);
                    table.CheckConstraint("ck_payment_attempts_status", "status IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_payment_attempts_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "payments",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refunds", x => x.id);
                    table.CheckConstraint("ck_refunds_amount_currency", "amount_currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_refunds_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_refunds_status", "status IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_refunds_payments_payment_id",
                        column: x => x.payment_id,
                        principalSchema: "payments",
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payment_method_assignments",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    min_amount = table.Column<string>(type: "jsonb", nullable: true),
                    max_amount = table.Column<string>(type: "jsonb", nullable: true),
                    configuration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_method_assignments", x => x.id);
                    table.CheckConstraint("ck_payment_method_assignments_max_amount", "max_amount IS NULL OR jsonb_typeof(max_amount) = 'object'");
                    table.CheckConstraint("ck_payment_method_assignments_method", "method IN (1, 2, 3, 4, 5, 6, 20, 21, 22, 23)");
                    table.CheckConstraint("ck_payment_method_assignments_min_amount", "min_amount IS NULL OR jsonb_typeof(min_amount) = 'object'");
                    table.CheckConstraint("ck_payment_method_assignments_priority", "priority BETWEEN 0 AND 1000");
                    table.ForeignKey(
                        name: "FK_payment_method_assignments_restaurant_payment_configuration~",
                        column: x => x.configuration_id,
                        principalSchema: "payments",
                        principalTable: "restaurant_payment_configurations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "payments",
                table: "outbox_messages",
                column: "id",
                filter: "processed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_payment_id",
                schema: "payments",
                table: "payment_attempts",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_tenant_id_provider_reference",
                schema: "payments",
                table: "payment_attempts",
                columns: new[] { "tenant_id", "provider_reference" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_method_assignments_configuration_id_location_id_method",
                schema: "payments",
                table: "payment_method_assignments",
                columns: new[] { "configuration_id", "location_id", "method" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_method_assignments_tenant_id_location_id",
                schema: "payments",
                table: "payment_method_assignments",
                columns: new[] { "tenant_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_providers_code",
                schema: "payments",
                table: "payment_providers",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payment_secrets_tenant_id",
                schema: "payments",
                table: "payment_secrets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_location_id_created_at_utc",
                schema: "payments",
                table: "payments",
                columns: new[] { "tenant_id", "location_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_order_id",
                schema: "payments",
                table: "payments",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_webhook_events_configuration_id_event_id",
                schema: "payments",
                table: "provider_webhook_events",
                columns: new[] { "configuration_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_provider_webhook_events_pending",
                schema: "payments",
                table: "provider_webhook_events",
                column: "next_attempt_at_utc",
                filter: "processed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_payment_id_provider_reference",
                schema: "payments",
                table: "refunds",
                columns: new[] { "payment_id", "provider_reference" },
                unique: true,
                filter: "provider_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_restaurant_payment_configurations_tenant_id_provider",
                schema: "payments",
                table: "restaurant_payment_configurations",
                columns: new[] { "tenant_id", "provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payment_attempts",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payment_method_assignments",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payment_providers",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payment_secrets",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "provider_webhook_events",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "refunds",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "restaurant_payment_configurations",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payments",
                schema: "payments");
        }
    }
}
