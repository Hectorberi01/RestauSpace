namespace Billing.Domain;

/// <summary>
/// Un paiement d'abonnement : la période qu'il couvre, le plan et les options À CE MOMENT (noms et montants recopiés
/// ligne par ligne, un changement de prix ultérieur ne réécrit pas l'historique). Le montant est la somme des lignes.
/// Immuable, sauf son numéro de facture, donné une seule fois à l'enregistrement (suite sans trou, par année).
/// </summary>
public sealed class SubscriptionPayment : AggregateRoot<SubscriptionPaymentId>, ITenantOwned
{
    private readonly List<SubscriptionPaymentLine> _lines = [];

    private SubscriptionPayment(
        SubscriptionPaymentId id, 
        TenantId tenantId, 
        SubscriptionId subscriptionId, 
        PlanId planId, 
        string planName, 
        Money amount,
        Period paidPeriod, 
        DateTimeOffset recordedAtUtc): base(id)
    {
        TenantId = tenantId;
        SubscriptionId = subscriptionId;
        PlanId = planId;
        PlanName = planName;
        Amount = amount;
        PaidPeriod = paidPeriod;
        RecordedAtUtc = recordedAtUtc;
    }

    // Réservé à l'ORM.
    private SubscriptionPayment()
    {
        PlanName = null!;
        Amount = null!;
        PaidPeriod = null!;
    }

    public TenantId TenantId { get; }

    public SubscriptionId SubscriptionId { get; }

    public PlanId PlanId { get; }

    public string PlanName { get; }

    public Money Amount { get; }

    public Period PaidPeriod { get; }

    public DateTimeOffset RecordedAtUtc { get; }

    /// <summary>Prestataire qui a encaissé en ligne (« fedapay »…) ; null pour un paiement enregistré par la plateforme.</summary>
    public string? CollectedBy { get; private set; }

    /// <summary>Numéro de facture (« F2026-000042 ») ; null tant que le paiement n'est pas enregistré.</summary>
    public string? InvoiceNumber { get; private set; }

    /// <summary>Le détail : le plan de base puis les options. Vide pour un paiement enregistré avant les options.</summary>
    public IReadOnlyCollection<SubscriptionPaymentLine> Lines => _lines.AsReadOnly();

    /// <summary>Le paiement de la période désormais payée de <paramref name="subscription"/>, au prix de <paramref name="plan"/>.</summary>
    public static SubscriptionPayment Record(SaasSubscription subscription, Plan plan, IClock clock) => Record(subscription, plan, [], clock);

    /// <summary>
    /// Le paiement de la période désormais payée : le prix de <paramref name="plan"/> et de chaque option active de
    /// <paramref name="addOns"/> (voir <see cref="SubscriptionCharge"/>).
    /// </summary>
    public static SubscriptionPayment Record(
        SaasSubscription subscription, Plan plan, IEnumerable<(TenantAddOn AddOn, Plan Plan)> addOns, IClock clock) =>
        Record(subscription, plan, addOns, collectedBy: null, clock);

    /// <summary>Comme ci-dessus ; <paramref name="collectedBy"/> : le prestataire qui a encaissé en ligne.</summary>
    public static SubscriptionPayment Record(
        SaasSubscription subscription, Plan plan, IEnumerable<(TenantAddOn AddOn, Plan Plan)> addOns, string? collectedBy, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(addOns);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(subscription.PlanId != plan.Id, SubscriptionPaymentErrors.PlanMismatch);
        DomainException.ThrowIf(subscription.Status != SubscriptionStatus.Active, SubscriptionPaymentErrors.SubscriptionNotActive);
        var options = addOns.ToList();
        DomainException.ThrowIf(options.Any(item => item.AddOn.TenantId != subscription.TenantId), SubscriptionPaymentErrors.AddOnOfAnotherTenant);

        var now = clock.UtcNow;
        var lines = SubscriptionCharge.Lines(plan, options);
        var total = SubscriptionCharge.Total(plan, lines);
        var payment = new SubscriptionPayment(
            SubscriptionPaymentId.New(), subscription.TenantId, subscription.Id, plan.Id, plan.Name, total, subscription.CurrentPeriod, now);
        payment._lines.AddRange(lines);
        payment.CollectedBy = string.IsNullOrWhiteSpace(collectedBy) ? null : collectedBy.Trim();
        payment.Raise(new SubscriptionPaymentRecorded(
            subscription.TenantId, payment.Id, subscription.Id, plan.Id, total, subscription.CurrentPeriod, now));
        return payment;
    }

    /// <summary>Le numéro de facture, donné par la persistance dans la transaction qui enregistre le paiement.</summary>
    public void AssignInvoiceNumber(string number)
    {
        DomainException.ThrowIf(InvoiceNumber is not null, SubscriptionPaymentErrors.InvoiceNumberAlreadySet);
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(number) || number.Length > MaxInvoiceNumberLength, SubscriptionPaymentErrors.InvoiceNumberInvalid);
        InvoiceNumber = number;
    }

    public const int MaxInvoiceNumberLength = 20;

    /// <summary>« F » suivi de l'année, puis le rang dans l'année sur six chiffres.</summary>
    public static string FormatInvoiceNumber(int year, long rank) =>
        $"F{year.ToString(System.Globalization.CultureInfo.InvariantCulture)}-{rank.ToString("000000", System.Globalization.CultureInfo.InvariantCulture)}";
}

public static class SubscriptionPaymentErrors
{
    public static readonly DomainError InvoiceNumberAlreadySet =
        new("billing.subscription_payment.invoice_number_already_set", "An invoice number is given once.");

    public static readonly DomainError InvoiceNumberInvalid =
        new("billing.subscription_payment.invoice_number_invalid", "The invoice number is invalid.");

    public static readonly DomainError PlanMismatch =
        new("billing.subscription_payment.plan_mismatch", "The payment must be recorded at the price of the subscription's plan.");

    public static readonly DomainError AddOnOfAnotherTenant =
        new("billing.subscription_payment.add_on_of_another_tenant", "Only the restaurant's own add-ons are billed with its subscription.");

    public static readonly DomainError SubscriptionNotActive =
        new("billing.subscription_payment.subscription_not_active", "A payment is recorded once the subscription is active.");
}

public sealed record SubscriptionPaymentRecorded(
    TenantId TenantId,
    SubscriptionPaymentId PaymentId,
    SubscriptionId SubscriptionId,
    PlanId PlanId,
    Money Amount,
    Period PaidPeriod,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
