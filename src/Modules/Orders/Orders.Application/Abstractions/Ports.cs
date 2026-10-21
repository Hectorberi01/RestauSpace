using Orders.Domain;

namespace Orders.Application.Abstractions;

// Chaque méthode prend le tenant explicitement : il vient du contexte de la requête (jamais de la requête elle-même).
// Une ressource d'un autre tenant n'est tout simplement pas trouvée.

public interface IOrderRepository
{
    Task<Order?> FindAsync(TenantId tenantId, OrderId id, CancellationToken cancellationToken = default);

    /// <summary>Page en lecture seule, les plus récentes d'abord ; filtres facultatifs, déjà validés.</summary>
    Task<OrderPage> ListAsync(TenantId tenantId, OrderSearch search, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Une commande de cette session n'est-elle ni terminée ni annulée (un brouillon compte) ?</summary>
    Task<bool> HasOpenOrdersAsync(TenantId tenantId, TableSessionId tableSessionId, CancellationToken cancellationToken = default);

    /// <summary>Commandes de cette session ni terminées ni annulées, suivies pour modification.</summary>
    Task<IReadOnlyList<Order>> ListOpenByTableSessionAsync(TenantId tenantId, TableSessionId tableSessionId, CancellationToken cancellationToken = default);

    /// <summary>Commandes passées (annulées comprises) dont le passage tombe dans [<paramref name="fromUtc"/>, <paramref name="toUtc"/>).</summary>
    Task<int> CountPlacedAsync(TenantId tenantId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);

    /// <summary>Les clients (distincts) d'au moins une commande non annulée de cet établissement, au plus <paramref name="max"/>.</summary>
    Task<IReadOnlyList<CustomerId>> ListCustomerIdsAsync(TenantId tenantId, LocationId locationId, int max, CancellationToken cancellationToken = default);

    void Add(Order order);
}

public sealed record OrderPage(IReadOnlyList<Order> Items, int TotalCount);

/// <summary>
/// Filtres de l'historique, tous facultatifs et cumulés. Période : création dans [<paramref name="FromUtc"/>,
/// <paramref name="ToUtc"/>[. <paramref name="Number"/> : le numéro complet, ou sa fin (« 21 », « 021 ») qui désigne le
/// compteur du jour. Montants : total TTC de la commande, bornes comprises.
/// </summary>
public sealed record OrderSearch(
    LocationId? LocationId = null,
    OrderStatus? Status = null,
    TableSessionId? TableSessionId = null,
    CustomerId? CustomerId = null,
    OrderType? Type = null,
    OrderChannel? Channel = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    OrderNumberFilter? Number = null,
    decimal? MinTotal = null,
    decimal? MaxTotal = null,
    bool ExcludeDrafts = false);

/// <summary>Un numéro complet (« 20261016-021 ») ou la fin d'un numéro (« -021 »), déjà normalisé.</summary>
public sealed record OrderNumberFilter(string Value, bool IsSuffix)
{
    public const int MaxSuffixDigits = 6;

    /// <summary>Null pour une saisie vide ; « 21 », « #21 » et « 021 » donnent la fin « -021 ».</summary>
    public static OrderNumberFilter? Parse(string? input)
    {
        var value = input?.Trim().TrimStart('#').Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.All(char.IsAsciiDigit) && value.Length <= MaxSuffixDigits)
        {
            return new OrderNumberFilter("-" + value.TrimStart('0').PadLeft(3, '0'), IsSuffix: true);
        }

        return new OrderNumberFilter(OrderNumber.Create(value).Value, IsSuffix: false);
    }
}

/// <summary>
/// Numéro suivant d'un établissement pour un jour (UTC) : « AAAAMMJJ-NNN », compteur atomique en base. Un numéro tiré
/// pour une commande finalement non enregistrée est perdu (trou dans la suite), jamais réutilisé.
/// </summary>
public interface IOrderNumberGenerator
{
    Task<OrderNumber> NextAsync(TenantId tenantId, LocationId locationId, DateOnly day, CancellationToken cancellationToken = default);
}

/// <summary>
/// Le plafond de commandes passées par mois civil UTC prévu par le plan du restaurant, fourni par l'hôte depuis Billing.
/// Null : aucun plafond. Vérifié au passage de la commande, pas au brouillon.
/// </summary>
public interface IOrderPlanLimits
{
    Task<long?> MonthlyOrdersAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Ce qu'Orders doit savoir de son tenant, fourni par l'hôte depuis le module Tenants.</summary>
public interface IOrderTenants
{
    /// <summary>Null quand le tenant n'existe pas.</summary>
    Task<OrderTenant?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <param name="Currency">Devise des nouvelles commandes.</param>
/// <param name="PriceTaxMode">Les prix du catalogue sont-ils TTC (réglage du tenant) ? Figé sur chaque nouvelle commande.</param>
/// <param name="AcceptsBusinessWrites">Faux pour un tenant suspendu ou fermé.</param>
public sealed record OrderTenant(Currency Currency, PriceTaxMode PriceTaxMode, bool AcceptsBusinessWrites);

/// <summary>Un établissement du tenant courant, fourni par l'hôte depuis Locations ; null s'il n'existe pas.</summary>
public interface IOrderLocations
{
    Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);
}

/// <summary>Une session de table du tenant courant, fournie par l'hôte depuis Floor ; null si elle n'existe pas.</summary>
public interface IOrderTableSessions
{
    Task<TableSessionReference?> FindAsync(TenantId tenantId, TableSessionId sessionId, CancellationToken cancellationToken = default);
}

/// <summary>
/// La vente d'un produit du tenant courant, décrite par le Catalog (via l'hôte) : produit actif, variante disponible,
/// options valides pour ses groupes, prix et taxe. Le Catalog refuse (erreur métier) ce qui ne se vend pas ; null
/// quand le produit n'existe pas.
/// </summary>
public interface IOrderCatalog
{
    /// <param name="locationId">Établissement de la vente : son prix propre s'il en a un.</param>
    /// <param name="channel">Canal de la vente : son prix propre s'il en a un.</param>
    Task<ProductSnapshot?> DescribeForSaleAsync(
        TenantId tenantId,
        ProductId productId,
        VariantId? variantId,
        IReadOnlyList<ModifierId> modifierIds,
        LocationId locationId,
        OrderChannel channel,
        IReadOnlyList<ComponentChoice>? components = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Un produit choisi à une étape d'une formule, avec sa variante et ses options.</summary>
public sealed record ComponentChoice(Guid StepId, ProductId ProductId, VariantId? VariantId, IReadOnlyList<ModifierId> ModifierIds);

public static class ComponentChoices
{
    public static IReadOnlyList<ComponentChoice> From(IReadOnlyList<OrderComponentInput>? inputs) =>
        (inputs ?? [])
            .Select(input => new ComponentChoice(
                input.StepId,
                input.ProductId == Guid.Empty ? throw new DomainException(OrderErrors.ComponentInvalid) : ProductId.From(input.ProductId),
                input.VariantId is { } variant ? VariantId.From(variant) : null,
                (input.ModifierIds ?? []).Select(ModifierId.From).ToList()))
            .ToList();
}

/// <summary>Un produit choisi à une étape d'une formule (<paramref name="StepId"/>), avec sa variante et ses options.</summary>
public sealed record OrderComponentInput(Guid StepId, Guid ProductId, Guid? VariantId = null, IReadOnlyList<Guid>? ModifierIds = null);

/// <summary>
/// Ce qui a été réglé pour une commande : capturé moins remboursé et moins les remboursements en cours ; zéro sans
/// paiement. Lu dans la projection locale tenue à jour par <see cref="IOrderPaymentSettlements"/>.
/// </summary>
public interface IOrderPayments
{
    Task<Money> SettledAmountAsync(TenantId tenantId, OrderId orderId, Currency currency, CancellationToken cancellationToken = default);
}

public interface IOrderPaymentSettlements
{
    Task RecordAsync(TenantId tenantId, Guid paymentId, OrderId orderId, Money settledAmount, long version, CancellationToken cancellationToken = default);
}

/// <summary>
/// Un client CRM du tenant courant, fourni par l'hôte depuis Customers : seulement (tenant, id, anonymisé) ; null s'il
/// n'existe pas (ou est d'un autre tenant).
/// </summary>
public interface IOrderCustomers
{
    Task<CustomerReference?> FindAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default);
}

/// <summary>Rattache tout l'historique d'un client fusionné à la fiche qui l'a absorbé (lignes du tenant donné seulement).</summary>
public interface IOrderCustomerReassignment
{
    /// <summary>Renvoie le nombre de lignes rattachées ; 0 quand tout est déjà fait.</summary>
    Task<int> ReassignAsync(TenantId tenantId, CustomerId from, CustomerId to, CancellationToken cancellationToken = default);
}
