using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Printing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KitchenTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_print_jobs_kind",
                schema: "printing",
                table: "print_jobs");

            migrationBuilder.AddColumn<Guid>(
                name: "kitchen_station_id",
                schema: "printing",
                table: "printers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "prints_kitchen_tickets",
                schema: "printing",
                table: "printers",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_print_jobs_kind",
                schema: "printing",
                table: "print_jobs",
                sql: "kind IN (1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_print_jobs_kind",
                schema: "printing",
                table: "print_jobs");

            migrationBuilder.DropColumn(
                name: "kitchen_station_id",
                schema: "printing",
                table: "printers");

            migrationBuilder.DropColumn(
                name: "prints_kitchen_tickets",
                schema: "printing",
                table: "printers");

            migrationBuilder.AddCheckConstraint(
                name: "ck_print_jobs_kind",
                schema: "printing",
                table: "print_jobs",
                sql: "kind IN (1, 2)");
        }
    }
}
