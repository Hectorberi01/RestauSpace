using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orders.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryPlatforms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_reference",
                schema: "orders",
                table: "orders",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "platform",
                schema: "orders",
                table: "orders",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_tenant_id_platform_external_reference",
                schema: "orders",
                table: "orders",
                columns: new[] { "tenant_id", "platform", "external_reference" },
                unique: true,
                filter: "external_reference IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_tenant_id_platform_external_reference",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "external_reference",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "platform",
                schema: "orders",
                table: "orders");
        }
    }
}
