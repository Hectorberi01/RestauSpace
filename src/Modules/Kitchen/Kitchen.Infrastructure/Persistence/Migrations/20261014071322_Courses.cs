using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kitchen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Courses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "courses",
                schema: "kitchen",
                table: "station_routings",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb");

            migrationBuilder.AddColumn<int>(
                name: "course",
                schema: "kitchen",
                table: "kitchen_ticket_items",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "is_held",
                schema: "kitchen",
                table: "kitchen_ticket_items",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "courses",
                schema: "kitchen",
                table: "station_routings");

            migrationBuilder.DropColumn(
                name: "course",
                schema: "kitchen",
                table: "kitchen_ticket_items");

            migrationBuilder.DropColumn(
                name: "is_held",
                schema: "kitchen",
                table: "kitchen_ticket_items");
        }
    }
}
