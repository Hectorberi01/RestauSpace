using Microsoft.EntityFrameworkCore.Design;

namespace Notifications.Infrastructure.Persistence;

/// <summary>
/// Pour <c>dotnet ef</c> uniquement : pas de requête, donc pas de tenant. Chaîne de connexion : variable
/// <c>NOTIFICATIONS_DB</c>, sinon la base locale.
/// </summary>
internal sealed class NotificationsDbContextFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public const string ConnectionStringVariable = "NOTIFICATIONS_DB";

    public NotificationsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable) ?? "Host=localhost;Database=saasrestaurant";
        var options = new DbContextOptionsBuilder<NotificationsDbContext>();
        options.UseNotificationsNpgsql(connectionString);
        return new NotificationsDbContext(options.Options, new FixedTenantContext(null));
    }
}
