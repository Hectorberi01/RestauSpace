using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "avatar_updated_at_utc",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                schema: "identity",
                table: "users",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "first_name",
                schema: "identity",
                table: "users",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "language",
                schema: "identity",
                table: "users",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "fr");

            migrationBuilder.AddColumn<string>(
                name: "last_name",
                schema: "identity",
                table: "users",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                schema: "identity",
                table: "users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "product_news_emails",
                schema: "identity",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "time_zone",
                schema: "identity",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "Europe/Paris");

            migrationBuilder.AddColumn<string>(
                name: "device_label",
                schema: "identity",
                table: "sessions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "user_avatars",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<byte[]>(type: "bytea", nullable: false),
                    content_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_avatars", x => x.user_id);
                    table.CheckConstraint("ck_user_avatars_size", "octet_length(content) BETWEEN 1 AND 524288");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_avatars",
                schema: "identity");

            migrationBuilder.DropColumn(
                name: "avatar_updated_at_utc",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "city",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "first_name",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "language",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "last_name",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "phone",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "product_news_emails",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "time_zone",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "device_label",
                schema: "identity",
                table: "sessions");
        }
    }
}
