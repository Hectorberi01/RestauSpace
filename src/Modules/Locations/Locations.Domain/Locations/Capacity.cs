namespace Locations.Domain.Locations;

/// <summary>Nombre de couverts simultanés de l'établissement. Zéro est valide (emporter seul, pas encore configuré).</summary>
public sealed class Capacity : ValueObject
{
    private Capacity(int seats) => Seats = seats;

    public int Seats { get; }

    public static Capacity Zero { get; } = new(0);

    public static Capacity Create(int seats)
    {
        DomainException.ThrowIf(seats < 0, LocationErrors.CapacityNegative);
        return new Capacity(seats);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Seats;
    }

    public override string ToString() => Seats.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
