namespace Loyalty.Domain;

public sealed record LoyaltyAccountOpened(
    TenantId TenantId,
    LoyaltyAccountId LoyaltyAccountId,
    CustomerId CustomerId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PointsEarned(
    TenantId TenantId,
    LoyaltyAccountId LoyaltyAccountId,
    CustomerId CustomerId,
    LoyaltyTransactionId TransactionId,
    Points Points,
    TransactionReference Reference,
    long BalanceAfter,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PointsRedeemed(
    TenantId TenantId,
    LoyaltyAccountId LoyaltyAccountId,
    CustomerId CustomerId,
    LoyaltyTransactionId TransactionId,
    Points Points,
    TransactionReference Reference,
    RewardId? RewardId,
    long BalanceAfter,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LoyaltyAdjusted(
    TenantId TenantId,
    LoyaltyAccountId LoyaltyAccountId,
    CustomerId CustomerId,
    LoyaltyTransactionId TransactionId,
    Points Points,
    LedgerDirection Direction,
    string Reason,
    long BalanceAfter,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PointsExpired(
    TenantId TenantId,
    LoyaltyAccountId LoyaltyAccountId,
    CustomerId CustomerId,
    LoyaltyTransactionId TransactionId,
    Points Points,
    long BalanceAfter,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record LoyaltyAccountSuspended(TenantId TenantId, LoyaltyAccountId LoyaltyAccountId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record LoyaltyAccountReactivated(TenantId TenantId, LoyaltyAccountId LoyaltyAccountId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <summary>
/// Une récompense a été échangée contre des points. Porte ce dont l'Application a besoin pour appliquer la remise à la
/// commande : le montant et la référence de la remise (l'id de la récompense en chaîne, 36 caractères).
/// </summary>
public sealed record RewardRedeemed(
    TenantId TenantId,
    LoyaltyAccountId LoyaltyAccountId,
    CustomerId CustomerId,
    LoyaltyTransactionId TransactionId,
    RewardId RewardId,
    Points Points,
    Money DiscountValue,
    string DiscountReference,
    TransactionReference Reference,
    long BalanceAfter,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PointsEarnReversed(
    TenantId TenantId,
    LoyaltyAccountId LoyaltyAccountId,
    CustomerId CustomerId,
    LoyaltyTransactionId TransactionId,
    LoyaltyTransactionId ReversedTransactionId,
    Points Points,
    TransactionReference Reference,
    string Reason,
    long BalanceAfter,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record PointsRedeemReversed(
    TenantId TenantId,
    LoyaltyAccountId LoyaltyAccountId,
    CustomerId CustomerId,
    LoyaltyTransactionId TransactionId,
    LoyaltyTransactionId ReversedTransactionId,
    Points Points,
    TransactionReference Reference,
    string Reason,
    long BalanceAfter,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary><paramref name="ForfeitedBalance"/> est le solde signé perdu à la clôture (0 s'il était vide).</summary>
public sealed record LoyaltyAccountClosed(
    TenantId TenantId,
    LoyaltyAccountId LoyaltyAccountId,
    CustomerId CustomerId,
    long ForfeitedBalance,
    string Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
