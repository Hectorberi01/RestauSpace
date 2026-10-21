using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reviews.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SurveyResponses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "survey_responses",
                schema: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    survey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    answers = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_survey_responses", x => x.id);
                    table.CheckConstraint("ck_survey_responses_answers", "jsonb_typeof(answers) = 'array'");
                });

            migrationBuilder.CreateIndex(
                name: "ix_survey_responses_tenant_id_survey_id_order_id",
                schema: "reviews",
                table: "survey_responses",
                columns: new[] { "tenant_id", "survey_id", "order_id" },
                unique: true,
                filter: "order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_survey_responses_tenant_id_survey_id_submitted_at_utc",
                schema: "reviews",
                table: "survey_responses",
                columns: new[] { "tenant_id", "survey_id", "submitted_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "survey_responses",
                schema: "reviews");
        }
    }
}
