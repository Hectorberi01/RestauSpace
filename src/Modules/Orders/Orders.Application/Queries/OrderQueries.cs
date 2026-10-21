using Orders.Application.Abstractions;
using Orders.Domain;

namespace Orders.Application.Queries;

// Lectures du tenant courant, ouvertes à tout membre et possibles même quand le tenant est suspendu. Une commande d'un
// autre tenant n'est jamais trouvée.

public sealed record GetOrderByIdQuery(Guid OrderId) : IQuery<OrderDto>;

public sealed class GetOrderByIdHandler(OrdersWorkspace workspace) : IQueryHandler<GetOrderByIdQuery, OrderDto>
{
    public async Task<OrderDto> HandleAsync(GetOrderByIdQuery query, CancellationToken cancellationToken = default) =>
        OrderDto.From(await workspace.LoadAsync(workspace.CurrentTenant, query.OrderId, cancellationToken));
}

/// <summary>
/// Historique des commandes, les plus récentes d'abord. Filtres facultatifs et cumulés : établissement, statut, session
/// de table, client, type, canal, période de création [<paramref name="FromUtc"/>, <paramref name="ToUtc"/>[, numéro
/// (complet ou sa fin : « 21 »), total TTC entre <paramref name="MinTotal"/> et <paramref name="MaxTotal"/> ;
/// <paramref name="ExcludeDrafts"/> : sans les brouillons (historique « tous statuts »).
/// </summary>
public sealed record ListOrdersQuery(
    int Page = 1,
    int PageSize = ListOrdersQuery.DefaultPageSize,
    Guid? LocationId = null,
    OrderStatus? Status = null,
    Guid? TableSessionId = null,
    Guid? CustomerId = null,
    OrderType? Type = null,
    OrderChannel? Channel = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? Number = null,
    decimal? MinTotal = null,
    decimal? MaxTotal = null,
    bool ExcludeDrafts = false) : IQuery<PagedResult<OrderSummaryDto>>
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>Garde l'offset loin du dépassement de capacité.</summary>
    public const int MaxPage = 100_000;
}

public sealed class ListOrdersHandler(OrdersWorkspace workspace) : IQueryHandler<ListOrdersQuery, PagedResult<OrderSummaryDto>>
{
    public async Task<PagedResult<OrderSummaryDto>> HandleAsync(ListOrdersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        if (query.Page is < 1 or > ListOrdersQuery.MaxPage || query.PageSize is < 1 or > ListOrdersQuery.MaxPageSize)
        {
            throw new DomainException(OrdersApplicationErrors.PagingInvalid);
        }

        DomainException.ThrowIf(query.FromUtc is { } from && query.ToUtc is { } to && from >= to, OrdersApplicationErrors.PeriodInvalid);
        DomainException.ThrowIf(
            query.MinTotal < 0 || query.MaxTotal < 0 || (query.MinTotal is { } min && query.MaxTotal is { } max && min > max),
            OrdersApplicationErrors.AmountRangeInvalid);
        DomainException.ThrowIf(query.Type is { } type && !Enum.IsDefined(type), OrdersApplicationErrors.FilterInvalid);
        DomainException.ThrowIf(query.Channel is { } channel && !Enum.IsDefined(channel), OrdersApplicationErrors.FilterInvalid);
        var number = OrderNumberFilter.Parse(query.Number);

        // Un id vide ne désigne rien : page vide plutôt qu'erreur, comme un id inconnu.
        if (query.LocationId == Guid.Empty || query.TableSessionId == Guid.Empty || query.CustomerId == Guid.Empty)
        {
            return PagedResult<OrderSummaryDto>.Counted([], query.Page, query.PageSize, 0);
        }

        var search = new OrderSearch(
            query.LocationId is { } location ? LocationId.From(location) : null,
            query.Status,
            query.TableSessionId is { } session ? TableSessionId.From(session) : null,
            query.CustomerId is { } customer ? CustomerId.From(customer) : null,
            query.Type,
            query.Channel,
            query.FromUtc?.ToUniversalTime(),
            query.ToUtc?.ToUniversalTime(),
            number,
            query.MinTotal,
            query.MaxTotal,
            query.ExcludeDrafts);
        var page = await workspace.Orders.ListAsync(tenant, search, (query.Page - 1) * query.PageSize, query.PageSize, cancellationToken);
        return PagedResult<OrderSummaryDto>.Counted(page.Items.Select(OrderSummaryDto.From).ToList(), query.Page, query.PageSize, page.TotalCount);
    }
}

/// <summary>
/// Pour le module Floor, via l'hôte : une commande de cette session (du tenant courant) est-elle encore ouverte (ni
/// terminée ni annulée, brouillon compris) ? Tant que oui, la table ne peut pas être libérée.
/// </summary>
public sealed record HasOpenOrdersForTableSessionQuery(Guid TableSessionId) : IQuery<bool>;

public sealed class HasOpenOrdersForTableSessionHandler(OrdersWorkspace workspace) : IQueryHandler<HasOpenOrdersForTableSessionQuery, bool>
{
    public async Task<bool> HandleAsync(HasOpenOrdersForTableSessionQuery query, CancellationToken cancellationToken = default) =>
        query.TableSessionId != Guid.Empty
        && await workspace.Orders.HasOpenOrdersAsync(workspace.CurrentTenant, TableSessionId.From(query.TableSessionId), cancellationToken);
}

/// <summary>Une ligne telle que les modules en aval la voient (<see cref="Order.DescribeLines"/>) : quoi préparer, sans prix.</summary>
public sealed record OrderLineDto(
    Guid OrderItemId,
    Guid ProductId,
    Guid? VariantId,
    string ProductName,
    string? VariantName,
    int Quantity,
    IReadOnlyList<string> ModifierNames,
    string? Note,
    string? FormulaName = null);

public sealed record OrderLinesDto(Guid OrderId, Guid LocationId, OrderStatus Status, IReadOnlyList<OrderLineDto> Lines, OrderType? Type = null);

/// <summary>
/// Pour Kitchen (via l'hôte) : les lignes d'une commande du tenant courant, par le seul contrat autorisé
/// (<see cref="Order.DescribeLines"/>), dans leur ordre d'ajout.
/// </summary>
public sealed record GetOrderLinesQuery(Guid OrderId) : IQuery<OrderLinesDto>;

public sealed class GetOrderLinesHandler(OrdersWorkspace workspace) : IQueryHandler<GetOrderLinesQuery, OrderLinesDto>
{
    public async Task<OrderLinesDto> HandleAsync(GetOrderLinesQuery query, CancellationToken cancellationToken = default)
    {
        var order = await workspace.LoadAsync(workspace.CurrentTenant, query.OrderId, cancellationToken);
        return new OrderLinesDto(
            order.Id.Value,
            order.LocationId.Value,
            order.Status,
            order.DescribeLines()
                .Select(line => new OrderLineDto(
                    line.OrderItemId.Value,
                    line.ProductId.Value,
                    line.VariantId?.Value,
                    line.ProductName,
                    line.VariantName,
                    line.Quantity,
                    line.Modifiers.Select(modifier => modifier.Name).ToList(),
                    line.Note,
                    line.FormulaName))
                .ToList(),
            order.Type);
    }
}

/// <summary>
/// Les clients d'un établissement (au moins une commande non annulée), pour cibler une campagne : ids seulement, au plus
/// <see cref="Max"/>. Un établissement d'un autre restaurant n'a aucun client.
/// </summary>
public sealed record ListLocationCustomersQuery(Guid LocationId) : IQuery<IReadOnlyList<Guid>>
{
    public const int Max = 50_000;
}

public sealed class ListLocationCustomersHandler(OrdersWorkspace workspace) : IQueryHandler<ListLocationCustomersQuery, IReadOnlyList<Guid>>
{
    public async Task<IReadOnlyList<Guid>> HandleAsync(ListLocationCustomersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.LocationId == Guid.Empty)
        {
            return [];
        }

        var ids = await workspace.Orders.ListCustomerIdsAsync(workspace.CurrentTenant, LocationId.From(query.LocationId), ListLocationCustomersQuery.Max, cancellationToken);
        return ids.Select(id => id.Value).ToList();
    }
}
