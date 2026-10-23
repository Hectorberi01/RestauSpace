namespace Billing.Application;

/// <summary>Erreurs levées par les cas d'usage Billing (les erreurs des règles métier sont dans Billing.Domain).</summary>
public static class BillingApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("billing.concurrent_update", "The resource was changed by someone else; reload it and try again.", DomainErrorKind.Conflict);

    public static readonly DomainError TenantNotWritable =
        new("billing.tenant.not_writable", "The restaurant account is suspended or closed: ask the platform to restore it.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError PlanNotFound = new("billing.plan.not_found", "The plan does not exist.");
    public static readonly DomainError SubscriptionNotFound = new("billing.subscription.not_found", "There is no such subscription.");
    public static readonly DomainError TenantNotFound = new("billing.tenant.not_found", "The restaurant does not exist.");
    public static readonly DomainError AddOnNotFound = new("billing.add_on.not_found", "This add-on does not exist for this restaurant.");
    public static readonly DomainError AddOnNeedsSubscription =
        new("billing.add_on.subscription_required", "The restaurant needs a live subscription before an add-on can be added.");

    public static readonly DomainError AddOnNeedsOpenSubscription =
        new("billing.add_on.subscription_not_open", "Add-ons are changed during the trial or a paid period: settle the subscription first.");

    public static readonly DomainError NothingToPay =
        new("billing.subscription.nothing_to_pay", "The current period is already paid; record the payment once it has ended.");
    public static readonly DomainError TrialLengthInvalid = new("billing.subscription.trial_length_invalid", "A trial lasts 1 to 90 days.");
    public static readonly DomainError FreePlanUnavailable =
        new("billing.subscription.free_plan_unavailable", "The free plan is only available once, during the first month.");
    public static readonly DomainError PageInvalid = new("billing.page_invalid", "Skip must be zero or more and take between 1 and 100.");
    public static readonly DomainError PlanCurrencyMismatch =
        new("billing.plan.currency_mismatch", "The plan is not sold in the restaurant's currency.");
    public static readonly DomainError OnlinePaymentUnavailable =
        new("billing.online_payment.unavailable", "Online payment of the subscription is not available yet: contact the platform.");
    public static readonly DomainError CancellationScheduled =
        new("billing.online_payment.cancellation_scheduled", "The subscription ends at the end of its period: withdraw the cancellation before paying.");
    public static readonly DomainError CheckoutNotFound = new("billing.checkout.not_found", "This payment does not exist.");
    public static readonly DomainError InvoiceNotFound = new("billing.invoice.not_found", "This invoice does not exist.");
    public static readonly DomainError PeriodInvalid = new("billing.period_invalid", "The end of the period must come after its start.");
    public static readonly DomainError OnlinePaymentProviderNotOffered =
        new("billing.online_payment.provider_not_offered", "This payment provider is not offered for this currency.");

    public static readonly DomainError OnlinePaymentProviderNotReady =
        new("billing.online_payment.provider_not_ready", "This provider cannot collect in this currency yet: enable it, allow the currency and save the platform account.");
    public static readonly DomainError CurrencyInvalid = new("billing.currency_invalid", "Unknown currency.");
    public static readonly DomainError AddOnComplimentaryUnlimited =
        new("billing.add_on.complimentary_unlimited", "The plan is offered without end date: ask the platform to add options.");
}

/// <summary>
/// Codes de permission déclarés par le module Billing pour les restaurants (accordés via les rôles Staff). Le catalogue
/// des plans et la gestion des abonnements de tous les restaurants relèvent de la plateforme (policy d'administration).
/// </summary>
public static class BillingPermissions
{
    /// <summary>Voir l'abonnement du restaurant, son plan, ses droits et sa consommation.</summary>
    public const string View = "billing.view";

    /// <summary>Choisir un plan (essai), en changer, programmer ou annuler la résiliation.</summary>
    public const string Manage = "billing.manage";

    public static IReadOnlyList<string> All { get; } = [View, Manage];
}

/// <summary>Réglages des cas d'usage (section <c>Billing</c> de l'hôte).</summary>
public sealed class BillingSettings
{
    /// <summary>Durée de l'essai offert quand un restaurant choisit son plan.</summary>
    public TimeSpan TrialLength { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Durée du plan gratuit (Découverte), une seule fois par restaurant : son premier mois.</summary>
    public int FreePlanMonths { get; set; } = 1;

    /// <summary>Délai entre l'échéance impayée (PastDue) et la suspension pour impayé.</summary>
    public TimeSpan PastDueGracePeriod { get; set; } = TimeSpan.FromDays(7);
}
