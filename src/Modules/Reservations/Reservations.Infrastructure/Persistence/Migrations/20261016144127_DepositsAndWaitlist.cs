using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reservations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DepositsAndWaitlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "deposit_amount",
                schema: "reservations",
                table: "reservations",
                type: "numeric(19,3)",
                precision: 19,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deposit_currency",
                schema: "reservations",
                table: "reservations",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deposit_due_before_utc",
                schema: "reservations",
                table: "reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "deposit_settled",
                schema: "reservations",
                table: "reservations",
                type: "numeric(19,3)",
                precision: 19,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "deposit_status",
                schema: "reservations",
                table: "reservations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "deposit_amount",
                schema: "reservations",
                table: "location_policies",
                type: "numeric(19,3)",
                precision: 19,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "deposit_basis",
                schema: "reservations",
                table: "location_policies",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "deposit_currency",
                schema: "reservations",
                table: "location_policies",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "deposit_min_party_size",
                schema: "reservations",
                table: "location_policies",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "deposit_mode",
                schema: "reservations",
                table: "location_policies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "deposit_payment_window",
                schema: "reservations",
                table: "location_policies",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 30, 0, 0));

            migrationBuilder.AddColumn<bool>(
                name: "waitlist_accepts_online",
                schema: "reservations",
                table: "location_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "waitlist_offer_window",
                schema: "reservations",
                table: "location_policies",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 15, 0, 0));

            migrationBuilder.CreateTable(
                name: "waitlist_entries",
                schema: "reservations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    party_size = table.Column<int>(type: "integer", nullable: false),
                    desired_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    flexibility_minutes = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    offered_start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    offer_expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    missed_offers = table.Column<int>(type: "integer", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    guest_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    guest_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    guest_phone = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waitlist_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reservations_unpaid_deposit_due",
                schema: "reservations",
                table: "reservations",
                column: "deposit_due_before_utc",
                filter: "status = 1 AND deposit_status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_open",
                schema: "reservations",
                table: "waitlist_entries",
                column: "desired_start_utc",
                filter: "status IN (1, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_tenant_id_customer_id",
                schema: "reservations",
                table: "waitlist_entries",
                columns: new[] { "tenant_id", "customer_id" },
                filter: "customer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_tenant_id_location_id_desired_start_utc",
                schema: "reservations",
                table: "waitlist_entries",
                columns: new[] { "tenant_id", "location_id", "desired_start_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "waitlist_entries",
                schema: "reservations");

            migrationBuilder.DropIndex(
                name: "ix_reservations_unpaid_deposit_due",
                schema: "reservations",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "deposit_amount",
                schema: "reservations",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "deposit_currency",
                schema: "reservations",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "deposit_due_before_utc",
                schema: "reservations",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "deposit_settled",
                schema: "reservations",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "deposit_status",
                schema: "reservations",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "deposit_amount",
                schema: "reservations",
                table: "location_policies");

            migrationBuilder.DropColumn(
                name: "deposit_basis",
                schema: "reservations",
                table: "location_policies");

            migrationBuilder.DropColumn(
                name: "deposit_currency",
                schema: "reservations",
                table: "location_policies");

            migrationBuilder.DropColumn(
                name: "deposit_min_party_size",
                schema: "reservations",
                table: "location_policies");

            migrationBuilder.DropColumn(
                name: "deposit_mode",
                schema: "reservations",
                table: "location_policies");

            migrationBuilder.DropColumn(
                name: "deposit_payment_window",
                schema: "reservations",
                table: "location_policies");

            migrationBuilder.DropColumn(
                name: "waitlist_accepts_online",
                schema: "reservations",
                table: "location_policies");

            migrationBuilder.DropColumn(
                name: "waitlist_offer_window",
                schema: "reservations",
                table: "location_policies");
        }
    }
}
