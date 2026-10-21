namespace Notifications.Domain;

/// <summary>
/// Un établissement (module Locations), construit par l'Application à partir de l'établissement chargé sous isolation par
/// tenant, jamais à partir d'une saisie utilisateur ; <see cref="Country"/> sert à choisir un prestataire qui y délivre.
/// </summary>
public sealed record LocationReference(TenantId TenantId, LocationId LocationId, CountryCode Country)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public LocationId LocationId { get; } = Guard.NotEmpty(LocationId);

    public CountryCode Country { get; } = Country ?? throw new ArgumentNullException(nameof(Country));
}
