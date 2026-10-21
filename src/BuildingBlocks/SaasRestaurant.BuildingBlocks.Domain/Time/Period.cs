namespace SaasRestaurant.BuildingBlocks.Domain;

/// <summary>Intervalle UTC semi-ouvert [StartUtc, EndUtc).</summary>
public sealed class Period : ValueObject
{
    private Period(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        StartUtc = startUtc;
        EndUtc = endUtc;
    }

    public DateTimeOffset StartUtc { get; }

    public DateTimeOffset EndUtc { get; }

    public TimeSpan Duration => EndUtc - StartUtc;

    public static Period Create(DateTimeOffset start, DateTimeOffset end)
    {
        DomainException.ThrowIf(start >= end, CommonErrors.PeriodInvalid);
        return new Period(start.ToUniversalTime(), end.ToUniversalTime());
    }

    public bool Contains(DateTimeOffset instant) => instant >= StartUtc && instant < EndUtc;

    public bool Overlaps(Period other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return StartUtc < other.EndUtc && other.StartUtc < EndUtc;
    }

    public bool HasEndedAt(DateTimeOffset instant) => instant >= EndUtc;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return StartUtc;
        yield return EndUtc;
    }

    public override string ToString() => $"[{StartUtc:O}, {EndUtc:O})";
}
