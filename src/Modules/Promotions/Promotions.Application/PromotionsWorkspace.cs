using Promotions.Application.Abstractions;
using Promotions.Domain;

namespace Promotions.Application;

/// <summary>
/// Étapes communes aux cas d'usage : le tenant vient du contexte (requête, ou message traité en arrière-plan), une
/// écriture exige un tenant qui les accepte, chaque agrégat est chargé dans ce tenant seulement (404 sinon), puis on
/// sauvegarde. Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class PromotionsWorkspace(
    ITenantContext tenantContext,
    IPromotionTenants tenants,
    IPromotionRepository promotions,
    ICouponRepository coupons,
    IPromotionUsageLedger ledger,
    IPromotionsUnitOfWork unitOfWork,
    IPromotionLocations locations,
    IPromotionProducts products)
{
    public const int MaxTake = 100;

    public IPromotionRepository Promotions => promotions;

    public ICouponRepository Coupons => coupons;

    public IPromotionUsageLedger Ledger => ledger;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    public Task<bool> AcceptsWritesAsync(CancellationToken cancellationToken) =>
        tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken);

    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await AcceptsWritesAsync(cancellationToken))
        {
            throw new DomainException(PromotionsApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<Promotion> LoadPromotionAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await promotions.FindAsync(CurrentTenant, PromotionId.From(id), cancellationToken))
        ?? throw new NotFoundException(PromotionsApplicationErrors.PromotionNotFound);

    public async Task<Coupon> LoadCouponAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await coupons.FindAsync(CurrentTenant, CouponId.From(id), cancellationToken))
        ?? throw new NotFoundException(PromotionsApplicationErrors.CouponNotFound);

    /// <summary>Charge la promotion du tenant courant (tenant qui accepte les écritures), applique <paramref name="change"/> et sauvegarde.</summary>
    public async Task EditPromotionAsync(Guid id, Action<Promotion> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await RequireWritableTenantAsync(cancellationToken);
        change(await LoadPromotionAsync(id, cancellationToken));
        await SaveAsync(cancellationToken);
    }

    /// <summary>Les établissements et produits ciblés par les règles appartiennent au tenant courant (404 sinon).</summary>
    public async Task RequireOwnTargetsAsync(PromotionRules rules, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);
        foreach (var locationId in rules.Conditions.LocationIds)
        {
            if (!await locations.ExistsAsync(CurrentTenant, locationId, cancellationToken))
            {
                throw new NotFoundException(PromotionsApplicationErrors.LocationNotFound);
            }
        }

        foreach (var productId in rules.Conditions.ProductIds)
        {
            if (!await products.ExistsAsync(CurrentTenant, productId, cancellationToken))
            {
                throw new NotFoundException(PromotionsApplicationErrors.ProductNotFound);
            }
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);

    public static void EnsurePage(int skip, int take) =>
        DomainException.ThrowIf(skip < 0 || take is < 1 or > MaxTake, PromotionsApplicationErrors.PageInvalid);

    /// <summary>
    /// L'instantané d'une commande, tel que le domaine l'évalue. Lignes en ordre quelconque (le domaine les trie), montants
    /// dans la devise de la commande. <paramref name="couponPresented"/> : un coupon de la promotion évaluée est présenté.
    /// </summary>
    public static OrderSnapshot Snapshot(TenantId tenantId, PromotionOrder order, bool couponPresented)
    {
        ArgumentNullException.ThrowIfNull(order);
        var currency = Currency.FromCode(order.CurrencyCode);
        return OrderSnapshot.Create(
            tenantId,
            order.OrderId,
            currency,
            order.LocationId,
            order.Type,
            order.Lines.Select(line => OrderLineSnapshot.Create(line.ProductId, Money.Of(line.LineTotal, currency))),
            couponPresented);
    }
}

/// <summary>
/// L'avantage saisi : <c>Percentage</c> (pourcentage, plafond facultatif) ou <c>FixedAmount</c> (montant). Les montants
/// sont dans la devise des règles (<see cref="PromotionRulesInput.CurrencyCode"/>).
/// </summary>
public sealed record BenefitInput(BenefitType Type, decimal? Percentage = null, decimal? Amount = null, decimal? MaxDiscount = null);

/// <summary>
/// Les règles saisies d'une promotion. <paramref name="CurrencyCode"/> : devise de TOUS les montants (avantage fixe,
/// plafond, minimum de commande) ; facultative pour un pourcentage sans plafond ni minimum. Listes vides = aucune
/// restriction. Une période [début, fin) en UTC.
/// </summary>
public sealed record PromotionRulesInput(
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ValidUntilUtc,
    BenefitInput? Benefit = null,
    string? CurrencyCode = null,
    decimal? MinimumOrderAmount = null,
    IReadOnlyList<Guid>? ProductIds = null,
    IReadOnlyList<Guid>? LocationIds = null,
    IReadOnlyList<OrderType>? OrderTypes = null,
    int? UsageLimit = null,
    bool RequiresCoupon = false)
{
    /// <summary>Validées et construites par les fabriques du domaine (erreur métier sinon).</summary>
    public PromotionRules ToRules()
    {
        var currency = string.IsNullOrWhiteSpace(CurrencyCode) ? null : Currency.FromCode(CurrencyCode);
        var conditions = Conditions.Create(
            Amount(MinimumOrderAmount, currency),
            (ProductIds ?? Array.Empty<Guid>()).Select(ProductId.From),
            (LocationIds ?? Array.Empty<Guid>()).Select(LocationId.From),
            OrderTypes);

        return PromotionRules.Create(
            Period.Create(ValidFromUtc, ValidUntilUtc),
            conditions,
            ToBenefit(currency),
            UsageLimit is { } limit ? Domain.UsageLimit.Of(limit) : null,
            RequiresCoupon);
    }

    private Domain.Benefit? ToBenefit(Currency? currency) =>
        Benefit switch
        {
            null => null,
            { Type: BenefitType.Percentage, Percentage: { } percentage, Amount: null } =>
                Domain.Benefit.PercentageOff(percentage, Amount(Benefit.MaxDiscount, currency)),
            { Type: BenefitType.FixedAmount, Amount: { } amount, Percentage: null, MaxDiscount: null } =>
                Domain.Benefit.FixedAmountOff(Amount(amount, currency)!),
            _ => throw new DomainException(PromotionsApplicationErrors.BenefitInvalid),
        };

    /// <summary>Un montant saisi exige une devise.</summary>
    private static Money? Amount(decimal? amount, Currency? currency)
    {
        if (amount is not { } value)
        {
            return null;
        }

        DomainException.ThrowIf(currency is null, PromotionsApplicationErrors.BenefitInvalid);
        return Money.Of(value, currency!);
    }
}
