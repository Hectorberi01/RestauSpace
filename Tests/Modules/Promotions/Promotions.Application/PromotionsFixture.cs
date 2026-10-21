using Promotions.Application.Abstractions;
using Promotions.Application.Commands;
using Promotions.Application.Queries;
using Promotions.Domain;

namespace Promotions.Application;

/// <summary>
/// Ports en mémoire du module Promotions. Comme les vrais, ils ne répondent jamais que dans le tenant qui leur est donné.
/// Le registre d'usages est tenu, comme par l'unit of work réel, à partir des événements des agrégats à chaque
/// sauvegarde. Le tenant de « la requête » est <see cref="CurrentTenantId"/>.
/// </summary>
public sealed class PromotionsFixture
    : ITenantContext, IPromotionsUnitOfWork, IPromotionTenants, IPromotionRepository, ICouponRepository, IPromotionUsageLedger,
      ICouponCodeUniquenessChecker, IOrderPromotionUsages, IPromotionOrders
{
    private readonly HashSet<IDomainEvent> _recorded = new(ReferenceEqualityComparer.Instance);

    public PromotionsFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        Writable[TenantA] = true;
        Writable[TenantB] = true;
        CurrentTenantId = TenantA;
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    public Dictionary<TenantId, bool> Writable { get; } = [];

    public List<Promotion> Promotions { get; } = [];

    public List<Coupon> Coupons { get; } = [];

    /// <summary>(tenant, promotion ou coupon, commande, détenu).</summary>
    public List<(TenantId Tenant, Guid Owner, OrderId Order, bool Held)> Usages { get; } = [];

    public Dictionary<(TenantId, OrderId), PromotionOrder> Orders { get; } = [];

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public PromotionsWorkspace Workspace => new(this, this, this, this, this, this);

    public CreatePromotionHandler Create => new(Workspace, Clock);

    public ChangePromotionRulesHandler ChangeRules => new(Workspace, Clock);

    public ActivatePromotionHandler Activate => new(Workspace, Clock);

    public PausePromotionHandler Pause => new(Workspace, Clock);

    public ExpirePromotionHandler ExpireOne => new(Workspace, Clock);

    public CreateCouponHandler CreateCoupon => new(Workspace, this, Clock);

    public DisableCouponHandler DisableCoupon => new(Workspace, Clock);

    public ApplyPromotionToOrderHandler Apply => new(Workspace, this, Clock);

    public ReleaseOrderPromotionsHandler Release => new(Workspace, this, Clock);

    public RevertPromotionForOrderHandler Revert => new(Workspace, this, Clock);

    public ExpireEndedPromotionsHandler ExpireEnded => new(Workspace, Clock);

    public GetPromotionHandler Get => new(Workspace);

    public ListPromotionsHandler List => new(Workspace);

    public ListCouponsHandler ListCoupons => new(Workspace);

    public PreviewOrderDiscountsHandler Preview => new(Workspace, this, Clock);

    public PreviewCartCouponHandler PreviewCart => new(Workspace, Clock);

    public Promotion PromotionById(Guid id) => Promotions.Single(promotion => promotion.Id.Value == id);

    public Coupon CouponById(Guid id) => Coupons.Single(coupon => coupon.Id.Value == id);

    /// <summary>Une commande en brouillon du tenant courant, en euros : une ligne par montant donné.</summary>
    public Guid NewOrder(bool acceptsDiscounts = true, OrderType type = OrderType.DineIn, LocationId? location = null, params decimal[] lines)
    {
        var order = OrderId.New();
        Orders[(CurrentTenantId!.Value, order)] = new PromotionOrder(
            order,
            location ?? LocationId.New(),
            type,
            "EUR",
            acceptsDiscounts,
            (lines.Length == 0 ? new[] { 40m } : lines).Select(total => new PromotionOrderLine(ProductId.New(), total)).ToList());
        return order.Value;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        var events = Promotions.Cast<IAggregateRoot>().Concat(Coupons)
            .SelectMany(aggregate => aggregate.DomainEvents)
            .Where(domainEvent => _recorded.Add(domainEvent))
            .ToList();
        foreach (var domainEvent in events)
        {
            switch (domainEvent)
            {
                case PromotionApplied applied:
                    Usages.Add((applied.TenantId, applied.PromotionId.Value, applied.OrderId, true));
                    break;
                case CouponRedeemed redeemed:
                    Usages.Add((redeemed.TenantId, redeemed.CouponId.Value, redeemed.OrderId, true));
                    break;
                case PromotionUsageReverted reverted:
                    GiveBack(reverted.TenantId, reverted.PromotionId.Value, reverted.OrderId);
                    break;
                case CouponReleased released:
                    GiveBack(released.TenantId, released.CouponId.Value, released.OrderId);
                    break;
            }
        }

        return Task.CompletedTask;
    }

    private void GiveBack(TenantId tenant, Guid owner, OrderId order)
    {
        var index = Usages.FindIndex(usage => usage.Tenant == tenant && usage.Owner == owner && usage.Order == order && usage.Held);
        Usages[index] = Usages[index] with { Held = false };
    }

    private bool IsHeld(TenantId tenant, Guid owner, OrderId order) =>
        Usages.Any(usage => usage.Tenant == tenant && usage.Owner == owner && usage.Order == order && usage.Held);

    Task<bool> IPromotionTenants.AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Writable.TryGetValue(tenantId, out var writable) && writable);

    Task<Promotion?> IPromotionRepository.FindAsync(TenantId tenantId, PromotionId id, CancellationToken cancellationToken) =>
        Task.FromResult<Promotion?>(Promotions.SingleOrDefault(promotion => promotion.TenantId == tenantId && promotion.Id == id));

    Task<IReadOnlyList<Promotion>> IPromotionRepository.ListAsync(
        TenantId tenantId, PromotionStatus? status, int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Promotion>>(Promotions
            .Where(promotion => promotion.TenantId == tenantId && (status is null || promotion.Status == status))
            .OrderByDescending(promotion => promotion.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToList());

    Task<IReadOnlyList<Promotion>> IPromotionRepository.ListAutomaticAsync(TenantId tenantId, DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Promotion>>(Promotions
            .Where(promotion => promotion.TenantId == tenantId && promotion.Status == PromotionStatus.Active)
            .Where(promotion => !promotion.Rules.RequiresCoupon && promotion.ValidityPeriod.Contains(nowUtc))
            .ToList());

    Task<IReadOnlyList<Promotion>> IPromotionRepository.ListEndedAsync(TenantId tenantId, DateTimeOffset nowUtc, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Promotion>>(Promotions
            .Where(promotion => promotion.TenantId == tenantId && promotion.Status != PromotionStatus.Expired)
            .Where(promotion => promotion.ValidityPeriod.HasEndedAt(nowUtc))
            .Take(take)
            .ToList());

    void IPromotionRepository.Add(Promotion promotion) => Promotions.Add(promotion);

    Task<Coupon?> ICouponRepository.FindAsync(TenantId tenantId, CouponId id, CancellationToken cancellationToken) =>
        Task.FromResult<Coupon?>(Coupons.SingleOrDefault(coupon => coupon.TenantId == tenantId && coupon.Id == id));

    Task<Coupon?> ICouponRepository.FindByCodeAsync(TenantId tenantId, CouponCode code, CancellationToken cancellationToken) =>
        Task.FromResult<Coupon?>(Coupons.SingleOrDefault(coupon => coupon.TenantId == tenantId && coupon.Code == code));

    Task<IReadOnlyList<Coupon>> ICouponRepository.ListForPromotionAsync(TenantId tenantId, PromotionId promotionId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Coupon>>(Coupons.Where(coupon => coupon.TenantId == tenantId && coupon.PromotionId == promotionId).ToList());

    void ICouponRepository.Add(Coupon coupon) => Coupons.Add(coupon);

    Task<bool> IPromotionUsageLedger.HasUsageForOrderAsync(TenantId tenantId, PromotionId promotionId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult(IsHeld(tenantId, promotionId.Value, orderId));

    Task<bool> IPromotionUsageLedger.HasUsageForOrderAsync(TenantId tenantId, CouponId couponId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult(IsHeld(tenantId, couponId.Value, orderId));

    Task<bool> ICouponCodeUniquenessChecker.IsUniqueAsync(TenantId tenantId, CouponCode code, CancellationToken cancellationToken) =>
        Task.FromResult(!Coupons.Any(coupon => coupon.TenantId == tenantId && coupon.Code == code));

    Task<IReadOnlyList<PromotionId>> IOrderPromotionUsages.PromotionsHeldByAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PromotionId>>(Promotions
            .Where(promotion => IsHeld(tenantId, promotion.Id.Value, orderId))
            .Select(promotion => promotion.Id)
            .ToList());

    Task<IReadOnlyList<CouponId>> IOrderPromotionUsages.CouponsHeldByAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CouponId>>(Coupons
            .Where(coupon => IsHeld(tenantId, coupon.Id.Value, orderId))
            .Select(coupon => coupon.Id)
            .ToList());

    Task<PromotionOrder?> IPromotionOrders.FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult<PromotionOrder?>(Orders.GetValueOrDefault((tenantId, orderId)));
}
