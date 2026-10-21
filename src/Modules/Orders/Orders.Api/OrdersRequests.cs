using Orders.Application.Queries;
using Orders.Domain;

namespace Orders.Api;

public sealed record CreatedResponse(Guid Id);

/// <summary><paramref name="TableSessionId"/> : session de table (Floor), seulement pour <c>DineIn</c>.</summary>
/// <param name="CustomerId">Facultatif : la fiche client (module Customers).</param>
/// <param name="Channel">Facultatif, pour l'équipe : <c>Phone</c> ou <c>WhatsApp</c> pour une commande reçue à distance ; sinon comptoir.</param>
public sealed record CreateOrderRequest(
    Guid LocationId,
    OrderType Type,
    Guid? TableSessionId = null,
    Guid? CustomerId = null,
    DateTimeOffset? ScheduledForUtc = null,
    OrderChannel? Channel = null,
    string? Platform = null,
    string? ExternalReference = null);

/// <summary><c>components</c> : pour une formule, le produit choisi à chaque étape (avec sa variante et ses options).</summary>
public sealed record AddItemRequest(
    Guid ProductId,
    int Quantity,
    Guid? VariantId = null,
    IReadOnlyList<Guid>? ModifierIds = null,
    string? Note = null,
    IReadOnlyList<Orders.Application.Abstractions.OrderComponentInput>? Components = null);

public sealed record QuantityRequest(int Quantity);

public sealed record NoteRequest(string? Note);

/// <summary>Montant dans la devise et le mode de prix (TTC/HT) de la commande.</summary>
public sealed record ManualDiscountRequest(decimal Amount, string? Reference, string? Label = null);

public sealed record CancelOrderRequest(string? Reason);

/// <param name="CustomerId">La fiche client à rattacher ; null pour détacher la commande.</param>
public sealed record OrderCustomerRequest(Guid? CustomerId);

public sealed record ReplaceItemRequest(
    int Quantity,
    Guid? VariantId = null,
    IReadOnlyList<Guid>? ModifierIds = null,
    string? Note = null,
    IReadOnlyList<Orders.Application.Abstractions.OrderComponentInput>? Components = null);

public sealed record TableSessionRequest(Guid TableSessionId);

/// <summary>
/// Filtres de l'historique (paramètres de requête). <c>from</c>/<c>to</c> : période de création [from, to[ ;
/// <c>number</c> : numéro complet ou sa fin (« 21 ») ; <c>minTotal</c>/<c>maxTotal</c> : total TTC, bornes comprises.
/// </summary>
public sealed record OrderHistoryFilter(
    int? Page,
    int? PageSize,
    Guid? LocationId,
    OrderStatus? Status,
    Guid? TableSessionId,
    OrderType? Type,
    OrderChannel? Channel,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Number,
    decimal? MinTotal,
    decimal? MaxTotal,
    bool? ExcludeDrafts)
{
    public ListOrdersQuery ToQuery(Guid? customerId) => new(
        Page ?? 1,
        PageSize ?? ListOrdersQuery.DefaultPageSize,
        LocationId,
        Status,
        TableSessionId,
        customerId,
        Type,
        Channel,
        From,
        To,
        Number,
        MinTotal,
        MaxTotal,
        ExcludeDrafts ?? false);
}
