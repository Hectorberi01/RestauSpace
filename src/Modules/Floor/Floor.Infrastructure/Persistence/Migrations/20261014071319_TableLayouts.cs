using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Floor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TableLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "layout_height",
                schema: "floor",
                table: "restaurant_tables",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "layout_shape",
                schema: "floor",
                table: "restaurant_tables",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "layout_width",
                schema: "floor",
                table: "restaurant_tables",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "layout_x",
                schema: "floor",
                table: "restaurant_tables",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "layout_y",
                schema: "floor",
                table: "restaurant_tables",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "layout_height",
                schema: "floor",
                table: "restaurant_tables");

            migrationBuilder.DropColumn(
                name: "layout_shape",
                schema: "floor",
                table: "restaurant_tables");

            migrationBuilder.DropColumn(
                name: "layout_width",
                schema: "floor",
                table: "restaurant_tables");

            migrationBuilder.DropColumn(
                name: "layout_x",
                schema: "floor",
                table: "restaurant_tables");

            migrationBuilder.DropColumn(
                name: "layout_y",
                schema: "floor",
                table: "restaurant_tables");
        }
    }
}
