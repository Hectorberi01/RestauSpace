using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reviews.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublishedFeedbacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at_utc",
                schema: "reviews",
                table: "feedbacks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_feedbacks_tenant_id_published",
                schema: "reviews",
                table: "feedbacks",
                columns: new[] { "tenant_id", "submitted_at_utc" },
                filter: "published_at_utc IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_feedbacks_published",
                schema: "reviews",
                table: "feedbacks",
                sql: "published_at_utc IS NULL OR status <> 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_feedbacks_tenant_id_published",
                schema: "reviews",
                table: "feedbacks");

            migrationBuilder.DropCheckConstraint(
                name: "ck_feedbacks_published",
                schema: "reviews",
                table: "feedbacks");

            migrationBuilder.DropColumn(
                name: "published_at_utc",
                schema: "reviews",
                table: "feedbacks");
        }
    }
}
