using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryFee : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "delivery_fee_amount",
                schema: "orders",
                table: "orders",
                type: "numeric(19,3)",
                precision: 19,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "delivery_fee_reference",
                schema: "orders",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "delivery_fee_amount",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "delivery_fee_reference",
                schema: "orders",
                table: "orders");
        }
    }
}
