namespace Loyalty.Domain;

public enum RewardStatus
{
    Active = 1,
    Inactive = 2,
}

public static class RewardErrors
{
    public static readonly DomainError NameRequired = new("loyalty.reward.name_required", "Reward name is required.");
    public static readonly DomainError NameTooLong = new("loyalty.reward.name_too_long", "Reward name is too long.");
    public static readonly DomainError CostNotPositive = new("loyalty.reward.cost_not_positive", "A reward must cost at least one point.");
    public static readonly DomainError DiscountValueNotPositive = new("loyalty.reward.discount_value_not_positive", "A reward discount value must be strictly positive.");
    public static readonly DomainError AlreadyActive = new("loyalty.reward.already_active", "The reward is already active.");
    public static readonly DomainError AlreadyInactive = new("loyalty.reward.already_inactive", "The reward is already inactive.");
}

public sealed record RewardCreated(
    TenantId TenantId,
    RewardId RewardId,
    string Name,
    Points PointCost,
    Money DiscountValue,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record RewardActivated(TenantId TenantId, RewardId RewardId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record RewardDeactivated(TenantId TenantId, RewardId RewardId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record RewardCostChanged(
    TenantId TenantId,
    RewardId RewardId,
    Points PreviousCost,
    Points NewCost,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record RewardDiscountValueChanged(
    TenantId TenantId,
    RewardId RewardId,
    Money PreviousValue,
    Money NewValue,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
