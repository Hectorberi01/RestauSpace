using Customers.Application.Abstractions;
using Customers.Domain;

namespace Customers.Application.Queries;

// Lectures du tenant courant, possibles même quand le tenant est suspendu. Un client d'un autre tenant n'est jamais
// trouvé.

public sealed record GetCustomerByIdQuery(Guid CustomerId) : IQuery<CustomerDto>;

public sealed class GetCustomerByIdHandler(CustomersWorkspace workspace) : IQueryHandler<GetCustomerByIdQuery, CustomerDto>
{
    public async Task<CustomerDto> HandleAsync(GetCustomerByIdQuery query, CancellationToken cancellationToken = default) =>
        CustomerDto.From(await workspace.LoadCustomerAsync(workspace.CurrentTenant, query.CustomerId, cancellationToken));
}

/// <summary>
/// Recherche par nom (contenu, sans tenir compte de la casse), email ou téléphone EXACTS, tag ; critères cumulés, tous
/// facultatifs. Par nom puis id, page de <paramref name="Take"/> (1 à 100) après <paramref name="Skip"/>.
/// </summary>
public sealed record SearchCustomersQuery(
    string? Name = null,
    string? Email = null,
    string? Phone = null,
    string? Tag = null,
    bool IncludeAnonymized = false,
    int Skip = 0,
    int Take = 50) : IQuery<IReadOnlyList<CustomerDto>>;

public sealed class SearchCustomersHandler(CustomersWorkspace workspace) : IQueryHandler<SearchCustomersQuery, IReadOnlyList<CustomerDto>>
{
    public const int MaxTake = 100;

    public async Task<IReadOnlyList<CustomerDto>> HandleAsync(SearchCustomersQuery query, CancellationToken cancellationToken = default)
    {
        DomainException.ThrowIf(query.Skip < 0 || query.Take is < 1 or > MaxTake, CustomersApplicationErrors.PageInvalid);
        var search = new CustomerSearch(
            string.IsNullOrWhiteSpace(query.Name) ? null : query.Name.Trim(),
            CustomersWorkspace.OptionalEmail(query.Email),
            CustomersWorkspace.OptionalPhone(query.Phone),
            string.IsNullOrWhiteSpace(query.Tag) ? null : CustomerTag.Create(query.Tag),
            query.IncludeAnonymized,
            query.Skip,
            query.Take);

        return (await workspace.Customers.SearchAsync(workspace.CurrentTenant, search, cancellationToken))
            .Select(CustomerDto.From)
            .ToList();
    }
}

/// <summary>Les consentements du client, par canal.</summary>
public sealed record ListCustomerConsentsQuery(Guid CustomerId) : IQuery<IReadOnlyList<ConsentDto>>;

public sealed class ListCustomerConsentsHandler(CustomersWorkspace workspace) : IQueryHandler<ListCustomerConsentsQuery, IReadOnlyList<ConsentDto>>
{
    public async Task<IReadOnlyList<ConsentDto>> HandleAsync(ListCustomerConsentsQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var customer = await workspace.LoadCustomerAsync(tenant, query.CustomerId, cancellationToken);
        return (await workspace.Consents.ListForCustomerAsync(tenant, customer.Id, cancellationToken))
            .OrderBy(consent => consent.Channel)
            .Select(ConsentDto.From)
            .ToList();
    }
}

public sealed record GetContactPolicyQuery : IQuery<ContactPolicyDto>;

public sealed class GetContactPolicyHandler(CustomersWorkspace workspace) : IQueryHandler<GetContactPolicyQuery, ContactPolicyDto>
{
    public async Task<ContactPolicyDto> HandleAsync(GetContactPolicyQuery query, CancellationToken cancellationToken = default) =>
        new((await workspace.Settings.GetContactPolicyAsync(workspace.CurrentTenant, cancellationToken)).Requirement);
}

/// <summary>Pour l'hôte (Orders, Reservations...) : la référence d'un client du tenant du contexte.</summary>
public sealed record GetCustomerReferenceQuery(Guid CustomerId) : IQuery<CustomerReferenceDto>;

public sealed class GetCustomerReferenceHandler(CustomersWorkspace workspace) : IQueryHandler<GetCustomerReferenceQuery, CustomerReferenceDto>
{
    public async Task<CustomerReferenceDto> HandleAsync(GetCustomerReferenceQuery query, CancellationToken cancellationToken = default)
    {
        var reference = (await workspace.LoadCustomerAsync(workspace.CurrentTenant, query.CustomerId, cancellationToken)).ToReference();
        return new CustomerReferenceDto(reference.TenantId.Value, reference.CustomerId.Value, reference.IsAnonymized);
    }
}

/// <summary>
/// À vérifier AVANT tout envoi marketing (module Notifications) : seul un consentement accordé l'autorise, jamais la
/// préférence de la fiche ; il faut aussi l'adresse du canal (email pour Email, téléphone pour SMS et WhatsApp).
/// </summary>
public sealed record GetMarketingPermissionQuery(Guid CustomerId, CommunicationChannel Channel) : IQuery<MarketingPermissionDto>;

public sealed class GetMarketingPermissionHandler(CustomersWorkspace workspace)
    : IQueryHandler<GetMarketingPermissionQuery, MarketingPermissionDto>
{
    public async Task<MarketingPermissionDto> HandleAsync(GetMarketingPermissionQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var customer = await workspace.LoadCustomerAsync(tenant, query.CustomerId, cancellationToken);
        var consent = Enum.IsDefined(query.Channel)
            ? await workspace.Consents.FindAsync(tenant, customer.Id, query.Channel, cancellationToken)
            : null;
        var hasAddress = query.Channel switch
        {
            CommunicationChannel.Email => customer.Email is not null,
            CommunicationChannel.Sms or CommunicationChannel.WhatsApp => customer.Phone is not null,
            // Push : l'appareil est connu du module Notifications, pas de la fiche.
            _ => true,
        };

        var reason = customer.IsAnonymized ? "anonymized"
            : consent is null ? "no_consent"
            : !consent.IsGranted ? "consent_withdrawn"
            : !hasAddress ? "no_contact"
            : null;
        return new MarketingPermissionDto(customer.Id.Value, query.Channel, reason is null, reason);
    }
}

/// <summary>
/// Pour l'hôte (campagnes de Notifications) : les clients joignables en marketing sur ce canal, par lots, dans le
/// tenant du contexte. Seuls des ids sortent ; le consentement est revérifié à chaque envoi.
/// </summary>
/// <param name="Among">Seulement parmi ces clients (ceux d'un établissement) ; null : tous.</param>
public sealed record ListMarketingAudienceQuery(CommunicationChannel Channel, string? Tag, int Skip, int Take, IReadOnlyCollection<Guid>? Among = null)
    : IQuery<IReadOnlyList<Guid>>;

public sealed class ListMarketingAudienceHandler(CustomersWorkspace workspace, IMarketingAudienceReader audience)
    : IQueryHandler<ListMarketingAudienceQuery, IReadOnlyList<Guid>>
{
    public const int MaxTake = 1000;

    public async Task<IReadOnlyList<Guid>> HandleAsync(ListMarketingAudienceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        DomainException.ThrowIf(query.Skip < 0 || query.Take is < 1 or > MaxTake, CustomersApplicationErrors.PageInvalid);
        var ids = await audience.ListAsync(
            workspace.CurrentTenant, query.Channel, MarketingAudience.Tag(query.Tag), query.Skip, query.Take, MarketingAudience.Among(query.Among), cancellationToken);
        return ids.Select(id => id.Value).ToList();
    }
}

public sealed record CountMarketingAudienceQuery(CommunicationChannel Channel, string? Tag, IReadOnlyCollection<Guid>? Among = null) : IQuery<int>;

public sealed class CountMarketingAudienceHandler(CustomersWorkspace workspace, IMarketingAudienceReader audience)
    : IQueryHandler<CountMarketingAudienceQuery, int>
{
    public Task<int> HandleAsync(CountMarketingAudienceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return audience.CountAsync(workspace.CurrentTenant, query.Channel, MarketingAudience.Tag(query.Tag), MarketingAudience.Among(query.Among), cancellationToken);
    }
}

internal static class MarketingAudience
{
    public static CustomerTag? Tag(string? tag) => string.IsNullOrWhiteSpace(tag) ? null : CustomerTag.Create(tag);

    public static IReadOnlyCollection<CustomerId>? Among(IReadOnlyCollection<Guid>? ids) =>
        ids?.Where(id => id != Guid.Empty).Distinct().Select(CustomerId.From).ToList();
}
