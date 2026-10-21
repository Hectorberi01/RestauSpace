namespace Locations.Application.Abstractions;

/// <summary>
/// Ce que Locations doit savoir de son tenant, servi par le host depuis le module Tenants : le fuseau horaire par
/// défaut d'un nouvel établissement, et si le tenant accepte les écritures métier (pas un tenant suspendu ou fermé).
/// </summary>
public interface ITenantDirectory
{
    /// <summary>Null quand le tenant n'existe pas.</summary>
    Task<TenantSettings?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

public sealed record TenantSettings(TimeZoneId DefaultTimeZone, bool AcceptsBusinessWrites);
