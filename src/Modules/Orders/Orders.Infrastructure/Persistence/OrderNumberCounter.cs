namespace Orders.Infrastructure.Persistence;

/// <summary>
/// Dernier numéro tiré pour un établissement et un jour. Jamais lu par EF : seulement incrémenté par
/// <see cref="OrderNumberGenerator"/> en une instruction atomique. Déclaré ici pour que la migration crée la table.
/// </summary>
internal sealed class OrderNumberCounter : ITenantOwned
{
    public const string Table = "order_number_counters";

    public TenantId TenantId { get; set; }

    public LocationId LocationId { get; set; }

    public DateOnly Day { get; set; }

    public int LastValue { get; set; }
}
