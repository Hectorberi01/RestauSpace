namespace Reservations.Domain;

public readonly record struct ReservationId
{
    private ReservationId(Guid value) => Value = value;

    public Guid Value { get; }

    public static ReservationId New() => new(Guid.CreateVersion7());

    public static ReservationId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new ReservationId(value);
    }

    public override string ToString() => Value.ToString();
}
