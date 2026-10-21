using Identity.Infrastructure.Notifications;
using SaasRestaurant.BuildingBlocks.Application;

namespace SaasRestaurant.Host;

/// <summary>Destinataire et lien des alertes de demandes de démo (section <c>Sales</c> de la configuration).</summary>
internal sealed record DemoRequestAlertSettings(string? NotificationEmail, string AdminUrl);

/// <summary>
/// tenants.DemoRequestSubmitted -> un email à l'équipe commerciale. Le message ne contient AUCUNE donnée du prospect
/// (ni nom, ni email, ni téléphone, ni message) : seulement le lien vers le back-office, où la demande se consulte
/// après connexion. Sans destinataire configuré, rien n'est envoyé. Une erreur d'envoi est retentée par l'outbox.
/// </summary>
internal sealed class DemoRequestAlertFlow(IEmailSender sender, DemoRequestAlertSettings settings, ILogger<DemoRequestAlertFlow> logger)
    : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not ("tenants", "DemoRequestSubmitted"))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(settings.NotificationEmail))
        {
            logger.LogInformation("Demo request received ({MessageId}); no Sales:DemoRequestNotificationEmail configured.", message.Id);
            return;
        }

        var link = $"{settings.AdminUrl.TrimEnd('/')}/admin/demo-requests";
        await sender.SendAsync(
            new EmailMessage(
                settings.NotificationEmail,
                "Nouvelle demande de démonstration RestauSpace",
                $"Une nouvelle demande de démonstration a été envoyée depuis le site.\n\nConsultez-la dans le back-office : {link}\n"),
            cancellationToken);
        logger.LogInformation("Sales team notified of demo request message {MessageId}.", message.Id);
    }
}
