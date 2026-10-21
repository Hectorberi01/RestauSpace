using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Floor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TableRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "layout_rotation",
                schema: "floor",
                table: "restaurant_tables",
                type: "integer",
                nullable: true,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "layout_rotation",
                schema: "floor",
                table: "restaurant_tables");
        }
    }
}
