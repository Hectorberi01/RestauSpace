using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GiftCardMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_method",
                schema: "payments",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_method_assignments_method",
                schema: "payments",
                table: "payment_method_assignments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_method",
                schema: "payments",
                table: "payments",
                sql: "method IN (1, 2, 3, 4, 5, 6, 20, 21, 22, 23, 24)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_method_assignments_method",
                schema: "payments",
                table: "payment_method_assignments",
                sql: "method IN (1, 2, 3, 4, 5, 6, 20, 21, 22, 23, 24)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payments_method",
                schema: "payments",
                table: "payments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_method_assignments_method",
                schema: "payments",
                table: "payment_method_assignments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payments_method",
                schema: "payments",
                table: "payments",
                sql: "method IN (1, 2, 3, 4, 5, 6, 20, 21, 22, 23)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_method_assignments_method",
                schema: "payments",
                table: "payment_method_assignments",
                sql: "method IN (1, 2, 3, 4, 5, 6, 20, 21, 22, 23)");
        }
    }
}
