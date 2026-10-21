using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Floor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TableQrTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "qr_token",
                schema: "floor",
                table: "restaurant_tables",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_restaurant_tables_qr_token",
                schema: "floor",
                table: "restaurant_tables",
                column: "qr_token",
                unique: true,
                filter: "qr_token IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_restaurant_tables_qr_token",
                schema: "floor",
                table: "restaurant_tables");

            migrationBuilder.DropColumn(
                name: "qr_token",
                schema: "floor",
                table: "restaurant_tables");
        }
    }
}
