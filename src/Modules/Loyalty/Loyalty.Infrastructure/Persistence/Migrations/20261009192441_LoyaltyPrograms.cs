using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Loyalty.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LoyaltyPrograms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "loyalty_programs",
                schema: "loyalty",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    points_per_currency_unit = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: false),
                    auto_enroll = table.Column<bool>(type: "boolean", nullable: false),
                    inactivity_expiry_days = table.Column<int>(type: "integer", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_loyalty_programs", x => x.tenant_id);
                    table.CheckConstraint("ck_loyalty_programs_inactivity_expiry_days", "inactivity_expiry_days IS NULL OR inactivity_expiry_days BETWEEN 30 AND 3650");
                    table.CheckConstraint("ck_loyalty_programs_points_per_currency_unit", "points_per_currency_unit > 0 AND points_per_currency_unit <= 1000");
                });

            migrationBuilder.CreateIndex(
                name: "ix_loyalty_programs_inactivity_expiry_days",
                schema: "loyalty",
                table: "loyalty_programs",
                column: "inactivity_expiry_days",
                filter: "inactivity_expiry_days IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "loyalty_programs",
                schema: "loyalty");
        }
    }
}
