using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fiscal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "fiscal");

            migrationBuilder.CreateTable(
                name: "fiscal_invoices",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<long>(type: "bigint", nullable: false),
                    original_invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    uid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    security = table.Column<string>(type: "jsonb", nullable: true),
                    error_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    error_message = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    certified_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    lines = table.Column<string>(type: "jsonb", nullable: false),
                    payments = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_invoices", x => x.id);
                    table.CheckConstraint("ck_fiscal_invoices_kind", "kind IN (1, 2)");
                    table.CheckConstraint("ck_fiscal_invoices_mode", "mode IN (1, 2, 3)");
                    table.CheckConstraint("ck_fiscal_invoices_status", "status IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_fiscal_invoices_total_positive", "total > 0");
                });

            migrationBuilder.CreateTable(
                name: "fiscal_profiles",
                schema: "fiscal",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ifu = table.Column<string>(type: "character(13)", fixedLength: true, maxLength: 13, nullable: false),
                    rccm = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    regime = table.Column<int>(type: "integer", nullable: false),
                    aib = table.Column<int>(type: "integer", nullable: false),
                    mode = table.Column<int>(type: "integer", nullable: false),
                    has_token = table.Column<bool>(type: "boolean", nullable: false),
                    token_updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_profiles", x => x.id);
                    table.CheckConstraint("ck_fiscal_profiles_aib", "aib IN (0, 1, 2)");
                    table.CheckConstraint("ck_fiscal_profiles_ifu", "ifu ~ '^[0-9]{13}$'");
                    table.CheckConstraint("ck_fiscal_profiles_mode", "mode IN (1, 2, 3)");
                    table.CheckConstraint("ck_fiscal_profiles_regime", "regime IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "fiscal_tokens",
                schema: "fiscal",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cipher_text = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fiscal_tokens", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "fiscal",
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
                name: "outbox_deliveries",
                schema: "fiscal",
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
                        principalSchema: "fiscal",
                        principalTable: "outbox_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_invoices_due",
                schema: "fiscal",
                table: "fiscal_invoices",
                column: "next_attempt_at_utc",
                filter: "status IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_fiscal_invoices_tenant_id_created_at_utc",
                schema: "fiscal",
                table: "fiscal_invoices",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_invoices_tenant_id_kind_source_id",
                schema: "fiscal",
                table: "fiscal_invoices",
                columns: new[] { "tenant_id", "kind", "source_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_fiscal_profiles_tenant_id",
                schema: "fiscal",
                table: "fiscal_profiles",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_lettered",
                schema: "fiscal",
                table: "outbox_messages",
                column: "id",
                filter: "dead_lettered_at_utc IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "fiscal_invoices",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "fiscal_profiles",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "fiscal_tokens",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "outbox_deliveries",
                schema: "fiscal");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "fiscal");
        }
    }
}
