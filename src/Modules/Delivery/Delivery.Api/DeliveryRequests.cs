using Delivery.Domain;

namespace Delivery.Api;

public sealed record CreatedResponse(Guid Id);

/// <summary>Délai annoncé en minutes (0 à 240).</summary>
public sealed record ServiceSettingsRequest(bool Enabled, int LeadMinutes);

/// <summary>Montants dans la devise du restaurant ; délai de trajet de 5 à 240 minutes.</summary>
public sealed record DeliveryZoneRequest(string? Name, decimal Fee, decimal MinimumOrder, int EstimatedMinutes);

/// <summary>Téléphone au format international (E.164) ; heure de retrait souhaitée facultative.</summary>
public sealed record CreatePickupRequest(Guid OrderId, string? CustomerName, string? CustomerPhone = null, DateTimeOffset? PickupAt = null);

public sealed record CreateDeliveryRequest(
    Guid OrderId,
    Guid ZoneId,
    string? CustomerName,
    string? CustomerPhone,
    string? AddressLine,
    string? Landmark = null,
    string? Instructions = null);

/// <summary>Motif obligatoire pour un échec, facultatif pour une annulation.</summary>
public sealed record ReasonRequest(string? Reason);

/// <summary>Un livreur enregistré (<paramref name="CourierId"/>) ou saisi à la main (nom, téléphone facultatif).</summary>
public sealed record CourierRequest(string? Name = null, string? Phone = null, Guid? CourierId = null);

/// <summary>Ce que le client a payé au livreur, quand il restait à payer : montant (tout le dû si omis) et mode.</summary>
public sealed record DeliveredRequest(decimal? Collected = null, CollectionMethod? Method = null);

/// <summary>Tarif par course facultatif, dans la devise du restaurant.</summary>
public sealed record CourierProfileRequest(string? Name, string? Phone, CourierKind Kind, decimal? FeePerDelivery = null);

/// <summary>
/// Les courses soldées (même livreur, même établissement) et l'argent qui a changé de main : positif, remis par le
/// livreur ; négatif, payé au livreur.
/// </summary>
public sealed record RemittanceRequest(IReadOnlyList<Guid>? FulfillmentIds, decimal Counted, string? Note = null);

public sealed record ContactRequest(string? CustomerName, string? CustomerPhone);

public sealed record AddressRequest(string? AddressLine, string? Landmark = null, string? Instructions = null);
