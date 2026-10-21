namespace Loyalty.Domain;

public sealed class Reward : AggregateRoot<RewardId>, ITenantOwned
{
    public const int MaxNameLength = 120;

    private Reward(RewardId id, TenantId tenantId, string name, Points pointCost, Money discountValue) : base(id)
    {
        DiscountValue = discountValue;
        TenantId = tenantId;
        Name = name;
        PointCost = pointCost;
        Status = RewardStatus.Active;
    }

    // Réservé à l'ORM.
    private Reward()
    {
        Name = null!;
        PointCost = null!;
        DiscountValue = null!;
    }

    public TenantId TenantId { get; }

    public string Name { get; private set; }

    public Points PointCost { get; private set; }

    /// <summary>Montant déduit de la commande quand la récompense est utilisée ; strictement positif.</summary>
    public Money DiscountValue { get; private set; }

    public RewardStatus Status { get; private set; }

    public static Reward Create(TenantId tenantId, string name, Points pointCost, Money discountValue, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(pointCost);
        ArgumentNullException.ThrowIfNull(discountValue);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);

        var trimmed = name?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed), RewardErrors.NameRequired);
        DomainException.ThrowIf(trimmed!.Length > MaxNameLength, RewardErrors.NameTooLong);
        DomainException.ThrowIf(pointCost.IsZero, RewardErrors.CostNotPositive);
        DomainException.ThrowIf(!discountValue.IsPositive, RewardErrors.DiscountValueNotPositive);

        var reward = new Reward(RewardId.New(), tenantId, trimmed, pointCost, discountValue);
        reward.Raise(new RewardCreated(tenantId, reward.Id, trimmed, pointCost, discountValue, clock.UtcNow));
        return reward;
    }

    public void Activate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == RewardStatus.Active, RewardErrors.AlreadyActive);

        Status = RewardStatus.Active;
        Raise(new RewardActivated(TenantId, Id, clock.UtcNow));
    }

    public void Deactivate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == RewardStatus.Inactive, RewardErrors.AlreadyInactive);

        Status = RewardStatus.Inactive;
        Raise(new RewardDeactivated(TenantId, Id, clock.UtcNow));
    }

    /// <summary>Utilisations futures uniquement : les écritures passées du registre gardent leurs points.</summary>
    public void ChangeCost(Points newCost, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newCost);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(newCost.IsZero, RewardErrors.CostNotPositive);
        if (newCost == PointCost)
        {
            return;
        }

        var previous = PointCost;
        PointCost = newCost;
        Raise(new RewardCostChanged(TenantId, Id, previous, newCost, clock.UtcNow));
    }

    /// <summary>Utilisations futures uniquement : une remise déjà appliquée à une commande garde son montant.</summary>
    public void ChangeDiscountValue(Money newValue, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newValue);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!newValue.IsPositive, RewardErrors.DiscountValueNotPositive);
        if (newValue == DiscountValue)
        {
            return;
        }

        var previous = DiscountValue;
        DiscountValue = newValue;
        Raise(new RewardDiscountValueChanged(TenantId, Id, previous, newValue, clock.UtcNow));
    }

    public RewardReference ToReference() => new(TenantId, Id, PointCost, DiscountValue, Status == RewardStatus.Active);
}
