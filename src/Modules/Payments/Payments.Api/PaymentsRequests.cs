namespace Payments.Api;

public sealed record CreatedResponse(Guid Id);

/// <param name="Provider">Code d'un prestataire sur place accepté par le restaurant (cash, card_terminal...).</param>
/// <param name="Reference">N° de ticket, de transaction ou de caisse.</param>
/// <param name="Amount">Facultatif : tout le reste à payer.</param>
/// <param name="PaymentMethod">Facultatif si le prestataire n'offre qu'un moyen (Cash, CardOnSite...).</param>
/// <param name="Items">Facultatif, à la place du montant : les lignes réglées (addition partagée par article).</param>
public sealed record OfflinePaymentRequest(
    Guid OrderId,
    string? Provider,
    string? Reference,
    decimal? Amount = null,
    Payments.Domain.PaymentMethodType? PaymentMethod = null,
    IReadOnlyList<Payments.Application.Commands.PaymentItemShareInput>? Items = null);

/// <param name="PaymentMethod">Facultatif si le prestataire n'offre qu'un moyen (Card, MobileMoney...).</param>
public sealed record OnlinePaymentRequest(
    Guid OrderId,
    string? Provider,
    decimal? Amount = null,
    Payments.Domain.PaymentMethodType? PaymentMethod = null,
    string? PayerPhone = null,
    IReadOnlyList<Payments.Application.Commands.PaymentItemShareInput>? Items = null);

/// <param name="PayerPhone">Facultatif : numéro du payeur pour un prestataire qui pousse la demande sur son téléphone.</param>
public sealed record RetryPaymentRequest(string? PayerPhone = null);

public sealed record ReasonRequest(string? Reason);

/// <param name="Amount">Facultatif : tout ce qui reste remboursable.</param>
public sealed record RefundRequest(string? Reason, decimal? Amount = null);

/// <param name="Capability">Ce que le prestataire sait faire (moyens, pays, devises, remboursement, limites...).</param>
public sealed record RegisterProviderRequest(
    string? Code, Payments.Domain.PaymentProviderType Type, string? DisplayName, Payments.Application.Commands.ProviderCapabilityInput? Capability);

public sealed record UpdateProviderRequest(string? DisplayName, Payments.Application.Commands.ProviderCapabilityInput? Capability);

/// <param name="Mode">Facultatif : <c>TenantManaged</c> (compte du restaurant) par défaut.</param>
public sealed record CreateConfigurationRequest(string? Provider, Payments.Domain.PaymentMode? Mode = null);

public sealed record ModeRequest(Payments.Domain.PaymentMode Mode);

/// <param name="Method">Un moyen du prestataire (carte, mobile money...).</param>
public sealed record MethodStateRequest(Payments.Domain.PaymentMethodType Method, bool IsActive);

/// <param name="Credentials">Tous les champs demandés par le prestataire (ex. { "secret_key": "...", "webhook_secret": "..." }).</param>
public sealed record CredentialsRequest(IReadOnlyDictionary<string, string>? Credentials);

/// <param name="MinAmount">Facultatif, avec <paramref name="Currency"/> : montant minimum accepté dans l'établissement.</param>
public sealed record LocationAssignmentRequest(
    Payments.Domain.PaymentMethodType Method, int Priority = 0, decimal? MinAmount = null, decimal? MaxAmount = null, string? Currency = null, bool IsEnabled = true);

/// <param name="OpeningFloat">Espèces mises dans le tiroir (0 possible).</param>
public sealed record OpenCashSessionRequest(Guid LocationId, decimal OpeningFloat);

public sealed record CashMovementRequest(Payments.Domain.CashMovementKind Kind, decimal Amount, string? Reason);

/// <param name="Counted">Montant compté par moyen sur place (espèces comprises, sauf si les coupures sont données).</param>
/// <param name="Denominations">Facultatif : comptage des espèces coupure par coupure.</param>
public sealed record CloseCashSessionRequest(
    IReadOnlyList<Payments.Application.CountedAmount>? Counted, IReadOnlyList<Payments.Application.DenominationInput>? Denominations, string? Note);

/// <summary>Acompte reçu par l'équipe : le montant est celui de la réservation, jamais saisi.</summary>
public sealed record OfflineDepositRequest(string? Provider, string? Reference, Payments.Domain.PaymentMethodType? PaymentMethod = null);

public sealed record ApplyDepositRequest(Guid OrderId);
