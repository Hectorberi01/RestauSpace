using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReservationDeposits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "applied_at_utc",
                schema: "payments",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "purpose",
                schema: "payments",
                table: "payments",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "reservation_id",
                schema: "payments",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_tenant_id_reservation_id",
                schema: "payments",
                table: "payments",
                columns: new[] { "tenant_id", "reservation_id" },
                filter: "reservation_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_deposit_reservation",
                schema: "payments",
                table: "payments",
                sql: "(purpose = 2) = (reservation_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_purpose",
                schema: "payments",
                table: "payments",
                sql: "purpose IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payments_tenant_id_reservation_id",
                schema: "payments",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_deposit_reservation",
                schema: "payments",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_purpose",
                schema: "payments",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "applied_at_utc",
                schema: "payments",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "purpose",
                schema: "payments",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "reservation_id",
                schema: "payments",
                table: "payments");
        }
    }
}
