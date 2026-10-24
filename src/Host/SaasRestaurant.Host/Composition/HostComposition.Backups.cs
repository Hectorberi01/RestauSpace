using System.Globalization;
using Backups.Api;
using Backups.Application.Abstractions;
using Npgsql;

namespace SaasRestaurant.Host;

internal static partial class HostComposition
{
    public static WebApplicationBuilder AddBackupsComposition(this WebApplicationBuilder builder, string connectionString)
    {
        // --- Backups : file des sauvegardes et restaurations, exécutées par l'agent du serveur de base. Clé S3 en LECTURE
        // seule (liens pré-signés) ; code 2FA vérifié par Identity.
        var backups = builder.Configuration.GetSection("Backups");
        var backupsConnection = builder.ConnectionStringFor("Backups", connectionString);
        builder.Services.AddBackupsModule(
            backupsConnection,
            settings =>
            {
                settings.DatabaseName = backups["DatabaseName"] is { Length: > 0 } name
                    ? name.Trim()
                    : new NpgsqlConnectionStringBuilder(backupsConnection).Database ?? string.Empty;
                settings.RetentionDays = backups.GetValue("RetentionDays", settings.RetentionDays);
                settings.KeepMin = backups.GetValue("KeepMin", settings.KeepMin);
                settings.ScheduleEnabled = backups.GetValue("Schedule:Enabled", settings.ScheduleEnabled);
                if (backups["Schedule:DailyAtUtc"] is { Length: > 0 } dailyAt)
                {
                    settings.DailyAtUtc = TimeOnly.ParseExact(dailyAt.Trim(), "HH:mm", CultureInfo.InvariantCulture);
                }

                settings.RestoreDelayMinutes = backups.GetValue("RestoreDelayMinutes", settings.RestoreDelayMinutes);
                settings.StaleAfterMinutes = backups.GetValue("StaleAfterMinutes", settings.StaleAfterMinutes);
                settings.DownloadLinkMinutes = backups.GetValue("DownloadLinkMinutes", settings.DownloadLinkMinutes);
            },
            options =>
            {
                options.RunWorkers = backups.GetValue("RunWorkers", true);
                backups.GetSection("Storage").Bind(options.Storage);
            });
        builder.Services.AddScoped<IAdminTwoFactor, AdminTwoFactorFromIdentity>();
        return builder;
    }
}
