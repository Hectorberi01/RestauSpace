using Delivery.Application.Abstractions;
using Delivery.Domain;

namespace Delivery.Application.Queries;

// Lectures du tenant courant, possibles même quand le tenant est suspendu. Une ressource d'un autre tenant n'est jamais
// trouvée.

/// <summary>Réglages de l'établissement ; les valeurs par défaut (non enregistrées) s'il n'a encore rien réglé.</summary>
public sealed record GetDeliverySettingsQuery(Guid LocationId) : IQuery<DeliverySettingsDto>;

public sealed class GetDeliverySettingsHandler(DeliveryWorkspace workspace) : IQueryHandler<GetDeliverySettingsQuery, DeliverySettingsDto>
{
    public async Task<DeliverySettingsDto> HandleAsync(GetDeliverySettingsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var location = await workspace.LoadLocationAsync(query.LocationId, cancellationToken);
        var settings = await workspace.Settings.FindAsync(workspace.CurrentTenant, location.LocationId, cancellationToken);
        return settings is null
            ? DeliverySettingsDto.Defaults(location.LocationId, await workspace.CurrencyAsync(cancellationToken))
            : DeliverySettingsDto.From(settings);
    }
}

public sealed record GetFulfillmentQuery(Guid FulfillmentId) : IQuery<FulfillmentDto>;

public sealed class GetFulfillmentHandler(DeliveryWorkspace workspace) : IQueryHandler<GetFulfillmentQuery, FulfillmentDto>
{
    public async Task<FulfillmentDto> HandleAsync(GetFulfillmentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return FulfillmentDto.From(await workspace.LoadFulfillmentAsync(query.FulfillmentId, cancellationToken));
    }
}

/// <summary>La fiche active (non annulée) d'une commande ; 404 s'il n'y en a pas.</summary>
public sealed record GetOrderFulfillmentQuery(Guid OrderId) : IQuery<FulfillmentDto>;

public sealed class GetOrderFulfillmentHandler(DeliveryWorkspace workspace) : IQueryHandler<GetOrderFulfillmentQuery, FulfillmentDto>
{
    public async Task<FulfillmentDto> HandleAsync(GetOrderFulfillmentQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var fulfillment = query.OrderId == Guid.Empty
            ? null
            : await workspace.Fulfillments.FindActiveForOrderAsync(workspace.CurrentTenant, OrderId.From(query.OrderId), cancellationToken);
        return FulfillmentDto.From(fulfillment ?? throw new NotFoundException(DeliveryApplicationErrors.FulfillmentNotFound));
    }
}

/// <summary>
/// Fiches de l'établissement (ou de tous), de la promesse la plus proche à la plus lointaine. <paramref name="OpenOnly"/> :
/// ni terminées ni annulées. Période : création dans [<paramref name="FromUtc"/>, <paramref name="ToUtc"/>[.
/// </summary>
public sealed record ListFulfillmentsQuery(
    Guid? LocationId,
    FulfillmentMode? Mode,
    FulfillmentStatus? Status,
    bool OpenOnly,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int Skip,
    int Take) : IQuery<IReadOnlyList<FulfillmentDto>>;

public sealed class ListFulfillmentsHandler(DeliveryWorkspace workspace) : IQueryHandler<ListFulfillmentsQuery, IReadOnlyList<FulfillmentDto>>
{
    public async Task<IReadOnlyList<FulfillmentDto>> HandleAsync(ListFulfillmentsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        DeliveryWorkspace.EnsurePage(query.Skip, query.Take);
        DomainException.ThrowIf(query.FromUtc >= query.ToUtc, DeliveryApplicationErrors.PeriodInvalid);
        if (query.LocationId == Guid.Empty)
        {
            return [];
        }

        var search = new FulfillmentSearch(
            query.LocationId is { } location ? LocationId.From(location) : null,
            query.Mode,
            query.Status,
            query.OpenOnly,
            query.FromUtc?.ToUniversalTime(),
            query.ToUtc?.ToUniversalTime());
        var found = await workspace.Fulfillments.SearchAsync(workspace.CurrentTenant, search, query.Skip, query.Take, cancellationToken);
        return found.Select(FulfillmentDto.From).ToList();
    }
}
