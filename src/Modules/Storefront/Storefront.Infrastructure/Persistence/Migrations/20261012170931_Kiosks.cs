using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Storefront.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Kiosks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "kiosk_devices",
                schema: "storefront",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    token_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    token_hint = table.Column<string>(type: "character(4)", fixedLength: true, maxLength: 4, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    paired_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kiosk_devices", x => x.id);
                    table.CheckConstraint("ck_kiosk_devices_revoked", "status <> 2 OR revoked_at_utc IS NOT NULL");
                    table.CheckConstraint("ck_kiosk_devices_status", "status IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "kiosk_pairings",
                schema: "storefront",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    secret_hash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    device_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    device_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kiosk_pairings", x => x.id);
                    table.CheckConstraint("ck_kiosk_pairings_approved", "status = 1 OR (location_id IS NOT NULL AND device_name IS NOT NULL)");
                    table.CheckConstraint("ck_kiosk_pairings_completed", "status <> 3 OR device_id IS NOT NULL");
                    table.CheckConstraint("ck_kiosk_pairings_status", "status IN (1, 2, 3)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_kiosk_devices_tenant_id",
                schema: "storefront",
                table: "kiosk_devices",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ux_kiosk_devices_token_hash",
                schema: "storefront",
                table: "kiosk_devices",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kiosk_pairings_tenant_id_code_hash_pending",
                schema: "storefront",
                table: "kiosk_pairings",
                columns: new[] { "tenant_id", "code_hash" },
                filter: "status = 1");

            migrationBuilder.CreateIndex(
                name: "ix_kiosk_pairings_tenant_id_expires_at_utc",
                schema: "storefront",
                table: "kiosk_pairings",
                columns: new[] { "tenant_id", "expires_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kiosk_devices",
                schema: "storefront");

            migrationBuilder.DropTable(
                name: "kiosk_pairings",
                schema: "storefront");
        }
    }
}
