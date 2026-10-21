using Loyalty.Domain;

namespace Loyalty.Application.Queries;

public sealed record LoyaltyAccountDto(
    Guid Id, Guid CustomerId, long Balance, LoyaltyAccountStatus Status, DateTimeOffset OpenedAtUtc, DateTimeOffset? ClosedAtUtc)
{
    public static LoyaltyAccountDto From(LoyaltyAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new(account.Id.Value, account.CustomerId.Value, account.Balance, account.Status, account.OpenedAtUtc, account.ClosedAtUtc);
    }
}

/// <summary>Une écriture du registre. <paramref name="SignedPoints"/> : son effet sur le solde (négatif pour un débit).</summary>
public sealed record LoyaltyTransactionDto(
    Guid Id,
    LoyaltyTransactionType Type,
    LedgerDirection Direction,
    int Points,
    long SignedPoints,
    string? Reference,
    string? Reason,
    Guid? ReversedTransactionId,
    DateTimeOffset OccurredAtUtc)
{
    public static LoyaltyTransactionDto From(LoyaltyTransaction entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new(
            entry.Id.Value, entry.Type, entry.Direction, entry.Points.Value, entry.SignedPoints, entry.Reference?.Value, entry.Reason,
            entry.ReversedTransactionId?.Value, entry.OccurredAtUtc);
    }
}

public sealed record RewardDto(Guid Id, string Name, int PointCost, decimal DiscountAmount, string CurrencyCode, RewardStatus Status)
{
    public static RewardDto From(Reward reward)
    {
        ArgumentNullException.ThrowIfNull(reward);
        return new(reward.Id.Value, reward.Name, reward.PointCost.Value, reward.DiscountValue.Amount, reward.DiscountValue.Currency.Code, reward.Status);
    }
}

public sealed record LoyaltyProgramDto(bool IsEnabled, decimal PointsPerCurrencyUnit, bool AutoEnroll, int? InactivityExpiryDays)
{
    public static LoyaltyProgramDto From(LoyaltyProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return new(program.IsEnabled, program.PointsPerCurrencyUnit, program.AutoEnroll, program.InactivityExpiryDays);
    }
}
