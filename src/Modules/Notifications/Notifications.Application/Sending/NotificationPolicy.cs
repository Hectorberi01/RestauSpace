using Notifications.Application.Abstractions;
using Notifications.Domain;

namespace Notifications.Application.Sending;

/// <summary>Ce qu'on veut envoyer, avant toute décision.</summary>
/// <param name="TenantId">Null pour un envoi de la plateforme elle-même.</param>
/// <param name="IdempotencyKey">Une seule notification par clé (événement d'intégration, appel rejoué).</param>
/// <param name="Anonymous">Déclenchée par un visiteur du site (pas par le personnel) : SMS et WhatsApp restreints.</param>
public sealed record NotificationIntent(
    TenantId? TenantId,
    NotificationTypeDefinition Type,
    NotificationRecipient Recipient,
    RecipientId? RecipientId,
    LocationReference? Location,
    NotificationLanguage Language,
    NotificationPayload Payload,
    DateTimeOffset? ScheduledAtUtc = null,
    string? IdempotencyKey = null,
    bool Anonymous = false);

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

    /// <summary>Envoi anonyme vers un numéro d'un autre pays que celui du restaurant.</summary>
    PhoneCountryNotAllowed = 12,

    /// <summary>Plafond horaire des SMS/WhatsApp anonymes du restaurant atteint.</summary>
    AnonymousPhoneLimitReached = 13,

    /// <summary>Restaurant de démonstration : rien n'est envoyé aux visiteurs (sauf le code de connexion par e-mail).</summary>
    DemoTenant = 14,
}

/// <param name="Notification">À ajouter et sauvegarder par l'appelant ; null si l'envoi est écarté.</param>
public sealed record NotificationDecision(Notification? Notification, NotificationSkipReason? Skipped)
{
    public static NotificationDecision Skip(NotificationSkipReason reason) => new(null, reason);
}

/// <summary>
/// La politique d'envoi (§ 18) : un événement ou une demande ne crée une notification QUE si tout est réuni, dans cet
/// ordre : pas déjà créée (idempotence), SMS/WhatsApp anonyme permis (pays, plafond horaire), préférences et consentement (transactionnel n'implique jamais marketing ; un
/// message de sécurité passe toujours), template disponible (restaurant puis plateforme, puis langue par défaut), rendu
/// complet, prestataire disponible, quota du mois. Un refus n'est pas une erreur : la raison est renvoyée. Le quota ne
/// vaut que pour le compte de la PLATEFORME : au-delà, un compte du restaurant est pris s'il y en a un.
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
    IMarketingHistory marketingHistory,
    INotificationLocations locations,
    IDemoTenants? demo = null)
{
    public static readonly TimeSpan AnonymousWindow = TimeSpan.FromHours(1);

    private static readonly NotificationChannel[] PhoneChannels = [NotificationChannel.Sms, NotificationChannel.WhatsApp];

    /// <summary>Types qu'une action anonyme peut déclencher : comptés dans le plafond (y compris ceux envoyés par le personnel).</summary>
    public static readonly IReadOnlyList<TemplateKey> AnonymousTypes =
    [
        NotificationTypes.SecurityOtp.Key, NotificationTypes.OrderConfirmed.Key, NotificationTypes.OrderReady.Key,
        NotificationTypes.OrderCancelled.Key, NotificationTypes.PaymentFailed.Key, NotificationTypes.ReservationConfirmed.Key,
        NotificationTypes.ReservationReminder.Key, NotificationTypes.ReservationCancelled.Key,
        NotificationTypes.ReservationDepositRequest.Key, NotificationTypes.WaitlistOffer.Key,
    ];

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

        // Démo : seulement l'in-app de l'équipe et le code de connexion par e-mail.
        if (demo is not null && tenant is { } demoTenant && demo.IsDemo(demoTenant)
            && channel != NotificationChannel.InApp
            && !(channel == NotificationChannel.Email && type.Key == NotificationTypes.SecurityOtp.Key))
        {
            return NotificationDecision.Skip(NotificationSkipReason.DemoTenant);
        }

        if (await AnonymousPhoneRefusalAsync(intent, cancellationToken) is { } anonymousRefusal)
        {
            return NotificationDecision.Skip(anonymousRefusal);
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

        var routes = await routing.CandidatesAsync(tenant, channel, intent.Location, content.Body.Length, cancellationToken);
        var route = routes.FirstOrDefault();
        if (route is null)
        {
            return NotificationDecision.Skip(NotificationSkipReason.NoProvider);
        }

        if (tenant is { } quotaTenant && channel != NotificationChannel.InApp && !type.IsEssential && IsPlatformAccount(route)
            && await entitlements.MonthlyLimitAsync(quotaTenant, channel, cancellationToken) is { } limit
            && await usage.CountAsync(quotaTenant, channel, clock.UtcNow, cancellationToken) >= limit)
        {
            route = routes.FirstOrDefault(candidate => !IsPlatformAccount(candidate));
            if (route is null)
            {
                return NotificationDecision.Skip(NotificationSkipReason.QuotaExceeded);
            }
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

    /// <summary>Envoi par le compte de la plateforme (in-app compris) : le seul compté et limité par les quotas.</summary>
    public static bool IsPlatformAccount(NotificationRoute route) =>
        route.Configuration is not { Mode: NotificationProviderMode.TenantManaged };

    /// <summary>Délai avant qu'un envoi anonyme sorte du plafond horaire du restaurant.</summary>
    public async Task<TimeSpan> AnonymousRetryAfterAsync(TenantId tenant, NotificationTypeDefinition type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(type);
        var now = clock.UtcNow;
        var window = await CountAnonymousAsync(tenant, type, now, cancellationToken);
        var wait = (window.OldestAtUtc ?? now) + AnonymousWindow - now;
        return wait > TimeSpan.FromMinutes(1) ? wait : TimeSpan.FromMinutes(1);
    }

    /// <summary>SMS/WhatsApp anonyme : numéro du pays de l'établissement (sinon des établissements du restaurant), plafond horaire.</summary>
    private async Task<NotificationSkipReason?> AnonymousPhoneRefusalAsync(NotificationIntent intent, CancellationToken cancellationToken)
    {
        if (!intent.Anonymous || intent.TenantId is not { } tenant || !PhoneChannels.Contains(intent.Recipient.Channel))
        {
            return null;
        }

        IReadOnlyList<CountryCode> countries = intent.Location is { } location
            ? [location.Country]
            : await locations.CountriesAsync(tenant, cancellationToken);
        if (!countries.Any(country => PhoneCountries.IsFrom(intent.Recipient.Address, country)))
        {
            return NotificationSkipReason.PhoneCountryNotAllowed;
        }

        var limit = settings.AnonymousSmsPerTenantPerHour;
        return limit > 0 && (await CountAnonymousAsync(tenant, intent.Type, clock.UtcNow, cancellationToken)).Count >= limit
            ? NotificationSkipReason.AnonymousPhoneLimitReached
            : null;
    }

    private Task<NotificationWindowCount> CountAnonymousAsync(
        TenantId tenant, NotificationTypeDefinition type, DateTimeOffset now, CancellationToken cancellationToken) =>
        notifications.CountCreatedSinceAsync(
            tenant, PhoneChannels, AnonymousTypes.Contains(type.Key) ? AnonymousTypes : [.. AnonymousTypes, type.Key], now - AnonymousWindow,
            cancellationToken);

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
