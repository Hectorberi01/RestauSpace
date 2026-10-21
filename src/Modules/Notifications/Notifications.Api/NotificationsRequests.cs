using Notifications.Application.Commands;
using Notifications.Domain;

namespace Notifications.Api;

public sealed record CreatedResponse(Guid Id);

public sealed record CreateProviderRequest(
    string? Code, string? Name, NotificationChannel Channel, NotificationProviderConfigurationType ConfigurationType, ProviderCapabilityInput? Capability);

public sealed record UpdateProviderRequest(string? Name, NotificationProviderConfigurationType ConfigurationType, ProviderCapabilityInput? Capability);

/// <param name="Credentials">Tous les champs demandés par l'intégration (ex. { "api_key": "...", "webhook_secret": "..." }). Jamais renvoyés.</param>
public sealed record CredentialsRequest(IReadOnlyDictionary<string, string>? Credentials);

/// <param name="Mode">Facultatif : <c>TenantManaged</c> (compte du restaurant) par défaut.</param>
public sealed record TestConfigurationRequest(string? Address);

public sealed record CreateConfigurationRequest(NotificationProviderMode? Mode = null);

public sealed record ModeRequest(NotificationProviderMode Mode);

public sealed record AssignLocationRequest(Guid LocationId, int Priority = 0, bool IsEnabled = true);

public sealed record CreateTemplateRequest(string? Key, NotificationChannel Channel, string? Language, string? Subject, string? Body);

public sealed record UpdateTemplateRequest(string? Subject, string? Body);

/// <param name="Type">Un type du catalogue (<c>order.ready</c>...).</param>
/// <param name="CustomerId">Un client du restaurant (son adresse est lue dans sa fiche).</param>
/// <param name="UserId">Un utilisateur (in-app, ou push vers ses appareils).</param>
/// <param name="Address">Une adresse saisie (invité sans fiche) : transactionnel seulement.</param>
public sealed record SendRequest(
    string? Type,
    NotificationChannel Channel,
    Guid? CustomerId = null,
    Guid? UserId = null,
    string? Address = null,
    Guid? LocationId = null,
    string? Language = null,
    IReadOnlyDictionary<string, string>? Variables = null,
    DateTimeOffset? ScheduledAtUtc = null,
    string? IdempotencyKey = null);

public sealed record AlertTypeRequest(bool IsEnabled, string? Source = null);

public sealed record PreferenceRequest(NotificationPurpose Purpose, NotificationChannel Channel, bool IsAllowed, string? Source = null);

public sealed record DeviceRequest(string? DeviceId, DevicePlatform Platform, string? PushToken);

public sealed record OtpRequest(string? Purpose, NotificationChannel Channel, string? Address, string? Language = null);

public sealed record OtpVerifyRequest(string? Code);

/// <summary>Le jeton du lien de désinscription, tel que reçu dans l'e-mail.</summary>
public sealed record UnsubscribeRequest(string? Token);

/// <param name="Type">Un type marketing du catalogue (<c>marketing.promotional_campaign</c>...).</param>
/// <param name="Tag">Seuls les clients portant ce tag ; vide : tous ceux qui ont consenti.</param>
/// <param name="OnlyLocationCustomers">Seulement les clients de l'établissement <paramref name="LocationId"/>.</param>
/// <param name="Variables">Variables communes du message (titre, lien, date) ; jamais le prénom ni le lien de désinscription.</param>
public sealed record CampaignRequest(
    string? Name,
    string? Type,
    NotificationChannel Channel,
    string? Tag = null,
    Guid? LocationId = null,
    IReadOnlyDictionary<string, string>? Variables = null,
    bool OnlyLocationCustomers = false);

/// <param name="At">Départ ; vide : dès que possible.</param>
public sealed record ScheduleCampaignRequest(DateTimeOffset? At = null);

public sealed record MarketingRulesRequest(int MinimumIntervalDays);
