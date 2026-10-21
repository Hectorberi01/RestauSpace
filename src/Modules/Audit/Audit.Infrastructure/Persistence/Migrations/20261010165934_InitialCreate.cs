using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Audit.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_entries",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target = table.Column<string>(type: "character varying(301)", maxLength: 301, nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    module = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, computedColumnSql: "split_part(action, '.', 1)", stored: true),
                    target_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true, computedColumnSql: "substr(target, strpos(target, '/') + 1)", stored: true),
                    target_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true, computedColumnSql: "split_part(target, '/', 1)", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.id);
                    table.CheckConstraint("ck_audit_entries_occurred_before_recorded", "occurred_at_utc <= recorded_at_utc");
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_occurred_at_utc",
                schema: "audit",
                table: "audit_entries",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_origin",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "correlation_id", "action", "target" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_tenant_id_actor",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "tenant_id", "actor" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_tenant_id_occurred_at_utc",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "tenant_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entries_tenant_id_target",
                schema: "audit",
                table: "audit_entries",
                columns: new[] { "tenant_id", "target_type", "target_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_entries",
                schema: "audit");
        }
    }
}
