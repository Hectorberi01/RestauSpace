namespace Loyalty.Domain;

/// <summary>Avance à chaque lecture : prouve qu'une opération lit l'horloge une fois (horodatage état == événement).</summary>
internal sealed class TickingClock : IClock
{
    private DateTimeOffset _next = FakeClock.DefaultNow;

    public int Reads { get; private set; }

    public DateTimeOffset UtcNow
    {
        get
        {
            Reads++;
            var now = _next;
            _next = _next.AddSeconds(1);
            return now;
        }
    }
}
