using Billing.Application.Abstractions;
using Billing.Domain;

namespace Billing.Application.Queries;

public sealed record AmountDto(string CurrencyCode, decimal Amount);

/// <summary>Pourquoi un abonnement demande l'attention de la plateforme.</summary>
public enum AttentionReason
{
    /// <summary>L'essai se termine.</summary>
    TrialEnding = 1,

    /// <summary>L'offre de la plateforme se termine.</summary>
    ComplimentaryEnding = 2,

    /// <summary>La résiliation programmée prend effet.</summary>
    CancellationEnding = 3,

    /// <summary>Impayé : <see cref="AttentionDto.AtUtc"/> est la date de suspension sans paiement.</summary>
    PastDue = 4,
}

/// <param name="AtUtc">L'échéance (fin d'essai, d'offre, de période) ou, pour un impayé, la suspension.</param>
/// <param name="MonthlyAmount">Ce que l'abonnement rapportera par mois (plan et options) ; null si le plan est inconnu.</param>
public sealed record AttentionDto(
    Guid SubscriptionId, Guid TenantId, string PlanName, SubscriptionStatus Status, AttentionReason Reason, DateTimeOffset AtUtc, decimal? MonthlyAmount, string CurrencyCode);

/// <summary>Encaissements d'abonnement d'un mois civil UTC (<paramref name="Month"/> au format AAAA-MM), par devise.</summary>
public sealed record MonthlyCollectionDto(string Month, string CurrencyCode, decimal Amount, int Count);

public sealed record FailedCheckoutDto(Guid TenantId, decimal Amount, string CurrencyCode, string Provider, SubscriptionCheckoutStatus Status, DateTimeOffset AtUtc);

/// <summary>
/// Les indicateurs d'abonnement du back-office.
/// <list type="bullet">
/// <item>Revenu récurrent mensuel (MRR) : abonnements actifs et impayés hors offres, plan et options ramenés au mois ;</item>
/// <item>Conversion : essais démarrés sur 90 jours, dont ceux qui ont payé au moins une fois ;</item>
/// <item>À traiter : échéances des 7 prochains jours et impayés ; règlements en ligne refusés ou expirés sur 30 jours.</item>
/// </list>
/// </summary>
public sealed record BillingOverviewDto(
    DateTimeOffset AtUtc,
    IReadOnlyList<AmountDto> MonthlyRecurringRevenue,
    IReadOnlyList<AmountDto> AverageRevenuePerSubscription,
    int Paying,
    int Trialing,
    int PastDue,
    int Suspended,
    int Complimentary,
    int ComplimentaryUnlimited,
    int CancellationsScheduled,
    int CancelledThisMonth,
    int TrialsStarted,
    int TrialsConverted,
    IReadOnlyList<AmountDto> CollectedThisMonth,
    IReadOnlyList<AmountDto> CollectedLastMonth,
    IReadOnlyList<MonthlyCollectionDto> Collections,
    IReadOnlyList<AttentionDto> Attention,
    int FailedCheckouts,
    IReadOnlyList<FailedCheckoutDto> RecentFailedCheckouts);

public sealed record GetBillingOverviewQuery : IQuery<BillingOverviewDto>;

public sealed class GetBillingOverviewHandler(BillingWorkspace workspace, IBillingOverviewReader reader, IClock clock)
    : IQueryHandler<GetBillingOverviewQuery, BillingOverviewDto>
{
    public const int Months = 6;
    public const int ConversionDays = 90;
    public const int FailedCheckoutDays = 30;
    public const int MaxAttention = 50;
    public const int MaxFailedCheckouts = 10;

    public async Task<BillingOverviewDto> HandleAsync(GetBillingOverviewQuery query, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var firstMonth = monthStart.AddMonths(-(Months - 1));
        var plans = (await workspace.Plans.ListAsync(false, cancellationToken)).ToDictionary(plan => plan.Id);
        var addOns = (await reader.ListActiveAddOnsAsync(cancellationToken)).ToLookup(entry => entry.AddOn.TenantId);
        var live = await reader.ListLiveAsync(cancellationToken);

        decimal? Monthly(SaasSubscription subscription) =>
            plans.TryGetValue(subscription.PlanId, out var plan) ? SubscriptionCharge.Monthly(plan, addOns[subscription.TenantId]).Amount : null;

        string Currency(SaasSubscription subscription) =>
            plans.TryGetValue(subscription.PlanId, out var plan) ? plan.Price.Currency.Code : string.Empty;

        var paying = live.Where(subscription => subscription.Status is SubscriptionStatus.Active or SubscriptionStatus.PastDue && !subscription.IsComplimentary).ToList();
        var mrr = paying
            .Where(subscription => plans.ContainsKey(subscription.PlanId))
            .GroupBy(Currency)
            .Select(group => (Currency: group.Key, Total: group.Sum(subscription => Monthly(subscription) ?? 0), Count: group.Count()))
            .OrderByDescending(entry => entry.Total)
            .ToList();

        var attention = new List<AttentionDto>();
        foreach (var subscription in live)
        {
            var end = subscription.CurrentPeriod.EndUtc;
            AttentionReason? reason = subscription.Status switch
            {
                SubscriptionStatus.PastDue => AttentionReason.PastDue,
                _ when end <= now || end - now > SaasSubscription.EndingNoticeWindow => null,
                _ when subscription.CancelAtPeriodEnd => AttentionReason.CancellationEnding,
                SubscriptionStatus.Trialing => AttentionReason.TrialEnding,
                SubscriptionStatus.Active when subscription.IsComplimentary => AttentionReason.ComplimentaryEnding,
                _ => null,
            };
            if (reason is { } why)
            {
                attention.Add(new AttentionDto(
                    subscription.Id.Value, subscription.TenantId.Value, plans.GetValueOrDefault(subscription.PlanId)?.Name ?? string.Empty, subscription.Status, why,
                    why == AttentionReason.PastDue ? end + workspace.Settings.PastDueGracePeriod : end, Monthly(subscription), Currency(subscription)));
            }
        }

        var payments = await reader.ListPaymentsSinceAsync(firstMonth < now.AddDays(-ConversionDays) ? firstMonth : now.AddDays(-ConversionDays), cancellationToken);
        var paidSubscriptions = payments.Select(payment => payment.SubscriptionId).ToHashSet();
        var trials = await reader.ListStartedSinceAsync(now.AddDays(-ConversionDays), cancellationToken);
        var failed = await reader.ListFailedCheckoutsSinceAsync(now.AddDays(-FailedCheckoutDays), int.MaxValue, cancellationToken);

        static IReadOnlyList<AmountDto> Sum(IEnumerable<OverviewPayment> list) =>
            list.GroupBy(payment => payment.CurrencyCode).Select(group => new AmountDto(group.Key, group.Sum(payment => payment.Amount)))
                .OrderByDescending(amount => amount.Amount).ToList();

        var collections = payments
            .Where(payment => payment.RecordedAtUtc >= firstMonth)
            .GroupBy(payment => (Month: payment.RecordedAtUtc.UtcDateTime.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture), payment.CurrencyCode))
            .Select(group => new MonthlyCollectionDto(group.Key.Month, group.Key.CurrencyCode, group.Sum(payment => payment.Amount), group.Count()))
            .OrderBy(entry => entry.Month, StringComparer.Ordinal)
            .ThenBy(entry => entry.CurrencyCode, StringComparer.Ordinal)
            .ToList();

        return new BillingOverviewDto(
            now,
            mrr.Select(entry => new AmountDto(entry.Currency, entry.Total)).ToList(),
            mrr.Select(entry => new AmountDto(entry.Currency, Math.Round(entry.Total / entry.Count, 2, MidpointRounding.AwayFromZero))).ToList(),
            paying.Count,
            live.Count(subscription => subscription.Status == SubscriptionStatus.Trialing),
            live.Count(subscription => subscription.Status == SubscriptionStatus.PastDue),
            live.Count(subscription => subscription.Status == SubscriptionStatus.Suspended),
            live.Count(subscription => subscription.IsComplimentary),
            live.Count(subscription => subscription.IsComplimentaryUnlimited),
            live.Count(subscription => subscription.CancelAtPeriodEnd),
            await reader.CountCancelledSinceAsync(monthStart, cancellationToken),
            trials.Count,
            trials.Count(trial => paidSubscriptions.Contains(trial.Id.Value)),
            Sum(payments.Where(payment => payment.RecordedAtUtc >= monthStart)),
            Sum(payments.Where(payment => payment.RecordedAtUtc >= monthStart.AddMonths(-1) && payment.RecordedAtUtc < monthStart)),
            collections,
            attention.OrderBy(entry => entry.AtUtc).Take(MaxAttention).ToList(),
            failed.Count,
            failed.Take(MaxFailedCheckouts)
                .Select(checkout => new FailedCheckoutDto(checkout.TenantId, checkout.Amount, checkout.CurrencyCode, checkout.Provider, checkout.Status, checkout.AtUtc))
                .ToList());
    }
}
