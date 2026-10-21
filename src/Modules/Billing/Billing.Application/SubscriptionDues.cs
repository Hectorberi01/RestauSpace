using Billing.Domain;

namespace Billing.Application;

/// <summary>La période que paierait un paiement maintenant, et son application à l'abonnement.</summary>
public static class SubscriptionDues
{
    /// <summary>
    /// <list type="bullet">
    /// <item>essai en cours : la première période payée suit l'essai (conversion anticipée) ;</item>
    /// <item>essai fini, impayé, suspendu : la période payée part de la fin de la période courante si elle contient
    /// maintenant, sinon de maintenant (pas de trou rétroactif) ;</item>
    /// <item>actif : la période suivante, une fois la période courante échue ; avant, null (rien à payer).</item>
    /// </list>
    /// </summary>
    public static Period? NextPaidPeriod(SaasSubscription subscription, Plan plan, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(plan);
        var current = subscription.CurrentPeriod;
        var followsCurrent = BillingPeriods.Starting(current.EndUtc, plan.BillingInterval);
        if (subscription.Status == SubscriptionStatus.Active)
        {
            return current.HasEndedAt(nowUtc) ? followsCurrent : null;
        }

        return subscription.Status == SubscriptionStatus.Trialing && !current.HasEndedAt(nowUtc)
            ? followsCurrent
            : followsCurrent.Contains(nowUtc) ? followsCurrent : BillingPeriods.Starting(nowUtc, plan.BillingInterval);
    }

    public static void Apply(SaasSubscription subscription, Period paid, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        if (subscription.Status == SubscriptionStatus.Active)
        {
            subscription.RenewPeriod(paid, clock);
        }
        else
        {
            subscription.Activate(paid, clock);
        }
    }
}
