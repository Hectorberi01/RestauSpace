using SaasRestaurant.BuildingBlocks.Domain;

namespace SaasRestaurant.Testing;

/// <summary>Horloge déterministe pour les tests. Démarre au 2026-01-15T12:00:00Z sauf indication contraire.</summary>
public sealed class FakeClock : IClock
{
    public static readonly DateTimeOffset DefaultNow = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public FakeClock() : this(DefaultNow)
    {
    }

    public FakeClock(DateTimeOffset utcNow) => UtcNow = utcNow.ToUniversalTime();

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);

    public void Set(DateTimeOffset utcNow) => UtcNow = utcNow.ToUniversalTime();
}
