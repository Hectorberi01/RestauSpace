namespace Reservations.Domain;

/// <summary>
/// Ce qu'une réservation doit savoir du client CRM pour lequel elle est prise (module Customers). Construit par
/// l'Application à partir du client chargé.
/// </summary>
public sealed record CustomerReference(TenantId TenantId, CustomerId CustomerId, bool IsAnonymized)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public CustomerId CustomerId { get; } = Guard.NotEmpty(CustomerId);

    /// <summary>Données personnelles du client effacées : rien de nouveau ne peut être rattaché à cette fiche.</summary>
    public bool IsAnonymized { get; } = IsAnonymized;
}
