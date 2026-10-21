using Reporting.Application.Abstractions;
using Reporting.Application.Platform;

namespace Reporting.Infrastructure.Persistence;

/// <summary>
/// PLATEFORME : l'état courant des restaurants et de leurs abonnements. Écriture par upsert SQL ; un instantané plus
/// ancien que celui stocké (deux messages traités dans le désordre) ne remplace rien.
/// </summary>
internal sealed class PlatformDirectory(ReportingDbContext context) : IPlatformDirectory
{
    public async Task UpsertTenantAsync(PlatformTenantSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var (tenant, name, status, created, observed) =
            (snapshot.TenantId.Value, snapshot.Name, (int)snapshot.Status, snapshot.CreatedAtUtc, snapshot.ObservedAtUtc);
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO reporting.platform_tenants (tenant_id, name, status, created_at_utc, observed_at_utc)
            VALUES ({tenant}, {name}, {status}, {created}, {observed})
            ON CONFLICT (tenant_id) DO UPDATE
            SET name = EXCLUDED.name, status = EXCLUDED.status, created_at_utc = EXCLUDED.created_at_utc, observed_at_utc = EXCLUDED.observed_at_utc
            WHERE platform_tenants.observed_at_utc <= EXCLUDED.observed_at_utc
            """,
            cancellationToken);
    }

    public async Task UpsertSubscriptionAsync(PlatformSubscriptionSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var (tenant, subscription, plan, planName, status, amount, currency, observed) = (
            snapshot.TenantId.Value, snapshot.SubscriptionId, snapshot.PlanId, snapshot.PlanName, (int)snapshot.Status,
            snapshot.MonthlyAmount, snapshot.CurrencyCode, snapshot.ObservedAtUtc);
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO reporting.platform_subscriptions
                (tenant_id, subscription_id, plan_id, plan_name, status, monthly_amount, currency_code, observed_at_utc)
            VALUES ({tenant}, {subscription}, {plan}, {planName}, {status}, {amount}, {currency}, {observed})
            ON CONFLICT (tenant_id) DO UPDATE
            SET subscription_id = EXCLUDED.subscription_id, plan_id = EXCLUDED.plan_id, plan_name = EXCLUDED.plan_name,
                status = EXCLUDED.status, monthly_amount = EXCLUDED.monthly_amount, currency_code = EXCLUDED.currency_code,
                observed_at_utc = EXCLUDED.observed_at_utc
            WHERE platform_subscriptions.observed_at_utc <= EXCLUDED.observed_at_utc
            """,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PlatformTenantRow>> ListTenantsAsync(
        PlatformTenantStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var tenants = context.PlatformTenants.AsNoTracking();
        if (status is { } wanted)
        {
            tenants = tenants.Where(tenant => tenant.Status == wanted);
        }

        var page = await tenants.OrderBy(tenant => tenant.Name).ThenBy(tenant => tenant.TenantId).Skip(skip).Take(take).ToListAsync(cancellationToken);
        var ids = page.Select(tenant => tenant.TenantId).ToArray();
        var subscriptions = await context.PlatformSubscriptions.AsNoTracking()
            .Where(subscription => ids.Contains(subscription.TenantId))
            .ToDictionaryAsync(subscription => subscription.TenantId, cancellationToken);

        return page.Select(tenant =>
        {
            var subscription = subscriptions.GetValueOrDefault(tenant.TenantId);
            return new PlatformTenantRow(
                tenant.TenantId, tenant.Name, tenant.Status, tenant.CreatedAtUtc,
                subscription?.Status, subscription?.PlanName, subscription?.MonthlyAmount, subscription?.CurrencyCode);
        }).ToList();
    }

    public async Task<PlatformSummary> SummarizeAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var tenantsByStatus = await context.PlatformTenants.AsNoTracking()
            .GroupBy(tenant => tenant.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var newTenants = await context.PlatformTenants.AsNoTracking()
            .CountAsync(tenant => tenant.CreatedAtUtc >= fromUtc && tenant.CreatedAtUtc < toUtc, cancellationToken);
        var subscriptionsByStatus = await context.PlatformSubscriptions.AsNoTracking()
            .GroupBy(subscription => subscription.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var recurring = await context.PlatformSubscriptions.AsNoTracking()
            .Where(subscription => subscription.Status == PlatformSubscriptionStatus.Active || subscription.Status == PlatformSubscriptionStatus.PastDue)
            .GroupBy(subscription => subscription.CurrencyCode)
            .Select(group => new { Currency = group.Key, Amount = group.Sum(subscription => subscription.MonthlyAmount) })
            .ToListAsync(cancellationToken);

        return new PlatformSummary(
            tenantsByStatus.OrderBy(row => row.Status).Select(row => new StatusCount<PlatformTenantStatus>(row.Status, row.Count)).ToList(),
            newTenants,
            subscriptionsByStatus.OrderBy(row => row.Status).Select(row => new StatusCount<PlatformSubscriptionStatus>(row.Status, row.Count)).ToList(),
            recurring.OrderBy(row => row.Currency, StringComparer.Ordinal).Select(row => new AmountByCurrency(row.Currency, row.Amount)).ToList());
    }
}
