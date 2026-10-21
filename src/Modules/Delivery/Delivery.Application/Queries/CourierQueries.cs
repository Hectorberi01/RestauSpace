using Delivery.Application.Abstractions;
using Delivery.Domain;

namespace Delivery.Application.Queries;

/// <summary>Les livreurs du restaurant, par nom ; les désactivés seulement sur demande.</summary>
public sealed record ListCouriersQuery(bool IncludeInactive = false) : IQuery<IReadOnlyList<CourierDto>>;

public sealed class ListCouriersHandler(DeliveryWorkspace workspace) : IQueryHandler<ListCouriersQuery, IReadOnlyList<CourierDto>>
{
    public async Task<IReadOnlyList<CourierDto>> HandleAsync(ListCouriersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var couriers = await workspace.Couriers.ListAsync(workspace.CurrentTenant, query.IncludeInactive, cancellationToken);
        return couriers.Select(CourierDto.From).ToList();
    }
}

/// <summary>
/// L'argent des livreurs dans l'établissement : pour chacun, les courses terminées pas encore remises, les espèces qu'il
/// détient et ce qu'on lui doit. Du plus gros montant à remettre au plus petit.
/// </summary>
public sealed record GetCourierBalancesQuery(Guid LocationId) : IQuery<IReadOnlyList<CourierBalanceDto>>;

public sealed class GetCourierBalancesHandler(DeliveryWorkspace workspace) : IQueryHandler<GetCourierBalancesQuery, IReadOnlyList<CourierBalanceDto>>
{
    public async Task<IReadOnlyList<CourierBalanceDto>> HandleAsync(GetCourierBalancesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var location = await workspace.LoadLocationAsync(query.LocationId, cancellationToken);
        var pending = await workspace.Fulfillments.ListAwaitingRemittanceAsync(workspace.CurrentTenant, location.LocationId, cancellationToken);
        var groups = new List<List<Fulfillment>>();
        foreach (var delivery in pending.OrderBy(delivery => delivery.CompletedAtUtc))
        {
            var group = groups.FirstOrDefault(candidate => CourierRemittance.SameCourier(candidate[0], delivery) && candidate[0].FeeCurrency == delivery.FeeCurrency);
            if (group is null)
            {
                groups.Add([delivery]);
            }
            else
            {
                group.Add(delivery);
            }
        }

        return groups
            .Select(group =>
            {
                var first = group[0];
                var last = group[^1];
                var cash = group.Sum(delivery => delivery.CashHeld);
                var fees = group.Sum(delivery => delivery.CourierFeeAmount ?? 0m);
                return new CourierBalanceDto(
                    first.CourierId?.Value,
                    last.CourierName ?? string.Empty,
                    last.CourierPhone?.Value,
                    first.LocationId.Value,
                    first.FeeCurrency?.Code ?? string.Empty,
                    group.Count,
                    cash,
                    group.Where(delivery => delivery.CollectionMethod == CollectionMethod.MobileMoney).Sum(delivery => delivery.AmountCollected ?? 0m),
                    fees,
                    cash - fees,
                    group
                        .Select(delivery => new PendingDeliveryDto(
                            delivery.Id.Value,
                            delivery.OrderId.Value,
                            delivery.CustomerName,
                            delivery.ZoneName,
                            delivery.CompletedAtUtc,
                            delivery.AmountCollected,
                            delivery.CollectionMethod,
                            delivery.CashHeld,
                            delivery.CourierFeeAmount ?? 0m))
                        .ToList());
            })
            .OrderByDescending(balance => balance.Expected)
            .ThenBy(balance => balance.CourierName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}

/// <summary>Les remises, de la plus récente à la plus ancienne ; période : enregistrement dans [from, to[.</summary>
public sealed record ListRemittancesQuery(Guid? LocationId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, int Skip, int Take)
    : IQuery<IReadOnlyList<CourierRemittanceDto>>;

public sealed class ListRemittancesHandler(DeliveryWorkspace workspace) : IQueryHandler<ListRemittancesQuery, IReadOnlyList<CourierRemittanceDto>>
{
    public async Task<IReadOnlyList<CourierRemittanceDto>> HandleAsync(ListRemittancesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        DeliveryWorkspace.EnsurePage(query.Skip, query.Take);
        DomainException.ThrowIf(query.FromUtc >= query.ToUtc, DeliveryApplicationErrors.PeriodInvalid);
        if (query.LocationId == Guid.Empty)
        {
            return [];
        }

        var found = await workspace.Remittances.SearchAsync(
            workspace.CurrentTenant,
            query.LocationId is { } location ? LocationId.From(location) : null,
            query.FromUtc?.ToUniversalTime(),
            query.ToUtc?.ToUniversalTime(),
            query.Skip,
            query.Take,
            cancellationToken);
        return found.Select(remittance => CourierRemittanceDto.From(remittance, withItems: false)).ToList();
    }
}

public sealed record GetRemittanceQuery(Guid RemittanceId) : IQuery<CourierRemittanceDto>;

public sealed class GetRemittanceHandler(DeliveryWorkspace workspace) : IQueryHandler<GetRemittanceQuery, CourierRemittanceDto>
{
    public async Task<CourierRemittanceDto> HandleAsync(GetRemittanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return CourierRemittanceDto.From(await workspace.LoadRemittanceAsync(query.RemittanceId, cancellationToken));
    }
}
