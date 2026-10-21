using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tenants.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublicProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "public_profile",
                schema: "tenants",
                table: "tenants",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "public_profile",
                schema: "tenants",
                table: "tenants");
        }
    }
}
