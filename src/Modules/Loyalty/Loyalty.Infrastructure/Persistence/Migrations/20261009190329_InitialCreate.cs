using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Loyalty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "loyalty");

            migrationBuilder.CreateTable(
                name: "loyalty_accounts",
                schema: "loyalty",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_accounts", x => x.id);
                    table.CheckConstraint("ck_loyalty_accounts_closed_at", "(status = 3) = (closed_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_loyalty_accounts_closed_balance", "status <> 3 OR balance = 0");
                    table.CheckConstraint("ck_loyalty_accounts_status", "status IN (1, 2, 3)");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "loyalty",
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
                name: "rewards",
                schema: "loyalty",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    point_cost = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    discount_value = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    discount_value_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rewards", x => x.id);
                    table.CheckConstraint("ck_rewards_discount_value_currency", "discount_value_currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_rewards_discount_value_positive", "discount_value > 0");
                    table.CheckConstraint("ck_rewards_point_cost_positive", "point_cost > 0");
                    table.CheckConstraint("ck_rewards_status", "status IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "loyalty_transactions",
                schema: "loyalty",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    direction = table.Column<int>(type: "integer", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    reversed_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_transactions", x => x.id);
                    table.CheckConstraint("ck_loyalty_transactions_closure_reference", "type <> 6 OR reference IS NULL");
                    table.CheckConstraint("ck_loyalty_transactions_direction", "direction IN (1, 2)");
                    table.CheckConstraint("ck_loyalty_transactions_points_positive", "points > 0");
                    table.CheckConstraint("ck_loyalty_transactions_reversal_link", "(type = 5) = (reversed_transaction_id IS NOT NULL)");
                    table.CheckConstraint("ck_loyalty_transactions_type", "type IN (1, 2, 3, 4, 5, 6)");
                    table.ForeignKey(
                        name: "FK_loyalty_transactions_loyalty_accounts_account_id",
                        column: x => x.account_id,
                        principalSchema: "loyalty",
                        principalTable: "loyalty_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_loyalty_transactions_loyalty_transactions_reversed_transact~",
                        column: x => x.reversed_transaction_id,
                        principalSchema: "loyalty",
                        principalTable: "loyalty_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_accounts_tenant_id_customer_id",
                schema: "loyalty",
                table: "loyalty_accounts",
                columns: new[] { "tenant_id", "customer_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_transactions_account_id_type_reference",
                schema: "loyalty",
                table: "loyalty_transactions",
                columns: new[] { "account_id", "type", "reference" },
                unique: true,
                filter: "reference IS NOT NULL AND type NOT IN (5, 6)");

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_transactions_reversed_transaction_id",
                schema: "loyalty",
                table: "loyalty_transactions",
                column: "reversed_transaction_id",
                unique: true,
                filter: "reversed_transaction_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_transactions_tenant_id_account_id_occurred_at_utc",
                schema: "loyalty",
                table: "loyalty_transactions",
                columns: new[] { "tenant_id", "account_id", "occurred_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "loyalty",
                table: "outbox_messages",
                column: "id",
                filter: "processed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_rewards_tenant_id_status",
                schema: "loyalty",
                table: "rewards",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loyalty_transactions",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "rewards",
                schema: "loyalty");

            migrationBuilder.DropTable(
                name: "loyalty_accounts",
                schema: "loyalty");
        }
    }
}
