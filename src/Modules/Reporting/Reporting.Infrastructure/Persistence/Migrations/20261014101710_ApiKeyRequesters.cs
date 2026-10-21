using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ApiKeyRequesters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "scheduled_by_kind",
                schema: "reporting",
                table: "report_deliveries",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "requested_by_kind",
                schema: "reporting",
                table: "export_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddCheckConstraint(
                name: "ck_report_deliveries_scheduled_by_kind",
                schema: "reporting",
                table: "report_deliveries",
                sql: "scheduled_by_kind IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_export_jobs_requested_by_kind",
                schema: "reporting",
                table: "export_jobs",
                sql: "requested_by_kind IN (1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_report_deliveries_scheduled_by_kind",
                schema: "reporting",
                table: "report_deliveries");

            migrationBuilder.DropCheckConstraint(
                name: "ck_export_jobs_requested_by_kind",
                schema: "reporting",
                table: "export_jobs");

            migrationBuilder.DropColumn(
                name: "scheduled_by_kind",
                schema: "reporting",
                table: "report_deliveries");

            migrationBuilder.DropColumn(
                name: "requested_by_kind",
                schema: "reporting",
                table: "export_jobs");
        }
    }
}
