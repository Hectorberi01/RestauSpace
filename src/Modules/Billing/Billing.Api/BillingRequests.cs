using Billing.Application.Commands;
using Billing.Domain;

namespace Billing.Api;

public sealed record CreatedResponse(Guid Id);

/// <summary>Le restaurant choisit un plan (essai) ou en change.</summary>
public sealed record ChoosePlanRequest(Guid PlanId);

/// <param name="PayerPhone">Numéro pour un paiement poussé sur le téléphone (selon le prestataire). Jamais conservé.</param>
/// <param name="Provider">Le prestataire choisi parmi ceux proposés ; absent : le premier.</param>
public sealed record StartCheckoutRequest(string? PayerPhone, string? Provider = null)
{
    public override string ToString() => "StartCheckoutRequest (***)";
}

public sealed record CreatePlanRequest(
    string? Name,
    decimal Price,
    string? CurrencyCode,
    BillingInterval BillingInterval,
    IReadOnlyList<EntitlementInput>? Entitlements = null,
    PlanKind Kind = PlanKind.Base);

public sealed record AdminActivateAddOnRequest(Guid PlanId, int? Quantity = null);

public sealed record SubscribeAddOnRequest(Guid PlanId, int? Quantity = null);

public sealed record AddOnQuantityRequest(int Quantity);

public sealed record PlanPriceRequest(decimal Price);

/// <param name="TrialDays">Facultatif : 1 à 90 jours (défaut : le réglage Billing:TrialLength).</param>
public sealed record AdminStartSubscriptionRequest(Guid PlanId, int? TrialDays = null);

public sealed record SuspendSubscriptionRequest(SuspensionReason Reason);

/// <param name="UntilUtc">Fin de l'offre ; absent ou null : sans fin.</param>
/// <param name="RenewalMonths">Renouvellement automatique de l'offre (1 à 12 mois) ; null : aucun.</param>
public sealed record GrantComplimentaryRequest(Guid PlanId, DateTimeOffset? UntilUtc = null, int? RenewalMonths = null);

/// <param name="Then">À la fin de l'offre : <c>Cancel</c> (arrêt) ou <c>Bill</c> (payante à la période suivante).</param>
public sealed record OfferAddOnRequest(
    Guid PlanId, int? Quantity = null, DateTimeOffset? UntilUtc = null, int? RenewalMonths = null, AddOnAfterOffer Then = AddOnAfterOffer.Cancel);

/// <param name="Providers">Codes des prestataires proposés, dans l'ordre ; vide : retour au réglage de l'hôte.</param>
/// <param name="Provider">Ancienne forme, un seul prestataire (ignorée si <paramref name="Providers"/> est donné).</param>
public sealed record OnlinePaymentRouteRequest(IReadOnlyList<string>? Providers = null, string? Provider = null)
{
    public IReadOnlyList<string> All => Providers ?? (string.IsNullOrWhiteSpace(Provider) ? [] : [Provider]);
}
