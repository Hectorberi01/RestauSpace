using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Promotions.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "promotions");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "promotions",
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
                name: "promotions",
                schema: "promotions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    rules = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    usage_count = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expired_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    requires_coupon = table.Column<bool>(type: "boolean", nullable: false),
                    valid_from_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    valid_until_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotions", x => x.id);
                    table.CheckConstraint("ck_promotions_expired_at", "(status = 4) = (expired_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_promotions_period", "valid_from_utc < valid_until_utc");
                    table.CheckConstraint("ck_promotions_status", "status IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_promotions_usage_count_not_negative", "usage_count >= 0");
                });

            migrationBuilder.CreateTable(
                name: "coupons",
                schema: "promotions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    max_uses = table.Column<int>(type: "integer", nullable: false),
                    usage_count = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupons", x => x.id);
                    table.CheckConstraint("ck_coupons_code_format", "code ~ '^[A-Z0-9-]{3,32}$'");
                    table.CheckConstraint("ck_coupons_max_uses_positive", "max_uses > 0");
                    table.CheckConstraint("ck_coupons_status", "status IN (1, 2)");
                    table.CheckConstraint("ck_coupons_usage_count_in_range", "usage_count >= 0 AND usage_count <= max_uses");
                    table.ForeignKey(
                        name: "FK_coupons_promotions_promotion_id",
                        column: x => x.promotion_id,
                        principalSchema: "promotions",
                        principalTable: "promotions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "promotion_usages",
                schema: "promotions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reverted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    discount = table.Column<decimal>(type: "numeric(19,3)", precision: 19, scale: 3, nullable: false),
                    discount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_promotion_usages", x => x.id);
                    table.CheckConstraint("ck_promotion_usages_discount_currency", "discount_currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_promotion_usages_discount_positive", "discount > 0");
                    table.CheckConstraint("ck_promotion_usages_reverted_after_applied", "reverted_at_utc IS NULL OR reverted_at_utc >= applied_at_utc");
                    table.ForeignKey(
                        name: "FK_promotion_usages_promotions_promotion_id",
                        column: x => x.promotion_id,
                        principalSchema: "promotions",
                        principalTable: "promotions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "coupon_usages",
                schema: "promotions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    coupon_id = table.Column<Guid>(type: "uuid", nullable: false),
                    promotion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    redeemed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    released_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupon_usages", x => x.id);
                    table.CheckConstraint("ck_coupon_usages_released_after_redeemed", "released_at_utc IS NULL OR released_at_utc >= redeemed_at_utc");
                    table.ForeignKey(
                        name: "FK_coupon_usages_coupons_coupon_id",
                        column: x => x.coupon_id,
                        principalSchema: "promotions",
                        principalTable: "coupons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_coupon_usages_coupon_id_order_id",
                schema: "promotions",
                table: "coupon_usages",
                columns: new[] { "coupon_id", "order_id" },
                unique: true,
                filter: "released_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_coupon_usages_tenant_id_order_id",
                schema: "promotions",
                table: "coupon_usages",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_coupons_promotion_id",
                schema: "promotions",
                table: "coupons",
                column: "promotion_id");

            migrationBuilder.CreateIndex(
                name: "ix_coupons_tenant_id_code",
                schema: "promotions",
                table: "coupons",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "promotions",
                table: "outbox_messages",
                column: "id",
                filter: "processed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_usages_promotion_id_order_id",
                schema: "promotions",
                table: "promotion_usages",
                columns: new[] { "promotion_id", "order_id" },
                unique: true,
                filter: "reverted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_promotion_usages_tenant_id_order_id",
                schema: "promotions",
                table: "promotion_usages",
                columns: new[] { "tenant_id", "order_id" });

            migrationBuilder.CreateIndex(
                name: "ix_promotions_tenant_id_created_at_utc",
                schema: "promotions",
                table: "promotions",
                columns: new[] { "tenant_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_promotions_tenant_id_status_valid_until_utc",
                schema: "promotions",
                table: "promotions",
                columns: new[] { "tenant_id", "status", "valid_until_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coupon_usages",
                schema: "promotions");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "promotions");

            migrationBuilder.DropTable(
                name: "promotion_usages",
                schema: "promotions");

            migrationBuilder.DropTable(
                name: "coupons",
                schema: "promotions");

            migrationBuilder.DropTable(
                name: "promotions",
                schema: "promotions");
        }
    }
}
