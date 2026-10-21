using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Application.Queries;

// Réponses de l'API. SÉCURITÉ : aucune ne contient d'identifiant de compte (clé, mot de passe, jeton, secret), ni la
// référence du coffre, ni le contenu rendu d'une notification, ni le message brut d'un prestataire ; une adresse est
// toujours MASQUÉE (« a***@example.com », « +229******00 »).

public sealed record ProviderCapabilityDto(
    NotificationChannel Channel, IReadOnlyList<NotificationFeature> Features, IReadOnlyList<string> Countries, int? MaxContentLength)
{
    public static ProviderCapabilityDto From(NotificationProviderCapability capability) => new(
        capability.Channel, capability.Features, capability.SupportedCountries.Select(country => country.Value).ToList(), capability.MaxContentLength);
}

public sealed record NotificationProviderDto(
    Guid Id,
    string Code,
    string Name,
    NotificationChannel Channel,
    NotificationProviderConfigurationType ConfigurationType,
    ProviderCapabilityDto Capability,
    bool IsEnabled,
    bool HasIntegration,
    IReadOnlyList<string>? CredentialFields = null,
    bool? HasPlatformCredentials = null)
{
    /// <param name="credentialFields">Les NOMS des champs que le restaurant saisit en mode restaurant, jamais des valeurs.</param>
    public static NotificationProviderDto From(NotificationProvider provider, bool hasIntegration, IReadOnlyList<string>? credentialFields = null) => new(
        provider.Id.Value, provider.Code.Value, provider.Name, provider.Channel, provider.ConfigurationType,
        ProviderCapabilityDto.From(provider.Capability), provider.IsEnabled, hasIntegration, credentialFields ?? []);
}

/// <param name="CredentialFields">Les NOMS des champs à saisir, jamais des valeurs.</param>
public sealed record NotificationIntegrationDto(string Provider, IReadOnlyList<NotificationChannel> Channels, IReadOnlyList<string> CredentialFields)
{
    public static NotificationIntegrationDto From(NotificationIntegration integration) =>
        new(integration.Provider.Value, integration.Channels, integration.CredentialFields);
}

public sealed record ChannelAssignmentDto(Guid LocationId, int Priority, bool IsEnabled);

/// <param name="HasCredentials">Des identifiants sont rangés au coffre (lesquels : jamais renvoyé).</param>
public sealed record NotificationConfigurationDto(
    Guid Id,
    Guid ProviderId,
    string Provider,
    NotificationChannel Channel,
    NotificationProviderMode Mode,
    NotificationConfigurationStatus Status,
    bool IsEnabled,
    bool HasCredentials,
    bool IsGlobal,
    IReadOnlyList<ChannelAssignmentDto> Assignments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static NotificationConfigurationDto From(RestaurantNotificationConfiguration configuration) => new(
        configuration.Id.Value,
        configuration.NotificationProviderId.Value,
        configuration.Provider.Value,
        configuration.Channel,
        configuration.Mode,
        configuration.Status,
        configuration.IsEnabled,
        configuration.SecretReference is not null,
        configuration.IsGlobal,
        configuration.Assignments
            .Select(assignment => new ChannelAssignmentDto(assignment.LocationId.Value, assignment.Priority, assignment.IsEnabled))
            .ToList(),
        configuration.CreatedAtUtc,
        configuration.UpdatedAtUtc);
}

/// <param name="IsPlatform">Template de la plateforme : lu par un restaurant (repli), jamais modifiable par lui.</param>
public sealed record NotificationTemplateDto(
    Guid Id,
    bool IsPlatform,
    string Key,
    NotificationChannel Channel,
    string Language,
    string? Subject,
    string Body,
    int Version,
    TemplateStatus Status,
    bool IsEnabled,
    IReadOnlyList<string> Variables,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? PublishedAtUtc)
{
    public static NotificationTemplateDto From(NotificationTemplate template) => new(
        template.Id.Value, template.IsPlatformLevel, template.Key.Value, template.Channel, template.Language.Value, template.Subject,
        template.Body, template.Version, template.Status, template.IsEnabled, template.Variables, template.UpdatedAtUtc, template.PublishedAtUtc);
}

/// <param name="FailureCode">Code de l'échec (jamais le message du prestataire).</param>
public sealed record NotificationDeliveryDto(
    Guid Id, int AttemptNumber, string Provider, NotificationDeliveryStatus Status, DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc, DateTimeOffset? DeliveredAtUtc, string? FailureCode, NotificationFailureKind? FailureKind,
    DateTimeOffset? NextRetryAtUtc);

public sealed record NotificationDto(
    Guid Id,
    string Type,
    NotificationPurpose Purpose,
    NotificationPriority Priority,
    NotificationChannel Channel,
    string Provider,
    NotificationStatus Status,
    string Recipient,
    Guid? CustomerId,
    Guid? LocationId,
    string Language,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ScheduledAtUtc,
    DateTimeOffset? SentAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    DateTimeOffset? CancelledAtUtc,
    bool IsRedacted,
    bool IsDeliveryAbandoned,
    string? LastFailureCode,
    IReadOnlyList<NotificationDeliveryDto> Deliveries)
{
    public static NotificationDto From(Notification notification) => new(
        notification.Id.Value,
        notification.TemplateKey.Value,
        notification.Purpose,
        notification.Priority,
        notification.Channel,
        notification.ProviderCode.Value,
        notification.Status,
        Masking.Mask(notification.Recipient),
        notification.CustomerId?.Value,
        notification.LocationId?.Value,
        notification.Language.Value,
        notification.CreatedAtUtc,
        notification.ScheduledAtUtc,
        notification.SentAtUtc,
        notification.DeliveredAtUtc,
        notification.CancelledAtUtc,
        notification.IsRedacted,
        notification.IsDeliveryAbandoned,
        notification.LastFailure?.Code,
        notification.Deliveries
            .Select(delivery => new NotificationDeliveryDto(
                delivery.Id.Value, delivery.AttemptNumber, delivery.ProviderCode.Value, delivery.Status, delivery.StartedAtUtc,
                delivery.CompletedAtUtc, delivery.DeliveredAtUtc, delivery.FailureCode, delivery.Failure?.Kind, delivery.NextRetryAtUtc))
            .ToList());
}

/// <summary>Une notification de la boîte in-app de l'utilisateur : SA notification, contenu compris.</summary>
public sealed record InAppNotificationDto(
    Guid Id, string Type, NotificationPriority Priority, string? Title, string Body, InAppState State, DateTimeOffset CreatedAtUtc, DateTimeOffset? ReadAtUtc)
{
    public static InAppNotificationDto From(Notification notification) => new(
        notification.Id.Value, notification.TemplateKey.Value, notification.Priority, notification.Content.Subject, notification.Content.Body,
        notification.InAppState ?? InAppState.Unread, notification.CreatedAtUtc, notification.ReadAtUtc);
}

public sealed record PreferenceChoiceDto(NotificationPurpose Purpose, NotificationChannel Channel, bool IsAllowed, DateTimeOffset? ChangedAtUtc, string? Source);

public sealed record AlertTypeChoiceDto(string Type, bool IsEnabled);

/// <summary>
/// Une ligne par finalité et canal : le choix enregistré, sinon le défaut. Pour un utilisateur, en plus, chaque type
/// d'alerte d'équipe et s'il le reçoit.
/// </summary>
public sealed record NotificationPreferencesDto(string Owner, IReadOnlyList<PreferenceChoiceDto> Choices, IReadOnlyList<AlertTypeChoiceDto> Alerts)
{
    public static NotificationPreferencesDto From(RecipientId owner, NotificationPreference? preference) => new(
        owner.Kind.ToString(),
        Enum.GetValues<NotificationPurpose>()
            .SelectMany(purpose => NotificationChannels.All.Select(channel => (purpose, channel)))
            .Where(pair => pair.purpose == NotificationPurpose.Transactional || owner.IsCustomer)
            .Select(pair =>
            {
                var choice = preference?.Choices.SingleOrDefault(c => c.Purpose == pair.purpose && c.Channel == pair.channel);
                return new PreferenceChoiceDto(
                    pair.purpose,
                    pair.channel,
                    preference?.Allows(pair.purpose, pair.channel) ?? NotificationPreference.AllowedByDefault(owner, pair.purpose),
                    choice?.ChangedAtUtc,
                    choice?.Source.Value);
            })
            .ToList(),
        owner.IsCustomer
            ? []
            : NotificationTypes.All
                .Where(NotificationPreference.IsStaffAlert)
                .OrderBy(type => type.Key.Value, StringComparer.Ordinal)
                .Select(type => new AlertTypeChoiceDto(type.Key.Value, preference?.IsMuted(type.Key) != true))
                .ToList());
}

/// <summary>Une notification en dead letter, vue de l'administration de la plateforme : ids et codes uniquement.</summary>
/// <summary>Une ligne du journal d'envoi de la plateforme : jamais le destinataire ni le contenu.</summary>
public sealed record NotificationLogDto(
    Guid Id, Guid? TenantId, string Type, NotificationChannel Channel, string Provider, NotificationStatus Status, int AttemptCount,
    string? LastFailureCode, DateTimeOffset CreatedAtUtc, DateTimeOffset? ScheduledAtUtc, DateTimeOffset? SentAtUtc, DateTimeOffset? DeliveredAtUtc)
{
    public static NotificationLogDto From(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return new(
            notification.Id.Value, notification.TenantId?.Value, notification.TemplateKey.Value, notification.Channel, notification.ProviderCode.Value,
            notification.Status, notification.AttemptCount, notification.LastFailure?.Code, notification.CreatedAtUtc, notification.ScheduledAtUtc,
            notification.SentAtUtc, notification.DeliveredAtUtc);
    }
}

/// <param name="HandedOver">Acceptées par le prestataire (envoyées, remises en partie ou remises).</param>
/// <param name="Delivered">Remise confirmée par le prestataire (tous ne la confirment pas : SMTP, par exemple).</param>
/// <param name="Failed">En échec (relance possible ou abandonnée).</param>
/// <param name="InProgress">À envoyer, programmées ou en cours d'envoi.</param>
/// <param name="AverageSendSeconds">Délai moyen entre la création (ou l'échéance) et l'acceptation ; null sans envoi.</param>
public sealed record NotificationsOverviewDto(
    DateTimeOffset SinceUtc, int Created, int HandedOver, int Delivered, int Failed, int InProgress, int Cancelled, double? AverageSendSeconds,
    IReadOnlyList<NotificationChannelOverviewDto> Channels);

public sealed record NotificationChannelOverviewDto(NotificationChannel Channel, int Created, int HandedOver, int Failed);

public sealed record DeadLetterDto(
    Guid Id, Guid? TenantId, string Type, NotificationChannel Channel, string Provider, int AttemptCount, string? LastFailureCode,
    NotificationFailureKind? LastFailureKind, DateTimeOffset CreatedAtUtc)
{
    public static DeadLetterDto From(Notification notification) => new(
        notification.Id.Value, notification.TenantId?.Value, notification.TemplateKey.Value, notification.Channel, notification.ProviderCode.Value,
        notification.AttemptCount, notification.LastFailure?.Code, notification.LastFailure?.Kind, notification.CreatedAtUtc);
}

/// <summary>Adresses masquées : assez pour reconnaître un destinataire, pas pour le contacter.</summary>
public static class Masking
{
    public static string Mask(NotificationRecipient recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        var address = recipient.Address;
        if (recipient.IsRedacted)
        {
            return "[redacted]";
        }

        switch (recipient.Channel)
        {
            case NotificationChannel.Email:
                var at = address.IndexOf('@', StringComparison.Ordinal);
                return at <= 0 ? "***" : address[..1] + "***" + address[at..];
            case NotificationChannel.Sms or NotificationChannel.WhatsApp:
                return address.Length <= 6 ? "***" : address[..4] + new string('*', address.Length - 6) + address[^2..];
            case NotificationChannel.InApp:
                return "user:" + address[..8];
            default:
                return "device:" + (address.Length <= 6 ? "***" : address[..6] + "***");
        }
    }
}

/// <summary>Un type du catalogue : clé, nature, priorité, destinataire et variables autorisées (dans l'ordre du catalogue).</summary>
public sealed record NotificationTypeDto(
    string Key, NotificationPurpose Purpose, NotificationPriority Priority, bool IsEssential, string Audience, IReadOnlyList<string> Variables)
{
    public static NotificationTypeDto From(NotificationTypeDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new NotificationTypeDto(
            definition.Key.Value, definition.Purpose, definition.Priority, definition.IsEssential, definition.Audience, definition.OrderedVariables);
    }
}
