using Billing.Application;
using Billing.Application.Queries;
using Locations.Application.Abstractions;
using Orders.Application.Abstractions;
using SaasRestaurant.BuildingBlocks.Application;
using Staff.Application.Abstractions;

namespace SaasRestaurant.Host;

/// <summary>
/// Orders, Locations, Staff -> Billing : les plafonds du plan du restaurant (commandes passées par mois, établissements,
/// personnel). Null = aucun plafond ; 0 = fonctionnalité désactivée par le plan.
/// <list type="bullet">
/// <item>Restaurant sans abonnement vivant (création par la plateforme, avant le premier abonnement) : aucun plafond.
/// Un abonnement résilié suspend de toute façon le restaurant (saga Billing -> Tenants), ce qui bloque ses écritures.</item>
/// <item>Tenant demandé différent de celui du contexte (action de la plateforme, job) : aucun plafond. Une requête
/// d'un restaurant a toujours son propre tenant dans le contexte : elle ne peut pas y échapper.</item>
/// </list>
/// </summary>
internal sealed class PlanLimitsFromBilling(ITenantContext tenantContext, IQueryHandler<GetFeatureLimitQuery, FeatureLimitDto> limits)
    : IOrderPlanLimits, ILocationPlanLimits, IStaffPlanLimits
{
    public Task<long?> MonthlyOrdersAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        LimitAsync(tenantId, BillingFeatures.OrdersMonthly, cancellationToken);

    public Task<long?> MaxLocationsAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        LimitAsync(tenantId, BillingFeatures.Locations, cancellationToken);

    public Task<long?> MaxStaffMembersAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        LimitAsync(tenantId, BillingFeatures.StaffMembers, cancellationToken);

    private async Task<long?> LimitAsync(TenantId tenantId, string feature, CancellationToken cancellationToken)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        var limit = await limits.HandleAsync(new GetFeatureLimitQuery(feature), cancellationToken);
        if (!limit.HasLiveSubscription)
        {
            return null;
        }

        return limit.Enabled ? limit.Limit : 0;
    }
}

/// <summary>
/// Modules -> Billing : la porte des fonctionnalités du plan (plan et options). Fail-closed : tenant autre que celui du
/// contexte, ou restaurant sans abonnement vivant, n'a droit à rien.
/// </summary>
internal sealed class PlanFeatureGateFromBilling(ITenantContext tenantContext, IQueryHandler<GetFeatureLimitQuery, FeatureLimitDto> limits)
    : IPlanFeatureGate
{
    public async Task<bool> AllowsAsync(TenantId tenantId, string feature, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId || string.IsNullOrWhiteSpace(feature))
        {
            return false;
        }

        var limit = await limits.HandleAsync(new GetFeatureLimitQuery(feature), cancellationToken);
        return limit.HasLiveSubscription && limit.Enabled;
    }
}
