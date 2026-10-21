using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CampaignsAndMarketingRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "campaigns",
                schema: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    tag = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scheduled_for_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processed = table.Column<int>(type: "integer", nullable: false),
                    created = table.Column<int>(type: "integer", nullable: false),
                    skipped = table.Column<int>(type: "integer", nullable: false),
                    skipped_for_frequency = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    variables = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaigns", x => x.id);
                    table.CheckConstraint("ck_campaigns_channel", "channel IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_campaigns_counters", "processed >= 0 AND created >= 0 AND skipped >= 0 AND skipped_for_frequency >= 0");
                    table.CheckConstraint("ck_campaigns_status", "status IN (1, 2, 3, 4, 5)");
                });

            migrationBuilder.CreateTable(
                name: "marketing_rules",
                schema: "notifications",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    minimum_interval_days = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_marketing_rules", x => x.tenant_id);
                    table.CheckConstraint("ck_marketing_rules_interval", "minimum_interval_days BETWEEN 0 AND 30");
                });

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_status_scheduled_for_utc",
                schema: "notifications",
                table: "campaigns",
                columns: new[] { "status", "scheduled_for_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_campaigns_tenant_id_created_at_utc",
                schema: "notifications",
                table: "campaigns",
                columns: new[] { "tenant_id", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "campaigns",
                schema: "notifications");

            migrationBuilder.DropTable(
                name: "marketing_rules",
                schema: "notifications");
        }
    }
}
