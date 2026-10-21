using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscription_payments",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    recorded_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    paid_period_end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_period_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_payments", x => x.id);
                    table.CheckConstraint("ck_subscription_payments_amount_currency", "amount_currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_subscription_payments_amount_not_negative", "amount >= 0");
                    table.CheckConstraint("ck_subscription_payments_period", "paid_period_start_utc < paid_period_end_utc");
                    table.ForeignKey(
                        name: "FK_subscription_payments_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "billing",
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_plan_id",
                schema: "billing",
                table: "subscription_payments",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_recorded_at_utc",
                schema: "billing",
                table: "subscription_payments",
                column: "recorded_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_subscription_id",
                schema: "billing",
                table: "subscription_payments",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_payments_tenant_id_recorded_at_utc",
                schema: "billing",
                table: "subscription_payments",
                columns: new[] { "tenant_id", "recorded_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_payments",
                schema: "billing");
        }
    }
}
