using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Printing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "printing");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "printing",
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
                name: "print_jobs",
                schema: "printing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    printer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    document = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sent_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_print_jobs", x => x.id);
                    table.CheckConstraint("ck_print_jobs_attempts", "attempts >= 0");
                    table.CheckConstraint("ck_print_jobs_kind", "kind IN (1, 2)");
                    table.CheckConstraint("ck_print_jobs_status", "status IN (1, 2, 3, 4, 5)");
                });

            migrationBuilder.CreateTable(
                name: "printers",
                schema: "printing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    protocol = table.Column<int>(type: "integer", nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    token_hint = table.Column<string>(type: "character(4)", fixedLength: true, maxLength: 4, nullable: false),
                    prints_customer_tickets = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_printers", x => x.id);
                    table.CheckConstraint("ck_printers_protocol", "protocol IN (1, 2)");
                    table.CheckConstraint("ck_printers_status", "status IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "outbox_deliveries",
                schema: "printing",
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
                        principalSchema: "printing",
                        principalTable: "outbox_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_lettered",
                schema: "printing",
                table: "outbox_messages",
                column: "id",
                filter: "dead_lettered_at_utc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_print_jobs_open_printer_id_created_at_utc",
                schema: "printing",
                table: "print_jobs",
                columns: new[] { "printer_id", "created_at_utc" },
                filter: "status IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_print_jobs_tenant_id_location_id_created_at_utc",
                schema: "printing",
                table: "print_jobs",
                columns: new[] { "tenant_id", "location_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_print_jobs_printer_id_kind_reference",
                schema: "printing",
                table: "print_jobs",
                columns: new[] { "printer_id", "kind", "reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_printers_tenant_id_location_id",
                schema: "printing",
                table: "printers",
                columns: new[] { "tenant_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ux_printers_token_hash",
                schema: "printing",
                table: "printers",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_deliveries",
                schema: "printing");

            migrationBuilder.DropTable(
                name: "print_jobs",
                schema: "printing");

            migrationBuilder.DropTable(
                name: "printers",
                schema: "printing");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "printing");
        }
    }
}
