using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Reviews.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reviews");

            migrationBuilder.CreateTable(
                name: "feedbacks",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archived_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    customer_detached_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_feedbacks", x => x.id);
                    table.CheckConstraint("ck_feedbacks_detached", "customer_detached_at_utc IS NULL OR (customer_id IS NULL AND comment IS NULL)");
                    table.CheckConstraint("ck_feedbacks_rating", "rating BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_feedbacks_reviewed", "(reviewed_by IS NULL) = (reviewed_at_utc IS NULL)");
                    table.CheckConstraint("ck_feedbacks_status", "status IN (1, 2, 3)");
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "reviews",
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
                name: "surveys",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    published_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_surveys", x => x.id);
                    table.CheckConstraint("ck_surveys_closed", "(status = 3) = (closed_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_surveys_published", "status = 1 OR published_at_utc IS NOT NULL");
                    table.CheckConstraint("ck_surveys_status", "status IN (1, 2, 3)");
                });

            migrationBuilder.CreateTable(
                name: "survey_questions",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    survey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    choices = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_questions", x => x.id);
                    table.CheckConstraint("ck_survey_questions_position", "position >= 1");
                    table.CheckConstraint("ck_survey_questions_type", "type IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_survey_questions_surveys_survey_id",
                        column: x => x.survey_id,
                        principalSchema: "reviews",
                        principalTable: "surveys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feedbacks_tenant_id_customer_id",
                schema: "reviews",
                table: "feedbacks",
                columns: new[] { "tenant_id", "customer_id" },
                filter: "customer_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_feedbacks_tenant_id_location_id_submitted_at_utc",
                schema: "reviews",
                table: "feedbacks",
                columns: new[] { "tenant_id", "location_id", "submitted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_feedbacks_tenant_id_order_id",
                schema: "reviews",
                table: "feedbacks",
                columns: new[] { "tenant_id", "order_id" },
                unique: true,
                filter: "order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_feedbacks_tenant_id_submitted_at_utc",
                schema: "reviews",
                table: "feedbacks",
                columns: new[] { "tenant_id", "submitted_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "reviews",
                table: "outbox_messages",
                column: "id",
                filter: "processed_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_survey_questions_survey_id",
                schema: "reviews",
                table: "survey_questions",
                column: "survey_id");

            migrationBuilder.CreateIndex(
                name: "ix_surveys_tenant_id_status",
                schema: "reviews",
                table: "surveys",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feedbacks",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "survey_questions",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "surveys",
                schema: "reviews");
        }
    }
}
