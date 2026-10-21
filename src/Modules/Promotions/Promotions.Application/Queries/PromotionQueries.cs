using Promotions.Application.Abstractions;
using Promotions.Domain;

namespace Promotions.Application.Queries;

// Lectures du tenant courant, possibles même quand le tenant est suspendu. Une ressource d'un autre tenant n'est jamais
// trouvée.

public sealed record GetPromotionQuery(Guid PromotionId) : IQuery<PromotionDto>;

public sealed class GetPromotionHandler(PromotionsWorkspace workspace) : IQueryHandler<GetPromotionQuery, PromotionDto>
{
    public async Task<PromotionDto> HandleAsync(GetPromotionQuery query, CancellationToken cancellationToken = default) =>
        PromotionDto.From(await workspace.LoadPromotionAsync(query.PromotionId, cancellationToken));
}

public sealed record ListPromotionsQuery(PromotionStatus? Status = null, int Skip = 0, int Take = 50) : IQuery<IReadOnlyList<PromotionDto>>;

public sealed class ListPromotionsHandler(PromotionsWorkspace workspace) : IQueryHandler<ListPromotionsQuery, IReadOnlyList<PromotionDto>>
{
    public async Task<IReadOnlyList<PromotionDto>> HandleAsync(ListPromotionsQuery query, CancellationToken cancellationToken = default)
    {
        PromotionsWorkspace.EnsurePage(query.Skip, query.Take);
        return (await workspace.Promotions.ListAsync(workspace.CurrentTenant, query.Status, query.Skip, query.Take, cancellationToken))
            .Select(PromotionDto.From)
            .ToList();
    }
}

public sealed record ListCouponsQuery(Guid PromotionId) : IQuery<IReadOnlyList<CouponDto>>;

public sealed class ListCouponsHandler(PromotionsWorkspace workspace) : IQueryHandler<ListCouponsQuery, IReadOnlyList<CouponDto>>
{
    public async Task<IReadOnlyList<CouponDto>> HandleAsync(ListCouponsQuery query, CancellationToken cancellationToken = default)
    {
        var promotion = await workspace.LoadPromotionAsync(query.PromotionId, cancellationToken);
        return (await workspace.Coupons.ListForPromotionAsync(promotion.TenantId, promotion.Id, cancellationToken)).Select(CouponDto.From).ToList();
    }
}

public sealed record GetCouponQuery(Guid CouponId) : IQuery<CouponDto>;

public sealed class GetCouponHandler(PromotionsWorkspace workspace) : IQueryHandler<GetCouponQuery, CouponDto>
{
    public async Task<CouponDto> HandleAsync(GetCouponQuery query, CancellationToken cancellationToken = default) =>
        CouponDto.From(await workspace.LoadCouponAsync(query.CouponId, cancellationToken));
}

/// <summary>
/// Au comptoir, avant d'appliquer : ce que chaque promotion automatique (et celle du coupon présenté, s'il y en a un)
/// donnerait à la commande telle qu'elle est maintenant. Rien n'est enregistré. Les plus fortes remises d'abord.
/// </summary>
public sealed record PreviewOrderDiscountsQuery(Guid OrderId, string? CouponCode = null) : IQuery<IReadOnlyList<DiscountPreviewDto>>;

public sealed class PreviewOrderDiscountsHandler(PromotionsWorkspace workspace, IPromotionOrders orders, IClock clock)
    : IQueryHandler<PreviewOrderDiscountsQuery, IReadOnlyList<DiscountPreviewDto>>
{
    public async Task<IReadOnlyList<DiscountPreviewDto>> HandleAsync(PreviewOrderDiscountsQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var order = (query.OrderId == Guid.Empty ? null : await orders.FindAsync(tenant, OrderId.From(query.OrderId), cancellationToken))
            ?? throw new NotFoundException(PromotionsApplicationErrors.OrderNotFound);
        var now = clock.UtcNow;

        var candidates = (await workspace.Promotions.ListAutomaticAsync(tenant, now, cancellationToken))
            .Select(promotion => (Promotion: promotion, CouponPresented: false))
            .ToList();
        if (!string.IsNullOrWhiteSpace(query.CouponCode))
        {
            var coupon = await workspace.Coupons.FindByCodeAsync(tenant, CouponCode.Create(query.CouponCode), cancellationToken)
                ?? throw new NotFoundException(PromotionsApplicationErrors.CouponNotFound);
            var promotion = await workspace.Promotions.FindAsync(tenant, coupon.PromotionId, cancellationToken);
            if (promotion is not null)
            {
                candidates.RemoveAll(candidate => candidate.Promotion.Id == promotion.Id);
                candidates.Add((promotion, coupon.Status == CouponStatus.Active && !coupon.IsExhausted));
            }
        }

        var previews = new List<DiscountPreviewDto>();
        foreach (var (promotion, couponPresented) in candidates)
        {
            var evaluation = DiscountPolicy.Evaluate(PromotionsWorkspace.Snapshot(tenant, order, couponPresented), promotion.Rules, now);
            var reason = Blocking(promotion) ?? evaluation.Reason
                ?? (await workspace.Ledger.HasUsageForOrderAsync(tenant, promotion.Id, order.OrderId, cancellationToken)
                    ? PromotionErrors.AlreadyAppliedToOrder
                    : null);
            previews.Add(new DiscountPreviewDto(
                promotion.Id.Value,
                promotion.Name,
                reason is null,
                reason?.Code,
                reason is null ? evaluation.Discount.Amount : 0m,
                order.CurrencyCode));
        }

        return previews.OrderByDescending(preview => preview.Discount).ThenBy(preview => preview.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>Ce que le domaine refuserait avant même d'évaluer la commande (statut, limite d'usage).</summary>
    private static DomainError? Blocking(Promotion promotion) =>
        promotion.Status switch
        {
            PromotionStatus.Expired => PromotionErrors.Expired,
            not PromotionStatus.Active => PromotionErrors.NotActive,
            _ when promotion.IsUsageLimitReached => PromotionErrors.UsageLimitReached,
            _ => null,
        };
}

/// <summary>Une ligne d'un panier pas encore commandé : produit et total de ligne (options comprises, mode de prix du restaurant).</summary>
public sealed record CartLineInput(Guid ProductId, decimal LineTotal);

/// <summary>Ce que vaudrait un coupon sur un panier. Refusé : <paramref name="RefusalCode"/> dit pourquoi, la remise vaut 0.</summary>
public sealed record CartCouponPreviewDto(string Code, bool IsValid, string? RefusalCode, Guid? PromotionId, string? PromotionName, decimal Discount);

/// <summary>
/// Vente en ligne : ce que vaudrait <paramref name="CouponCode"/> sur un panier, avant que la commande existe. Mêmes
/// règles que <c>ApplyPromotionToOrderCommand</c> (coupon actif et non épuisé, promotion active, période, limite d'usage,
/// conditions du panier), sans rien compter. Un coupon inconnu n'est pas une erreur : il est simplement refusé.
/// </summary>
public sealed record PreviewCartCouponQuery(Guid LocationId, OrderType OrderType, string CurrencyCode, IReadOnlyList<CartLineInput> Lines, string? CouponCode)
    : IQuery<CartCouponPreviewDto>;

public sealed class PreviewCartCouponHandler(PromotionsWorkspace workspace, IClock clock) : IQueryHandler<PreviewCartCouponQuery, CartCouponPreviewDto>
{
    // Un panier n'a pas d'id : l'instantané en exige un, qui ne sert à rien d'autre.
    private static readonly OrderId CartId = OrderId.From(new Guid("00000000-0000-0000-0000-00000000ca27"));

    public async Task<CartCouponPreviewDto> HandleAsync(PreviewCartCouponQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var typed = query.CouponCode?.Trim().ToUpperInvariant() ?? string.Empty;
        CouponCode code;
        try
        {
            code = CouponCode.Create(query.CouponCode);
        }
        catch (DomainException exception)
        {
            return Refused(typed, exception.Error);
        }

        var tenant = workspace.CurrentTenant;
        var coupon = await workspace.Coupons.FindByCodeAsync(tenant, code, cancellationToken);
        if (coupon is null)
        {
            return Refused(code.Value, PromotionsApplicationErrors.CouponNotFound);
        }

        var promotion = await workspace.Promotions.FindAsync(tenant, coupon.PromotionId, cancellationToken);
        if (promotion is null)
        {
            return Refused(code.Value, PromotionsApplicationErrors.CouponNotFound);
        }

        var now = clock.UtcNow;
        var blocking = coupon.Status == CouponStatus.Disabled ? CouponErrors.Disabled
            : coupon.IsExhausted ? CouponErrors.Exhausted
            : promotion.Status == PromotionStatus.Expired ? PromotionErrors.Expired
            : promotion.Status != PromotionStatus.Active ? PromotionErrors.NotActive
            : promotion.IsUsageLimitReached ? PromotionErrors.UsageLimitReached
            : null;
        if (blocking is not null)
        {
            return Refused(code.Value, blocking);
        }

        var currency = Currency.FromCode(query.CurrencyCode);
        var snapshot = OrderSnapshot.Create(
            tenant,
            CartId,
            currency,
            LocationId.From(query.LocationId),
            query.OrderType,
            (query.Lines ?? []).Select(line => OrderLineSnapshot.Create(ProductId.From(line.ProductId), Money.Of(line.LineTotal, currency))),
            couponPresented: true);
        var evaluation = DiscountPolicy.Evaluate(snapshot, promotion.Rules, now);
        return evaluation.Reason is { } reason
            ? Refused(code.Value, reason)
            : new CartCouponPreviewDto(code.Value, true, null, promotion.Id.Value, promotion.Name, evaluation.Discount.Amount);
    }

    private static CartCouponPreviewDto Refused(string code, DomainError reason) => new(code, false, reason.Code, null, null, 0m);
}
