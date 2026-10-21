using Notifications.Application.Abstractions;
using Notifications.Domain;

namespace Notifications.Application.Sending;

/// <summary>Ce qu'on veut envoyer, avant toute décision.</summary>
/// <param name="TenantId">Null pour un envoi de la plateforme elle-même.</param>
/// <param name="IdempotencyKey">Une seule notification par clé (événement d'intégration, appel rejoué).</param>
public sealed record NotificationIntent(
    TenantId? TenantId,
    NotificationTypeDefinition Type,
    NotificationRecipient Recipient,
    RecipientId? RecipientId,
    LocationReference? Location,
    NotificationLanguage Language,
    NotificationPayload Payload,
    DateTimeOffset? ScheduledAtUtc = null,
    string? IdempotencyKey = null);

/// <summary>Pourquoi rien n'est envoyé. Valeurs explicites et définitives (renvoyées par l'API).</summary>
public enum NotificationSkipReason
{
    OptedOut = 1,
    NotACustomer = 2,
    ConsentRequired = 3,
    ConsentNotGranted = 4,
    NoProvider = 5,
    NoTemplate = 6,
    TemplateIncomplete = 7,
    QuotaExceeded = 8,
    Duplicate = 9,
    NoContact = 10,
    FrequencyLimited = 11,
}

/// <param name="Notification">À ajouter et sauvegarder par l'appelant ; null si l'envoi est écarté.</param>
public sealed record NotificationDecision(Notification? Notification, NotificationSkipReason? Skipped)
{
    public static NotificationDecision Skip(NotificationSkipReason reason) => new(null, reason);
}

/// <summary>
/// La politique d'envoi (§ 18) : un événement ou une demande ne crée une notification QUE si tout est réuni, dans cet
/// ordre : pas déjà créée (idempotence), préférences et consentement (transactionnel n'implique jamais marketing ; un
/// message de sécurité passe toujours), template disponible (restaurant puis plateforme, puis langue par défaut), rendu
/// complet, prestataire disponible, quota du mois. Un refus n'est pas une erreur : la raison est renvoyée.
/// </summary>
public sealed class NotificationPolicy(
    INotificationRepository notifications,
    INotificationPreferenceRepository preferences,
    INotificationTemplateRepository templates,
    INotificationConsents consents,
    INotificationUsage usage,
    INotificationEntitlementChecker entitlements,
    NotificationRouting routing,
    NotificationsSettings settings,
    IClock clock,
    IMarketingRulesRepository marketingRules,
    IMarketingHistory marketingHistory)
{
    public async Task<NotificationDecision> DecideAsync(NotificationIntent intent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        var tenant = intent.TenantId;
        var channel = intent.Recipient.Channel;
        var type = intent.Type;

        if (intent.IdempotencyKey is { } key && await notifications.ExistsAsync(tenant, key, cancellationToken))
        {
            return NotificationDecision.Skip(NotificationSkipReason.Duplicate);
        }

        var consent = await ConsentAsync(intent, cancellationToken);
        if (await RefusalAsync(intent, consent, cancellationToken) is { } refusal)
        {
            return NotificationDecision.Skip(refusal);
        }

        if (await TooSoonAsync(intent, cancellationToken))
        {
            return NotificationDecision.Skip(NotificationSkipReason.FrequencyLimited);
        }

        var template = await TemplateAsync(tenant, type.Key, channel, intent.Language, cancellationToken);
        if (template is null)
        {
            return NotificationDecision.Skip(NotificationSkipReason.NoTemplate);
        }

        NotificationContent content;
        try
        {
            content = TemplateRenderer.Render(template, intent.Payload);
        }
        catch (DomainException exception) when (exception.Code == TemplateErrors.VariableMissing.Code)
        {
            return NotificationDecision.Skip(NotificationSkipReason.TemplateIncomplete);
        }

        var route = (await routing.CandidatesAsync(tenant, channel, intent.Location, content.Body.Length, cancellationToken)).FirstOrDefault();
        if (route is null)
        {
            return NotificationDecision.Skip(NotificationSkipReason.NoProvider);
        }

        if (tenant is { } quotaTenant && channel != NotificationChannel.InApp && !type.IsEssential
            && await entitlements.MonthlyLimitAsync(quotaTenant, channel, cancellationToken) is { } limit
            && await usage.CountAsync(quotaTenant, channel, clock.UtcNow, cancellationToken) >= limit)
        {
            return NotificationDecision.Skip(NotificationSkipReason.QuotaExceeded);
        }

        var notification = tenant is { } owner
            ? Notification.Create(
                owner, type, intent.Recipient, intent.RecipientId, route.Provider, template.Language, content, clock,
                intent.Location, consent, intent.ScheduledAtUtc, template.Id)
            : Notification.CreateForPlatform(
                type, intent.Recipient, intent.RecipientId, route.Provider, template.Language, content, clock,
                intent.ScheduledAtUtc, template.Id);
        return new NotificationDecision(notification, null);
    }

    /// <summary>Règle de fréquence du restaurant : un message marketing au plus par intervalle et par client.</summary>
    private async Task<bool> TooSoonAsync(NotificationIntent intent, CancellationToken cancellationToken)
    {
        if (!intent.Type.IsMarketing || intent.TenantId is not { } tenant || intent.RecipientId is not { } recipient)
        {
            return false;
        }

        var days = (await marketingRules.FindAsync(tenant, cancellationToken))?.MinimumIntervalDays ?? MarketingRules.DefaultIntervalDays;
        return days > 0 && await marketingHistory.ReceivedSinceAsync(tenant, recipient, clock.UtcNow.AddDays(-days), cancellationToken);
    }

    /// <summary>Le consentement marketing d'un client (canal externe) ; null sinon.</summary>
    private async Task<ConsentProof?> ConsentAsync(NotificationIntent intent, CancellationToken cancellationToken) =>
        intent.Type.IsMarketing
        && intent.TenantId is { } tenant
        && intent.RecipientId?.ToCustomerId() is { } customer
        && intent.Recipient.Channel.ToConsentChannel() is { } consentChannel
            ? await consents.FindAsync(tenant, customer, consentChannel, cancellationToken)
            : null;

    private async Task<NotificationSkipReason?> RefusalAsync(NotificationIntent intent, ConsentProof? consent, CancellationToken cancellationToken)
    {
        if (intent.RecipientId is not { } recipient)
        {
            // Un invité sans fiche : jamais de marketing (il faut un client et son consentement).
            return intent.Type.IsMarketing ? NotificationSkipReason.NotACustomer : null;
        }

        var choices = intent.TenantId is { } tenant ? await preferences.FindAsync(tenant, recipient, cancellationToken) : null;
        return NotificationPermission.Decide(intent.Type, intent.Recipient.Channel, recipient, choices, consent).Refusal switch
        {
            PermissionRefusal.None => null,
            PermissionRefusal.OptedOut => NotificationSkipReason.OptedOut,
            PermissionRefusal.NotACustomer => NotificationSkipReason.NotACustomer,
            PermissionRefusal.ConsentRequired => NotificationSkipReason.ConsentRequired,
            _ => NotificationSkipReason.ConsentNotGranted,
        };
    }

    /// <summary>Template du restaurant, sinon de la plateforme, dans la langue demandée puis dans la langue par défaut.</summary>
    private async Task<NotificationTemplate?> TemplateAsync(
        TenantId? tenant, TemplateKey key, NotificationChannel channel, NotificationLanguage language, CancellationToken cancellationToken)
    {
        var resolution = NotificationTemplateResolution.Resolve(
            tenant, key, channel, language, await templates.ListCandidatesAsync(tenant, key, channel, language, cancellationToken));
        if (resolution.IsFound)
        {
            return resolution.Template;
        }

        var fallback = NotificationLanguage.Create(settings.DefaultLanguage);
        return fallback == language
            ? null
            : NotificationTemplateResolution.Resolve(
                tenant, key, channel, fallback, await templates.ListCandidatesAsync(tenant, key, channel, fallback, cancellationToken)).Template;
    }
}
