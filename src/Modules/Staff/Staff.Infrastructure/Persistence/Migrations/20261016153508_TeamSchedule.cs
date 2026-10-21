using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Staff.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeamSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "schedule_settings",
                schema: "staff",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    positions = table.Column<string>(type: "jsonb", nullable: false),
                    weekly_hours_limit = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_settings", x => new { x.tenant_id, x.location_id });
                });

            migrationBuilder.CreateTable(
                name: "schedule_weeks",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week_start = table.Column<DateOnly>(type: "date", nullable: false),
                    published_version = table.Column<int>(type: "integer", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    published_shifts = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb"),
                    shifts = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'[]'::jsonb")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_weeks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "shift_swap_requests",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    week_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    shift_starts_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shift_swap_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "time_off_requests",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_day = table.Column<DateOnly>(type: "date", nullable: false),
                    last_day = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    requested_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_time_off_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_weeks_tenant_id_location_id_week_start",
                schema: "staff",
                table: "schedule_weeks",
                columns: new[] { "tenant_id", "location_id", "week_start" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_requests_tenant_id_shift_id",
                schema: "staff",
                table: "shift_swap_requests",
                columns: new[] { "tenant_id", "shift_id" });

            migrationBuilder.CreateIndex(
                name: "ix_shift_swap_requests_tenant_id_shift_starts_at_utc",
                schema: "staff",
                table: "shift_swap_requests",
                columns: new[] { "tenant_id", "shift_starts_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_time_off_requests_tenant_id_last_day",
                schema: "staff",
                table: "time_off_requests",
                columns: new[] { "tenant_id", "last_day" });

            migrationBuilder.CreateIndex(
                name: "ix_time_off_requests_tenant_id_staff_member_id",
                schema: "staff",
                table: "time_off_requests",
                columns: new[] { "tenant_id", "staff_member_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schedule_settings",
                schema: "staff");

            migrationBuilder.DropTable(
                name: "schedule_weeks",
                schema: "staff");

            migrationBuilder.DropTable(
                name: "shift_swap_requests",
                schema: "staff");

            migrationBuilder.DropTable(
                name: "time_off_requests",
                schema: "staff");
        }
    }
}
