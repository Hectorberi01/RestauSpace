using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Backups.Infrastructure.Persistence.Migrations
{
    /// <summary>File des sauvegardes et restaurations, ligne de l'agent, outbox.</summary>
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "backups");

            migrationBuilder.CreateTable(
                name: "agent",
                schema: "backups",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    host = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<string>(type: "text", nullable: false),
                    last_seen_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    restore_enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agent", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "backups",
                schema: "backups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    database_name = table.Column<string>(type: "text", nullable: false),
                    trigger = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    storage_key = table.Column<string>(type: "text", nullable: true),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    checksum_sha256 = table.Column<string>(type: "text", nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    failure_code = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    heartbeat_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "backups",
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
                name: "restores",
                schema: "backups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    backup_id = table.Column<Guid>(type: "uuid", nullable: false),
                    database_name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    not_before_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    heartbeat_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    previous_database_name = table.Column<string>(type: "text", nullable: true),
                    failure_code = table.Column<string>(type: "text", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restores", x => x.id);
                    table.ForeignKey(
                        name: "fk_restores_backup",
                        column: x => x.backup_id,
                        principalSchema: "backups",
                        principalTable: "backups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_deliveries",
                schema: "backups",
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
                        principalSchema: "backups",
                        principalTable: "outbox_messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_backups_created_at_utc",
                schema: "backups",
                table: "backups",
                column: "created_at_utc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_backups_status",
                schema: "backups",
                table: "backups",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_dead_lettered",
                schema: "backups",
                table: "outbox_messages",
                column: "id",
                filter: "dead_lettered_at_utc IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_restores_backup_id",
                schema: "backups",
                table: "restores",
                column: "backup_id");

            migrationBuilder.CreateIndex(
                name: "ix_restores_created_at_utc",
                schema: "backups",
                table: "restores",
                column: "created_at_utc",
                descending: new bool[0]);

            // Hors du modèle EF : une seule ligne d'agent, une seule sauvegarde et une seule restauration actives.
            migrationBuilder.Sql("ALTER TABLE backups.agent ADD CONSTRAINT ck_agent_single_row CHECK (id = 1);");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_backups_active ON backups.backups ((true)) WHERE status IN ('Pending', 'Running', 'Uploading');");
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_restores_active ON backups.restores ((true)) WHERE status IN ('Pending', 'Running');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent",
                schema: "backups");

            migrationBuilder.DropTable(
                name: "outbox_deliveries",
                schema: "backups");

            migrationBuilder.DropTable(
                name: "restores",
                schema: "backups");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "backups");

            migrationBuilder.DropTable(
                name: "backups",
                schema: "backups");
        }
    }
}
