using Customers.Domain;

namespace Customers.Application.Queries;

/// <summary>Une fiche client. Après anonymisation : nom neutre, ni contact, ni tags, ni préférences.</summary>
public sealed record CustomerDto(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    CustomerStatus Status,
    bool IsAnonymized,
    ContactRequirement ContactRequirement,
    IReadOnlyList<string> Tags,
    PreferencesDto? Preferences,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AnonymizedAtUtc,
    Guid? MergedIntoId = null)
{
    public static CustomerDto From(Customer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        return new CustomerDto(
            customer.Id.Value,
            customer.Name,
            customer.Email?.Value,
            customer.Phone?.Value,
            customer.Status,
            customer.IsAnonymized,
            customer.ContactPolicy.Requirement,
            customer.Tags.Select(tag => tag.Value).Order(StringComparer.Ordinal).ToList(),
            customer.Preferences is { } preferences
                ? new PreferencesDto(preferences.Language, preferences.MarketingChannels.ToList())
                : null,
            customer.CreatedAtUtc,
            customer.AnonymizedAtUtc,
            customer.MergedIntoId?.Value);
    }
}

/// <param name="MarketingChannels">Canaux SOUHAITÉS : n'autorisent aucun envoi sans consentement accordé.</param>
public sealed record PreferencesDto(string Language, IReadOnlyList<CommunicationChannel> MarketingChannels);

/// <summary>La preuve de consentement d'un client sur un canal, avec tout son historique (du plus ancien au plus récent).</summary>
public sealed record ConsentDto(
    Guid Id,
    CommunicationChannel Channel,
    ConsentStatus Status,
    bool IsGranted,
    DateTimeOffset GrantedAtUtc,
    string Source,
    DateTimeOffset? WithdrawnAtUtc,
    string? WithdrawalSource,
    WithdrawalReason? WithdrawalReason,
    IReadOnlyList<ConsentChangeDto> History)
{
    public static ConsentDto From(Consent consent)
    {
        ArgumentNullException.ThrowIfNull(consent);
        return new ConsentDto(
            consent.Id.Value,
            consent.Channel,
            consent.Status,
            consent.IsGranted,
            consent.GrantedAtUtc,
            consent.Source.Value,
            consent.WithdrawnAtUtc,
            consent.WithdrawalSource?.Value,
            consent.WithdrawalReason,
            // Tri stable par date : deux lignes du même instant gardent leur ordre d'écriture.
            consent.History
                .OrderBy(change => change.OccurredAtUtc)
                .Select(change => new ConsentChangeDto(change.Status, change.OccurredAtUtc, change.Source.Value, change.Reason))
                .ToList());
    }
}

public sealed record ConsentChangeDto(ConsentStatus Status, DateTimeOffset OccurredAtUtc, string Source, WithdrawalReason? Reason);

public sealed record ContactPolicyDto(ContactRequirement Requirement);

/// <summary>
/// Ce qu'un AUTRE module a le droit de savoir d'un client (servi par l'hôte, jamais exposé en HTTP) : son tenant, son id
/// et s'il est anonymisé. Aucun autre champ ne traverse la frontière du module.
/// </summary>
public sealed record CustomerReferenceDto(Guid TenantId, Guid CustomerId, bool IsAnonymized);

/// <summary>
/// Peut-on envoyer un message MARKETING à ce client sur ce canal ? <paramref name="Reason"/> : pourquoi pas (null si
/// oui) : <c>anonymized</c>, <c>no_consent</c>, <c>consent_withdrawn</c>, <c>no_contact</c>.
/// </summary>
public sealed record MarketingPermissionDto(Guid CustomerId, CommunicationChannel Channel, bool CanSend, string? Reason);
