namespace Billing.Domain;

/// <summary>
/// Une ligne de ce que paie une période d'abonnement : le plan de base, ou une option active (au prix de son plan
/// d'option, multiplié par la quantité). Montants recopiés au moment du paiement, dans la devise du paiement.
/// </summary>
public sealed class SubscriptionPaymentLine : ValueObject
{
    public const int MaxLabelLength = Plan.MaxNameLength;

    private SubscriptionPaymentLine(PlanId planId, PlanKind kind, string label, int quantity, decimal unitAmount, decimal amount)
    {
        PlanId = planId;
        Kind = kind;
        Label = label;
        Quantity = quantity;
        UnitAmount = unitAmount;
        Amount = amount;
    }

    // Réservé à l'ORM.
    private SubscriptionPaymentLine()
    {
        Label = null!;
    }

    public PlanId PlanId { get; }

    public PlanKind Kind { get; }

    public string Label { get; }

    public int Quantity { get; }

    /// <summary>Prix unitaire pour la période payée (une option mensuelle sur un plan annuel compte 12 mois).</summary>
    public decimal UnitAmount { get; }

    public decimal Amount { get; }

    internal static SubscriptionPaymentLine Of(Plan plan, int quantity, Money unit) =>
        new(plan.Id, plan.Kind, plan.Name, quantity, unit.Amount, unit.Multiply(quantity).Amount);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return PlanId;
        yield return Kind;
        yield return Label;
        yield return Quantity;
        yield return UnitAmount;
        yield return Amount;
    }
}

/// <summary>
/// Ce que coûte un abonnement : son plan de base et ses options actives. Une option est facturée sur l'intervalle du
/// plan de base : une option mensuelle sur un plan annuel compte 12 mois, une option annuelle sur un plan mensuel un
/// douzième (arrondi à la devise). Une option dans une autre devise que le plan n'est jamais facturée ; une option offerte
/// figure à 0.
/// </summary>
public static class SubscriptionCharge
{
    public static IReadOnlyList<SubscriptionPaymentLine> Lines(Plan basePlan, IEnumerable<(TenantAddOn AddOn, Plan Plan)> addOns)
    {
        ArgumentNullException.ThrowIfNull(basePlan);
        ArgumentNullException.ThrowIfNull(addOns);
        var lines = new List<SubscriptionPaymentLine> { SubscriptionPaymentLine.Of(basePlan, 1, basePlan.Price) };
        foreach (var (addOn, plan) in addOns.Where(item => item.AddOn.IsActive && item.Plan.Price.Currency == basePlan.Price.Currency).OrderBy(item => item.Plan.Name, StringComparer.Ordinal))
        {
            // Option offerte par la plateforme : ligne à 0.
            var ratio = addOn.IsComplimentary ? 0m : Months(basePlan.BillingInterval) / (decimal)Months(plan.BillingInterval);
            lines.Add(SubscriptionPaymentLine.Of(plan, addOn.Quantity, plan.Price.Multiply(ratio)));
        }

        return lines;
    }

    public static Money Total(Plan basePlan, IReadOnlyList<SubscriptionPaymentLine> lines)
    {
        ArgumentNullException.ThrowIfNull(basePlan);
        ArgumentNullException.ThrowIfNull(lines);
        return Money.Of(lines.Sum(line => line.Amount), basePlan.Price.Currency);
    }

    /// <summary>Le montant ramené au mois (revenu récurrent) : plan et options, chacun selon son intervalle.</summary>
    public static Money Monthly(Plan basePlan, IEnumerable<(TenantAddOn AddOn, Plan Plan)> addOns)
    {
        ArgumentNullException.ThrowIfNull(basePlan);
        ArgumentNullException.ThrowIfNull(addOns);
        var total = basePlan.Price.Amount / Months(basePlan.BillingInterval);
        total += addOns
            .Where(item => item.AddOn.IsActive && !item.AddOn.IsComplimentary && item.Plan.Price.Currency == basePlan.Price.Currency)
            .Sum(item => item.Plan.Price.Amount * item.AddOn.Quantity / Months(item.Plan.BillingInterval));
        return Money.Rounded(total, basePlan.Price.Currency);
    }

    private static int Months(BillingInterval interval) => interval == BillingInterval.Yearly ? 12 : 1;
}
