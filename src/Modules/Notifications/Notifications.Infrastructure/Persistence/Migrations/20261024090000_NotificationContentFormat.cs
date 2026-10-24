using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Notifications.Infrastructure.Persistence.Migrations
{
    /// <summary>Format du contenu rendu (HTML ou texte), décidé d'après le modèle.</summary>
    public partial class NotificationContentFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_html",
                schema: "notifications",
                table: "notifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Messages déjà rendus : même règle qu'avant (corps e-mail qui commence par une balise).
            migrationBuilder.Sql(
                "UPDATE notifications.notifications SET is_html = TRUE WHERE content_channel = 1 " +
                "AND body ~* '^\\s*<(!doctype|html|body|div|p|table|h[1-6]|br|span|a)([^a-z0-9]|$)';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_html",
                schema: "notifications",
                table: "notifications");
        }
    }
}
