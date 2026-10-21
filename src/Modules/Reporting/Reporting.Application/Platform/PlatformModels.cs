namespace Reporting.Application.Platform;

// La vue de la PLATEFORME (projection) : l'état courant de chaque restaurant et de son abonnement, recopié par l'hôte
// depuis Tenants et Billing à chaque changement. Reporting ne référence aucun de ces modules : il a ses propres statuts.

public enum PlatformTenantStatus
{
    Trial = 1,
    Active = 2,
    Suspended = 3,
    Closed = 4,
}

public enum PlatformSubscriptionStatus
{
    Trialing = 1,
    Active = 2,
    PastDue = 3,
    Suspended = 4,
    Cancelled = 5,
}

/// <summary>L'état d'un restaurant tel que Tenants le donne, à l'instant <see cref="ObservedAtUtc"/>.</summary>
public sealed record PlatformTenantSnapshot(
    TenantId TenantId, string Name, PlatformTenantStatus Status, DateTimeOffset CreatedAtUtc, DateTimeOffset ObservedAtUtc);

/// <summary>
/// L'abonnement d'un restaurant tel que Billing le donne. <see cref="MonthlyAmount"/> : prix du plan et des options actives ramené au mois
/// (annuel / 12), dans <see cref="CurrencyCode"/>.
/// </summary>
public sealed record PlatformSubscriptionSnapshot(
    TenantId TenantId,
    Guid SubscriptionId,
    Guid PlanId,
    string PlanName,
    PlatformSubscriptionStatus Status,
    decimal MonthlyAmount,
    string CurrencyCode,
    DateTimeOffset ObservedAtUtc);

/// <summary>Un restaurant et son abonnement (s'il en a un) : la liste du back-office.</summary>
public sealed record PlatformTenantRow(
    Guid TenantId,
    string Name,
    PlatformTenantStatus Status,
    DateTimeOffset CreatedAtUtc,
    PlatformSubscriptionStatus? SubscriptionStatus,
    string? PlanName,
    decimal? MonthlyAmount,
    string? CurrencyCode);

public sealed record StatusCount<TStatus>(TStatus Status, int Count)
    where TStatus : struct, Enum;

public sealed record AmountByCurrency(string CurrencyCode, decimal Amount);

/// <summary>
/// Les chiffres de la plateforme. Revenu récurrent mensuel (MRR) : abonnements actifs ou en impayé (encore facturés),
/// par devise ; jamais additionné entre devises.
/// </summary>
public sealed record PlatformSummary(
    IReadOnlyList<StatusCount<PlatformTenantStatus>> TenantsByStatus,
    int NewTenants,
    IReadOnlyList<StatusCount<PlatformSubscriptionStatus>> SubscriptionsByStatus,
    IReadOnlyList<AmountByCurrency> MonthlyRecurringRevenue);
