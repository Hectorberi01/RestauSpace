using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Staff.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TillsAndTimeClock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "pin_failures",
                schema: "staff",
                table: "staff_members",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "pin_hash",
                schema: "staff",
                table: "staff_members",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pin_locked_until_utc",
                schema: "staff",
                table: "staff_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tills",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    auto_lock_minutes = table.Column<int>(type: "integer", nullable: false),
                    secret_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tills", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "time_entries",
                schema: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clocked_in_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    clocked_out_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_automatically = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_time_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tills_tenant_id",
                schema: "staff",
                table: "tills",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_tenant_id_clocked_in_at_utc",
                schema: "staff",
                table: "time_entries",
                columns: new[] { "tenant_id", "clocked_in_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_time_entries_tenant_id_staff_member_id_open",
                schema: "staff",
                table: "time_entries",
                columns: new[] { "tenant_id", "staff_member_id" },
                unique: true,
                filter: "clocked_out_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tills",
                schema: "staff");

            migrationBuilder.DropTable(
                name: "time_entries",
                schema: "staff");

            migrationBuilder.DropColumn(
                name: "pin_failures",
                schema: "staff",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "pin_hash",
                schema: "staff",
                table: "staff_members");

            migrationBuilder.DropColumn(
                name: "pin_locked_until_utc",
                schema: "staff",
                table: "staff_members");
        }
    }
}
