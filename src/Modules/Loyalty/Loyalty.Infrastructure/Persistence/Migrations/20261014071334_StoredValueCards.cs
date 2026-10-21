using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Loyalty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoredValueCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stored_value_cards",
                schema: "loyalty",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    code_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    code_hint = table.Column<string>(type: "character(4)", fixedLength: true, maxLength: 4, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    initial_amount = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    balance = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    issued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    movements = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_value_cards", x => x.id);
                    table.CheckConstraint("ck_stored_value_cards_amounts", "initial_amount > 0 AND balance >= 0 AND balance <= initial_amount");
                    table.CheckConstraint("ck_stored_value_cards_cancelled", "(status = 2) = (cancelled_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_stored_value_cards_cancelled_balance", "status <> 2 OR balance = 0");
                    table.CheckConstraint("ck_stored_value_cards_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_stored_value_cards_kind", "kind IN (1, 2)");
                    table.CheckConstraint("ck_stored_value_cards_status", "status IN (1, 2)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_stored_value_cards_tenant_id_code_hash",
                schema: "loyalty",
                table: "stored_value_cards",
                columns: new[] { "tenant_id", "code_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stored_value_cards_tenant_id_customer_id",
                schema: "loyalty",
                table: "stored_value_cards",
                columns: new[] { "tenant_id", "customer_id" },
                filter: "customer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_stored_value_cards_tenant_id_issued_at_utc",
                schema: "loyalty",
                table: "stored_value_cards",
                columns: new[] { "tenant_id", "issued_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stored_value_cards",
                schema: "loyalty");
        }
    }
}
