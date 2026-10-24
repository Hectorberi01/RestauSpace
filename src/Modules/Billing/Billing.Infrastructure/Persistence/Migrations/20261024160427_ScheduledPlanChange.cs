using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledPlanChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "scheduled_plan_id",
                schema: "billing",
                table: "subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_scheduled_plan_id",
                schema: "billing",
                table: "subscriptions",
                column: "scheduled_plan_id");

            migrationBuilder.AddForeignKey(
                name: "FK_subscriptions_plans_scheduled_plan_id",
                schema: "billing",
                table: "subscriptions",
                column: "scheduled_plan_id",
                principalSchema: "billing",
                principalTable: "plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_subscriptions_plans_scheduled_plan_id",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropIndex(
                name: "ix_subscriptions_scheduled_plan_id",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "scheduled_plan_id",
                schema: "billing",
                table: "subscriptions");
        }
    }
}
