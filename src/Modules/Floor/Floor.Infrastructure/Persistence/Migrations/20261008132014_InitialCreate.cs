using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Floor.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "floor");

            migrationBuilder.CreateTable(
                name: "dining_areas",
                schema: "floor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dining_areas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "floor",
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
                name: "table_sessions",
                schema: "floor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    guest_count = table.Column<int>(type: "integer", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    opened_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_table_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "restaurant_tables",
                schema: "floor",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    dining_area_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_label = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true, computedColumnSql: "upper(label)", stored: true),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_restaurant_tables", x => x.id);
                    table.ForeignKey(
                        name: "FK_restaurant_tables_dining_areas_dining_area_id",
                        column: x => x.dining_area_id,
                        principalSchema: "floor",
                        principalTable: "dining_areas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dining_areas_tenant_id_location_id",
                schema: "floor",
                table: "dining_areas",
                columns: new[] { "tenant_id", "location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "floor",
                table: "outbox_messages",
                column: "id",
                filter: "processed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_restaurant_tables_dining_area_id_normalized_label",
                schema: "floor",
                table: "restaurant_tables",
                columns: new[] { "dining_area_id", "normalized_label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_table_sessions_tenant_id_location_id_open",
                schema: "floor",
                table: "table_sessions",
                columns: new[] { "tenant_id", "location_id" },
                filter: "closed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_table_sessions_tenant_id_table_id_open",
                schema: "floor",
                table: "table_sessions",
                columns: new[] { "tenant_id", "table_id" },
                unique: true,
                filter: "closed_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "floor");

            migrationBuilder.DropTable(
                name: "restaurant_tables",
                schema: "floor");

            migrationBuilder.DropTable(
                name: "table_sessions",
                schema: "floor");

            migrationBuilder.DropTable(
                name: "dining_areas",
                schema: "floor");
        }
    }
}
