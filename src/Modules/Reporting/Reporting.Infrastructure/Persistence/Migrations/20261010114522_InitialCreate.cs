using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Reporting.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reporting");

            migrationBuilder.CreateTable(
                name: "daily_metrics",
                schema: "reporting",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    metric = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    dimension = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    value = table.Column<decimal>(type: "numeric(20,4)", precision: 20, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_metrics", x => new { x.tenant_id, x.location_id, x.day, x.metric, x.dimension, x.currency });
                    table.CheckConstraint("ck_daily_metrics_currency", "currency ~ '^([A-Z]{3})?$'");
                });

            migrationBuilder.CreateTable(
                name: "export_files",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "export_jobs",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    report_definition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    queued_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expired_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_jobs", x => x.id);
                    table.CheckConstraint("ck_export_jobs_failure_reason", "status <> 4 OR failure_reason IS NOT NULL");
                    table.CheckConstraint("ck_export_jobs_file", "status NOT IN (3, 5) OR file_id IS NOT NULL");
                    table.CheckConstraint("ck_export_jobs_status", "status IN (1, 2, 3, 4, 5)");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_subscriptions",
                schema: "reporting",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    monthly_amount = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    currency_code = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_subscriptions", x => x.tenant_id);
                    table.CheckConstraint("ck_platform_subscriptions_currency_code", "currency_code ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_platform_subscriptions_monthly_amount", "monthly_amount >= 0");
                    table.CheckConstraint("ck_platform_subscriptions_status", "status IN (1, 2, 3, 4, 5)");
                });

            migrationBuilder.CreateTable(
                name: "platform_tenants",
                schema: "reporting",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    observed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_tenants", x => x.tenant_id);
                    table.CheckConstraint("ck_platform_tenants_status", "status IN (1, 2, 3, 4)");
                });

            migrationBuilder.CreateTable(
                name: "processed_sources",
                schema: "reporting",
                columns: table => new
                {
                    source_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_sources", x => x.source_key);
                });

            migrationBuilder.CreateTable(
                name: "report_definitions",
                schema: "reporting",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    metrics = table.Column<string>(type: "jsonb", nullable: false),
                    filters = table.Column<string>(type: "jsonb", nullable: false),
                    schedule = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "export_requests",
                schema: "reporting",
                columns: table => new
                {
                    export_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_export_requests", x => x.export_job_id);
                    table.ForeignKey(
                        name: "FK_export_requests_export_jobs_export_job_id",
                        column: x => x.export_job_id,
                        principalSchema: "reporting",
                        principalTable: "export_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "report_deliveries",
                schema: "reporting",
                columns: table => new
                {
                    report_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_by = table.Column<Guid>(type: "uuid", nullable: false),
                    next_run_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_run_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_report_deliveries", x => x.report_definition_id);
                    table.ForeignKey(
                        name: "FK_report_deliveries_report_definitions_report_definition_id",
                        column: x => x.report_definition_id,
                        principalSchema: "reporting",
                        principalTable: "report_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_daily_metrics_day_metric",
                schema: "reporting",
                table: "daily_metrics",
                columns: new[] { "day", "metric" });

            migrationBuilder.CreateIndex(
                name: "ix_export_files_tenant_id",
                schema: "reporting",
                table: "export_files",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_export_jobs_status_queued_at_utc",
                schema: "reporting",
                table: "export_jobs",
                columns: new[] { "status", "queued_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_export_jobs_tenant_id_queued_at_utc",
                schema: "reporting",
                table: "export_jobs",
                columns: new[] { "tenant_id", "queued_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "reporting",
                table: "outbox_messages",
                column: "id",
                filter: "processed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_platform_subscriptions_status",
                schema: "reporting",
                table: "platform_subscriptions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_platform_tenants_status",
                schema: "reporting",
                table: "platform_tenants",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_report_definitions_tenant_id_name",
                schema: "reporting",
                table: "report_definitions",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_report_deliveries_next_run_at_utc",
                schema: "reporting",
                table: "report_deliveries",
                column: "next_run_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_metrics",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "export_files",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "export_requests",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "platform_subscriptions",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "platform_tenants",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "processed_sources",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "report_deliveries",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "export_jobs",
                schema: "reporting");

            migrationBuilder.DropTable(
                name: "report_definitions",
                schema: "reporting");
        }
    }
}
