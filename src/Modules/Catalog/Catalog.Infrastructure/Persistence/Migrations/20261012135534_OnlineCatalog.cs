using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OnlineCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allergen_ids",
                schema: "catalog",
                table: "products");

            migrationBuilder.AddColumn<string>(
                name: "allergens",
                schema: "catalog",
                table: "products",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<string>(
                name: "hidden_channels",
                schema: "catalog",
                table: "products",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "allergens",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "hidden_channels",
                schema: "catalog",
                table: "products");

            migrationBuilder.AddColumn<string>(
                name: "allergen_ids",
                schema: "catalog",
                table: "products",
                type: "jsonb",
                nullable: false,
                defaultValue: "");
        }
    }
}
