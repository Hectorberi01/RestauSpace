using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OnlinePaymentAndInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "collected_by",
                schema: "billing",
                table: "subscription_payments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "invoice_number",
                schema: "billing",
                table: "subscription_payments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "invoice_sequences",
                schema: "billing",
                columns: table => new
                {
                    year = table.Column<int>(type: "integer", nullable: false),
                    last_number = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_sequences", x => x.year);
                    table.CheckConstraint("ck_invoice_sequences_last_number", "last_number > 0");
                });

            migrationBuilder.CreateTable(
                name: "subscription_checkouts",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    checkout_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_checkouts", x => x.id);
                    table.CheckConstraint("ck_subscription_checkouts_amount_currency", "amount_currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_subscription_checkouts_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_subscription_checkouts_paid_has_reference", "status <> 2 OR provider_reference IS NOT NULL");
                    table.CheckConstraint("ck_subscription_checkouts_status", "status IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_subscription_checkouts_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "billing",
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_subscription_payments_invoice_number",
                schema: "billing",
                table: "subscription_payments",
                column: "invoice_number",
                unique: true,
                filter: "invoice_number IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_checkouts_pending",
                schema: "billing",
                table: "subscription_checkouts",
                column: "created_at_utc",
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_checkouts_subscription_id",
                schema: "billing",
                table: "subscription_checkouts",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_checkouts_tenant_id_created_at_utc",
                schema: "billing",
                table: "subscription_checkouts",
                columns: new[] { "tenant_id", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invoice_sequences",
                schema: "billing");

            migrationBuilder.DropTable(
                name: "subscription_checkouts",
                schema: "billing");

            migrationBuilder.DropIndex(
                name: "ux_subscription_payments_invoice_number",
                schema: "billing",
                table: "subscription_payments");

            migrationBuilder.DropColumn(
                name: "collected_by",
                schema: "billing",
                table: "subscription_payments");

            migrationBuilder.DropColumn(
                name: "invoice_number",
                schema: "billing",
                table: "subscription_payments");
        }
    }
}
