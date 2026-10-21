namespace Inventory.Domain;

/// <summary>Avance à chaque lecture : prouve qu'une opération ne lit l'horloge qu'une fois.</summary>
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
