using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Integrations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "integrations");

            migrationBuilder.CreateTable(
                name: "api_credentials",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    key_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rotated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_credentials", x => x.id);
                    table.CheckConstraint("ck_api_credentials_revoked_at", "(status = 2) = (revoked_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_api_credentials_status", "status IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "integrations",
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
                name: "webhook_deliveries",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    current_attempt_started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    succeeded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dead_lettered_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "character varying(262144)", maxLength: 262144, nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    retry_initial_delay = table.Column<TimeSpan>(type: "interval", nullable: false),
                    retry_max_attempts = table.Column<int>(type: "integer", nullable: false),
                    retry_max_delay = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_deliveries", x => x.id);
                    table.CheckConstraint("ck_webhook_deliveries_attempt_count", "attempt_count >= 0 AND attempt_count <= retry_max_attempts");
                    table.CheckConstraint("ck_webhook_deliveries_dead_lettered_at", "(status = 4) = (dead_lettered_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_webhook_deliveries_status", "status IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_webhook_deliveries_succeeded_at", "(status = 2) = (succeeded_at_utc IS NOT NULL)");
                });

            migrationBuilder.CreateTable(
                name: "webhook_endpoints",
                schema: "integrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    secret_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    secret_rotated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    events = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_endpoints", x => x.id);
                    table.CheckConstraint("ck_webhook_endpoints_status", "status IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "webhook_secrets",
                schema: "integrations",
                columns: table => new
                {
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cipher_text = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_secrets", x => x.reference);
                });

            migrationBuilder.CreateTable(
                name: "api_credential_scopes",
                schema: "integrations",
                columns: table => new
                {
                    scope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    credential_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_credential_scopes", x => new { x.credential_id, x.scope });
                    table.ForeignKey(
                        name: "FK_api_credential_scopes_api_credentials_credential_id",
                        column: x => x.credential_id,
                        principalSchema: "integrations",
                        principalTable: "api_credentials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_api_credentials_key_hash",
                schema: "integrations",
                table: "api_credentials",
                column: "key_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_credentials_tenant_id",
                schema: "integrations",
                table: "api_credentials",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "integrations",
                table: "outbox_messages",
                column: "id",
                filter: "processed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_current_attempt_started_at_utc",
                schema: "integrations",
                table: "webhook_deliveries",
                column: "current_attempt_started_at_utc",
                filter: "current_attempt_started_at_utc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_endpoint_id_event_id_live",
                schema: "integrations",
                table: "webhook_deliveries",
                columns: new[] { "endpoint_id", "event_id" },
                unique: true,
                filter: "status <> 4");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_next_attempt_at_utc",
                schema: "integrations",
                table: "webhook_deliveries",
                column: "next_attempt_at_utc",
                filter: "next_attempt_at_utc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_status_created_at_utc",
                schema: "integrations",
                table: "webhook_deliveries",
                columns: new[] { "status", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_tenant_id_endpoint_id_created_at_utc",
                schema: "integrations",
                table: "webhook_deliveries",
                columns: new[] { "tenant_id", "endpoint_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_tenant_id_status",
                schema: "integrations",
                table: "webhook_endpoints",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_secrets_tenant_id",
                schema: "integrations",
                table: "webhook_secrets",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_credential_scopes",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "webhook_deliveries",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "webhook_endpoints",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "webhook_secrets",
                schema: "integrations");

            migrationBuilder.DropTable(
                name: "api_credentials",
                schema: "integrations");
        }
    }
}
