using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cash_sessions",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opened_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closing_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    denominations = table.Column<string>(type: "jsonb", nullable: false),
                    lines = table.Column<string>(type: "jsonb", nullable: false),
                    movements = table.Column<string>(type: "jsonb", nullable: false),
                    opening_float = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    opening_float_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cash_sessions", x => x.id);
                    table.CheckConstraint("ck_cash_sessions_closed", "(status = 2) = (closed_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_cash_sessions_denominations", "jsonb_typeof(denominations) = 'array'");
                    table.CheckConstraint("ck_cash_sessions_lines", "jsonb_typeof(lines) = 'array'");
                    table.CheckConstraint("ck_cash_sessions_movements", "jsonb_typeof(movements) = 'array'");
                    table.CheckConstraint("ck_cash_sessions_number_positive", "number > 0");
                    table.CheckConstraint("ck_cash_sessions_opening_float_currency", "opening_float_currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_cash_sessions_opening_float_not_negative", "opening_float >= 0");
                    table.CheckConstraint("ck_cash_sessions_status", "status IN (1, 2)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_cash_sessions_tenant_id_location_id_number",
                schema: "payments",
                table: "cash_sessions",
                columns: new[] { "tenant_id", "location_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cash_sessions_tenant_id_location_id_open",
                schema: "payments",
                table: "cash_sessions",
                columns: new[] { "tenant_id", "location_id" },
                unique: true,
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_cash_sessions_tenant_id_opened_at_utc",
                schema: "payments",
                table: "cash_sessions",
                columns: new[] { "tenant_id", "opened_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_sessions",
                schema: "payments");
        }
    }
}
