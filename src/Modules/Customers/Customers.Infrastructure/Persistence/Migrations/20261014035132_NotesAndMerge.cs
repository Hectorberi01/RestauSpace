using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Customers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotesAndMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_customers_status",
                schema: "customers",
                table: "customers");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "merged_at_utc",
                schema: "customers",
                table: "customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "merged_into_id",
                schema: "customers",
                table: "customers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "customer_notes",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_notes", x => x.id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_customers_merged_has_no_personal_data",
                schema: "customers",
                table: "customers",
                sql: "(status = 3) = (merged_into_id IS NOT NULL) AND (status <> 3 OR (email IS NULL AND phone IS NULL AND preferences IS NULL AND merged_at_utc IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customers_status",
                schema: "customers",
                table: "customers",
                sql: "status IN (1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "ix_customer_notes_tenant_id_customer_id_created_at_utc",
                schema: "customers",
                table: "customer_notes",
                columns: new[] { "tenant_id", "customer_id", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_notes",
                schema: "customers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customers_merged_has_no_personal_data",
                schema: "customers",
                table: "customers");

            migrationBuilder.DropCheckConstraint(
                name: "ck_customers_status",
                schema: "customers",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "merged_at_utc",
                schema: "customers",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "merged_into_id",
                schema: "customers",
                table: "customers");

            migrationBuilder.AddCheckConstraint(
                name: "ck_customers_status",
                schema: "customers",
                table: "customers",
                sql: "status IN (1, 2)");
        }
    }
}
