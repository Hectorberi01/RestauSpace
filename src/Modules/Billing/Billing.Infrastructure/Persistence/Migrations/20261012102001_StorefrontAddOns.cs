using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StorefrontAddOns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "kind",
                schema: "billing",
                table: "plans",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "tenant_add_ons",
                schema: "billing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    activated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_add_ons", x => x.id);
                    table.CheckConstraint("ck_tenant_add_ons_quantity", "quantity BETWEEN 1 AND 1000");
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_add_ons_plan_id",
                schema: "billing",
                table: "tenant_add_ons",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ux_tenant_add_ons_tenant_id_plan_id_active",
                schema: "billing",
                table: "tenant_add_ons",
                columns: new[] { "tenant_id", "plan_id" },
                unique: true,
                filter: "status = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_add_ons",
                schema: "billing");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "billing",
                table: "plans");
        }
    }
}
