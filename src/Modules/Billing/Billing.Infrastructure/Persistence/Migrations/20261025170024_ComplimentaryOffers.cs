using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ComplimentaryOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "after_complimentary",
                schema: "billing",
                table: "tenant_add_ons",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "complimentary_renewal_months",
                schema: "billing",
                table: "tenant_add_ons",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "complimentary_until_utc",
                schema: "billing",
                table: "tenant_add_ons",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "complimentary_renewal_months",
                schema: "billing",
                table: "subscriptions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_add_ons_complimentary_until_utc",
                schema: "billing",
                table: "tenant_add_ons",
                column: "complimentary_until_utc",
                filter: "complimentary_until_utc IS NOT NULL AND status = 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_add_ons_after_complimentary",
                schema: "billing",
                table: "tenant_add_ons",
                sql: "after_complimentary IS NULL OR after_complimentary IN (1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tenant_add_ons_complimentary_renewal_months",
                schema: "billing",
                table: "tenant_add_ons",
                sql: "complimentary_renewal_months IS NULL OR complimentary_renewal_months BETWEEN 1 AND 12");

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscriptions_complimentary_renewal_months",
                schema: "billing",
                table: "subscriptions",
                sql: "complimentary_renewal_months IS NULL OR (is_complimentary AND complimentary_renewal_months BETWEEN 1 AND 12)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_tenant_add_ons_complimentary_until_utc",
                schema: "billing",
                table: "tenant_add_ons");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenant_add_ons_after_complimentary",
                schema: "billing",
                table: "tenant_add_ons");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tenant_add_ons_complimentary_renewal_months",
                schema: "billing",
                table: "tenant_add_ons");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscriptions_complimentary_renewal_months",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "after_complimentary",
                schema: "billing",
                table: "tenant_add_ons");

            migrationBuilder.DropColumn(
                name: "complimentary_renewal_months",
                schema: "billing",
                table: "tenant_add_ons");

            migrationBuilder.DropColumn(
                name: "complimentary_until_utc",
                schema: "billing",
                table: "tenant_add_ons");

            migrationBuilder.DropColumn(
                name: "complimentary_renewal_months",
                schema: "billing",
                table: "subscriptions");
        }
    }
}
