using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Customers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "customers");

            migrationBuilder.CreateTable(
                name: "consents",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    granted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    withdrawn_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawal_source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    withdrawal_reason = table.Column<int>(type: "integer", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consents", x => x.id);
                    table.CheckConstraint("ck_consents_channel", "channel IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_consents_status", "status IN (1, 2)");
                    table.CheckConstraint("ck_consents_withdrawal", "(status = 1 AND withdrawn_at_utc IS NULL AND withdrawal_source IS NULL AND withdrawal_reason IS NULL) OR (status = 2 AND withdrawn_at_utc IS NOT NULL AND withdrawal_source IS NOT NULL AND withdrawal_reason IS NOT NULL)");
                    table.CheckConstraint("ck_consents_withdrawal_reason", "withdrawal_reason IS NULL OR withdrawal_reason IN (1, 2, 3, 4, 5)");
                });

            migrationBuilder.CreateTable(
                name: "customer_settings",
                schema: "customers",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_requirement = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_settings", x => x.tenant_id);
                    table.CheckConstraint("ck_customer_settings_contact_requirement", "contact_requirement IN (1, 2, 3, 4)");
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    phone = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    contact_requirement = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    preferences = table.Column<string>(type: "jsonb", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    anonymized_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true, computedColumnSql: "upper(name)", stored: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                    table.CheckConstraint("ck_customers_anonymized_has_no_personal_data", "status <> 2 OR (email IS NULL AND phone IS NULL AND preferences IS NULL AND anonymized_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_customers_contact_requirement", "contact_requirement IN (1, 2, 3, 4)");
                    table.CheckConstraint("ck_customers_preferences", "preferences IS NULL OR jsonb_typeof(preferences) = 'object'");
                    table.CheckConstraint("ck_customers_status", "status IN (1, 2)");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "customers",
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
                name: "consent_changes",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    status = table.Column<int>(type: "integer", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<int>(type: "integer", nullable: true),
                    consent_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consent_changes", x => x.id);
                    table.CheckConstraint("ck_consent_changes_reason", "reason IS NULL OR reason IN (1, 2, 3, 4, 5)");
                    table.CheckConstraint("ck_consent_changes_status", "status IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_consent_changes_consents_consent_id",
                        column: x => x.consent_id,
                        principalSchema: "customers",
                        principalTable: "consents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "customer_tags",
                schema: "customers",
                columns: table => new
                {
                    value = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_tags", x => new { x.customer_id, x.value });
                    table.ForeignKey(
                        name: "FK_customer_tags_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "customers",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_consent_changes_consent_id",
                schema: "customers",
                table: "consent_changes",
                column: "consent_id");

            migrationBuilder.CreateIndex(
                name: "ix_consents_tenant_id_customer_id_channel",
                schema: "customers",
                table: "consents",
                columns: new[] { "tenant_id", "customer_id", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_customer_tags_value",
                schema: "customers",
                table: "customer_tags",
                column: "value");

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_id_email",
                schema: "customers",
                table: "customers",
                columns: new[] { "tenant_id", "email" },
                filter: "email IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_id_normalized_name",
                schema: "customers",
                table: "customers",
                columns: new[] { "tenant_id", "normalized_name" });

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_id_phone",
                schema: "customers",
                table: "customers",
                columns: new[] { "tenant_id", "phone" },
                filter: "phone IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "customers",
                table: "outbox_messages",
                column: "id",
                filter: "processed_at_utc IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consent_changes",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "customer_settings",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "customer_tags",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "consents",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "customers");
        }
    }
}
