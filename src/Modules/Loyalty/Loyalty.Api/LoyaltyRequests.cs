using Loyalty.Domain;

namespace Loyalty.Api;

public sealed record CreatedResponse(Guid Id);

public sealed record OpenAccountRequest(Guid CustomerId);

/// <param name="Reference">Facultatif : clé de rejeu (un même ajustement n'est posté qu'une fois).</param>
public sealed record AdjustPointsRequest(int Points, LedgerDirection Direction, string? Reason, string? Reference = null);

public sealed record ReasonRequest(string? Reason);

public sealed record RedeemRewardRequest(Guid OrderId);

public sealed record CreateRewardRequest(string? Name, int PointCost, decimal DiscountAmount, string? CurrencyCode);

public sealed record RewardCostRequest(int PointCost);

public sealed record RewardDiscountValueRequest(decimal DiscountAmount, string? CurrencyCode);

public sealed record LoyaltyProgramRequest(bool IsEnabled, decimal PointsPerCurrencyUnit, bool AutoEnroll, int? InactivityExpiryDays);

public sealed record IssueStoredValueCardRequest(
    StoredValueKind Kind, decimal Amount, string? CurrencyCode, DateTimeOffset? ExpiresAtUtc = null, Guid? CustomerId = null, string? Note = null);

public sealed record StoredValueLookupRequest(string? Code);
