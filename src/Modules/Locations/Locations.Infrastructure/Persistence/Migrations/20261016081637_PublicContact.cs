using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Locations.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublicContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "contact",
                schema: "locations",
                table: "locations",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "contact",
                schema: "locations",
                table: "locations");
        }
    }
}
