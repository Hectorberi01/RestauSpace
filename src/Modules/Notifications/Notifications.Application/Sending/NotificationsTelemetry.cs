using System.Diagnostics;
using System.Diagnostics.Metrics;
using Notifications.Domain;

namespace Notifications.Application.Sending;

/// <summary>
/// Traces et métriques du module (§ 41), avec les seules API de .NET (aucune dépendance) : l'hôte les exporte par
/// OpenTelemetry s'il le souhaite. Chaque envoi est une activité portant TenantId, NotificationId, DeliveryId,
/// Provider, Channel et NotificationType ; le CorrelationId est l'id de trace. JAMAIS d'adresse, de contenu, d'OTP ni
/// d'identifiant de compte. Les métriques n'ont pour étiquettes que provider, channel et status (pas le tenant : la
/// cardinalité exploserait).
/// </summary>
public static class NotificationsTelemetry
{
    public const string Name = "SaasRestaurant.Notifications";

    public static readonly ActivitySource Source = new(Name);

    private static readonly Meter Meter = new(Name);
    private static readonly Counter<long> Sent = Meter.CreateCounter<long>("notifications.sent");
    private static readonly Counter<long> Failed = Meter.CreateCounter<long>("notifications.failed");
    private static readonly Counter<long> Delivered = Meter.CreateCounter<long>("notifications.delivered");
    private static readonly Counter<long> Retry = Meter.CreateCounter<long>("notifications.retry");
    private static readonly Counter<long> ProviderErrors = Meter.CreateCounter<long>("notifications.provider.errors");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("notifications.duration", "ms");

    public static Activity? StartSend(Notification notification, NotificationDeliveryId deliveryId)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var activity = Source.StartActivity("notifications.send", ActivityKind.Client);
        activity?.SetTag("tenant.id", notification.TenantId?.Value.ToString());
        activity?.SetTag("notification.id", notification.Id.Value.ToString());
        activity?.SetTag("notification.delivery_id", deliveryId.Value.ToString());
        activity?.SetTag("notification.provider", notification.ProviderCode.Value);
        activity?.SetTag("notification.channel", notification.Channel.ToString());
        activity?.SetTag("notification.type", notification.TemplateKey.Value);
        return activity;
    }

    public static void RecordOutcome(NotificationProviderCode provider, NotificationChannel channel, string status, TimeSpan elapsed)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var tags = new TagList { { "provider", provider.Value }, { "channel", channel.ToString() }, { "status", status } };
        Duration.Record(elapsed.TotalMilliseconds, tags);
        switch (status)
        {
            case "sent":
                Sent.Add(1, tags);
                break;
            case "delivered":
                Sent.Add(1, tags);
                Delivered.Add(1, tags);
                break;
            default:
                Failed.Add(1, tags);
                break;
        }
    }

    public static void RecordDelivered(NotificationProviderCode provider, NotificationChannel channel) =>
        Delivered.Add(1, new TagList { { "provider", provider.Value }, { "channel", channel.ToString() }, { "status", "delivered" } });

    public static void RecordRetry(NotificationProviderCode provider, NotificationChannel channel) =>
        Retry.Add(1, new TagList { { "provider", provider.Value }, { "channel", channel.ToString() }, { "status", "retry" } });

    public static void RecordProviderError(NotificationProviderCode provider, NotificationChannel channel) =>
        ProviderErrors.Add(1, new TagList { { "provider", provider.Value }, { "channel", channel.ToString() }, { "status", "error" } });
}
