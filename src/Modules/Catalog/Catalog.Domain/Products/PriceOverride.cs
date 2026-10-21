namespace Catalog.Domain;

/// <summary>Où un produit est vendu, pour son prix : en salle et au comptoir, ou par chacun des canaux en ligne.</summary>
public enum PriceChannel
{
    InHouse = 1,
    Web = 2,
    MobileApp = 3,
    Kiosk = 4,
}

/// <summary>
/// Un prix de base propre à un établissement, à un canal, ou aux deux. Les variantes gardent leur écart avec ce prix.
/// </summary>
public sealed record PriceOverride(LocationId? LocationId, PriceChannel? Channel, Money BasePrice)
{
    /// <summary>Le plus précis gagne : établissement et canal, puis établissement seul, puis canal seul.</summary>
    internal int Rank => (LocationId is null ? 0 : 2) + (Channel is null ? 0 : 1);

    internal bool Targets(LocationId? locationId, PriceChannel? channel) => LocationId == locationId && Channel == channel;

    internal bool AppliesTo(LocationId? locationId, PriceChannel? channel) =>
        (LocationId is null || LocationId == locationId) && (Channel is null || Channel == channel);
}
