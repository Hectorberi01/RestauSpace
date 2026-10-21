using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "notifications");

            migrationBuilder.CreateTable(
                name: "device_registrations",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    platform = table.Column<int>(type: "integer", nullable: false),
                    push_token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deactivated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deactivation_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_registrations", x => x.id);
                    table.CheckConstraint("ck_device_registrations_platform", "platform IN (1, 2, 3)");
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "notifications",
                columns: table => new
                {
                    consumer = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => new { x.consumer, x.event_id });
                });

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_preferences", x => x.id);
                    table.CheckConstraint("ck_notification_preferences_owner", "owner ~ '^(customer|user):[0-9a-f]{32}$'");
                });

            migrationBuilder.CreateTable(
                name: "notification_providers",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    configuration_type = table.Column<int>(type: "integer", nullable: false),
                    capabilities = table.Column<string>(type: "jsonb", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_providers", x => x.id);
                    table.CheckConstraint("ck_notification_providers_capabilities", "jsonb_typeof(capabilities) = 'object'");
                    table.CheckConstraint("ck_notification_providers_channel", "channel IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_notification_providers_configuration_type", "configuration_type IN (1, 2, 3, 4)");
                });

            migrationBuilder.CreateTable(
                name: "notification_secrets",
                schema: "notifications",
                columns: table => new
                {
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cipher_text = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_secrets", x => x.reference);
                    table.CheckConstraint("ck_notification_secrets_cipher_text", "cipher_text LIKE 'v1:%'");
                    table.CheckConstraint("ck_notification_secrets_owner", "(tenant_id IS NULL AND reference LIKE 'notifications/platform/%') OR (tenant_id IS NOT NULL AND reference LIKE ('notifications/tenant/' || replace(tenant_id::text, '-', '') || '/%'))");
                });

            migrationBuilder.CreateTable(
                name: "notification_templates",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    language = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_templates", x => x.id);
                    table.CheckConstraint("ck_notification_templates_channel", "channel IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_notification_templates_status", "status IN (1, 2, 3)");
                    table.CheckConstraint("ck_notification_templates_subject", "(channel <> 1 OR subject IS NOT NULL) AND (channel NOT IN (2, 3) OR subject IS NULL)");
                    table.CheckConstraint("ck_notification_templates_version", "version >= 1");
                });

            migrationBuilder.CreateTable(
                name: "notification_usage",
                schema: "notifications",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period = table.Column<DateOnly>(type: "date", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    count = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_usage", x => new { x.tenant_id, x.period, x.channel, x.provider });
                    table.CheckConstraint("ck_notification_usage_channel", "channel IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_notification_usage_count", "count >= 0");
                    table.CheckConstraint("ck_notification_usage_period", "extract(day from period) = 1");
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    purpose = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    recipient = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    language = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scheduled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_failure = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    redacted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    read_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    manual_retry_count = table.Column<int>(type: "integer", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    body = table.Column<string>(type: "character varying(40000)", maxLength: 40000, nullable: false),
                    content_channel = table.Column<int>(type: "integer", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    retry_initial_backoff = table.Column<TimeSpan>(type: "interval", nullable: false),
                    retry_max_attempts = table.Column<int>(type: "integer", nullable: false),
                    retry_max_backoff = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_channel", "channel IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_notifications_content_channel", "content_channel = channel");
                    table.CheckConstraint("ck_notifications_in_app_provider", "(provider = 'IN_APP') = (channel = 5)");
                    table.CheckConstraint("ck_notifications_last_failure", "last_failure IS NULL OR jsonb_typeof(last_failure) = 'object'");
                    table.CheckConstraint("ck_notifications_manual_retry_count", "manual_retry_count >= 0");
                    table.CheckConstraint("ck_notifications_platform", "tenant_id IS NOT NULL OR (location_id IS NULL AND purpose = 1)");
                    table.CheckConstraint("ck_notifications_priority", "priority IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_notifications_purpose", "purpose IN (1, 2)");
                    table.CheckConstraint("ck_notifications_recipient", "recipient IS NULL OR recipient ~ '^(customer|user):[0-9a-f]{32}$'");
                    table.CheckConstraint("ck_notifications_status", "status IN (1, 2, 3, 4, 5, 6, 7, 8)");
                });

            migrationBuilder.CreateTable(
                name: "otp_challenges",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    subject_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_otp_challenges", x => x.id);
                    table.CheckConstraint("ck_otp_challenges_attempts", "attempt_count >= 0 AND attempt_count <= max_attempts");
                    table.CheckConstraint("ck_otp_challenges_channel", "channel IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_otp_challenges_code_hash", "code_hash ~ '^[0-9a-f]{64,128}$'");
                    table.CheckConstraint("ck_otp_challenges_status", "status IN (1, 2, 3, 4, 5)");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "notifications",
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
                name: "provider_webhook_events",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    event_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    is_permanent_failure = table.Column<bool>(type: "boolean", nullable: false),
                    received_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_webhook_events", x => x.id);
                    table.CheckConstraint("ck_provider_webhook_events_attempts", "attempts >= 0");
                    table.CheckConstraint("ck_provider_webhook_events_kind", "kind IN (1, 2, 3, 4)");
                });

            migrationBuilder.CreateTable(
                name: "restaurant_notification_configurations",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    secret_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurant_notification_configurations", x => x.id);
                    table.CheckConstraint("ck_restaurant_notification_configurations_channel", "channel IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_restaurant_notification_configurations_enabled_configured", "NOT is_enabled OR status = 2");
                    table.CheckConstraint("ck_restaurant_notification_configurations_mode", "mode IN (1, 2)");
                    table.CheckConstraint("ck_restaurant_notification_configurations_secret_reference", "NOT (mode = 2 AND status = 2) OR secret_reference IS NOT NULL");
                    table.CheckConstraint("ck_restaurant_notification_configurations_secret_reference_ten~", "secret_reference IS NULL OR secret_reference LIKE ('notifications/tenant/' || replace(tenant_id::text, '-', '') || '/%')");
                    table.CheckConstraint("ck_restaurant_notification_configurations_status", "status IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "notification_preference_choices",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    purpose = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    is_allowed = table.Column<bool>(type: "boolean", nullable: false),
                    changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    preference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_preference_choices", x => x.id);
                    table.CheckConstraint("ck_notification_preference_choices_channel", "channel IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_notification_preference_choices_purpose", "purpose IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_notification_preference_choices_notification_preferences_pr~",
                        column: x => x.preference_id,
                        principalSchema: "notifications",
                        principalTable: "notification_preferences",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_deliveries",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_message_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure = table.Column<string>(type: "jsonb", nullable: true),
                    next_retry_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_deliveries", x => x.id);
                    table.CheckConstraint("ck_notification_deliveries_attempt_number", "attempt_number >= 1");
                    table.CheckConstraint("ck_notification_deliveries_failure", "failure IS NULL OR jsonb_typeof(failure) = 'object'");
                    table.CheckConstraint("ck_notification_deliveries_status", "status IN (1, 2, 3, 4, 5)");
                    table.ForeignKey(
                        name: "FK_notification_deliveries_notifications_notification_id",
                        column: x => x.notification_id,
                        principalSchema: "notifications",
                        principalTable: "notifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_channel_assignments",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    configuration_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_channel_assignments", x => x.id);
                    table.CheckConstraint("ck_notification_channel_assignments_channel", "channel IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_notification_channel_assignments_priority", "priority BETWEEN 0 AND 1000");
                    table.ForeignKey(
                        name: "FK_notification_channel_assignments_restaurant_notification_co~",
                        column: x => x.configuration_id,
                        principalSchema: "notifications",
                        principalTable: "restaurant_notification_configurations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_registrations_push_token",
                schema: "notifications",
                table: "device_registrations",
                column: "push_token");

            migrationBuilder.CreateIndex(
                name: "ix_device_registrations_user_id_device_id",
                schema: "notifications",
                table: "device_registrations",
                columns: new[] { "user_id", "device_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_processed_at_utc",
                schema: "notifications",
                table: "inbox_messages",
                column: "processed_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_notification_channel_assignments_configuration_id_location_id",
                schema: "notifications",
                table: "notification_channel_assignments",
                columns: new[] { "configuration_id", "location_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_channel_assignments_tenant_id_location_id",
                schema: "notifications",
                table: "notification_channel_assignments",
                columns: new[] { "tenant_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_notification_id",
                schema: "notifications",
                table: "notification_deliveries",
                column: "notification_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_deliveries_provider_provider_message_id",
                schema: "notifications",
                table: "notification_deliveries",
                columns: new[] { "provider", "provider_message_id" },
                unique: true,
                filter: "provider_message_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notification_preference_choices_preference_id_purpose_channel",
                schema: "notifications",
                table: "notification_preference_choices",
                columns: new[] { "preference_id", "purpose", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_preferences_tenant_id_owner",
                schema: "notifications",
                table: "notification_preferences",
                columns: new[] { "tenant_id", "owner" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_providers_code",
                schema: "notifications",
                table: "notification_providers",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_secrets_tenant_id",
                schema: "notifications",
                table: "notification_secrets",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_notification_templates_tenant_id_key_channel_language_version",
                schema: "notifications",
                table: "notification_templates",
                columns: new[] { "tenant_id", "key", "channel", "language", "version" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_status_priority_next_attempt_at_utc",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "status", "priority", "next_attempt_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_tenant_id_created_at_utc",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_tenant_id_idempotency_key",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "tenant_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL")
                .Annotation("Npgsql:NullsDistinct", false);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_tenant_id_recipient_created_at_utc",
                schema: "notifications",
                table: "notifications",
                columns: new[] { "tenant_id", "recipient", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_subject_key_purpose_created_at_utc",
                schema: "notifications",
                table: "otp_challenges",
                columns: new[] { "subject_key", "purpose", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "notifications",
                table: "outbox_messages",
                column: "id",
                filter: "processed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_provider_webhook_events_processed_at_utc_next_attempt_at_utc",
                schema: "notifications",
                table: "provider_webhook_events",
                columns: new[] { "processed_at_utc", "next_attempt_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_webhook_events_provider_event_id",
                schema: "notifications",
                table: "provider_webhook_events",
                columns: new[] { "provider", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_restaurant_notification_configurations_tenant_id_channel",
                schema: "notifications",
                table: "restaurant_notification_configurations",
                columns: new[] { "tenant_id", "channel" });

            migrationBuilder.CreateIndex(
                name: "ix_restaurant_notification_configurations_tenant_id_provider",
                schema: "notifications",
                table: "restaurant_notification_configurations",
                columns: new[] { "tenant_id", "provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_registrations",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_channel_assignments",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_deliveries",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_preference_choices",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_providers",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_secrets",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_templates",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_usage",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "otp_challenges",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "provider_webhook_events",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "restaurant_notification_configurations",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "notification_preferences",
                schema: "notifications");
        }
    }
}
