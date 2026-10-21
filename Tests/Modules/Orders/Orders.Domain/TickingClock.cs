namespace Orders.Domain;

/// <summary>Horloge qui avance à chaque lecture, comme une vraie : révèle une opération qui lit « maintenant » deux fois.</summary>
internal sealed class TickingClock : IClock
{
    private DateTimeOffset _now = FakeClock.DefaultNow;

    public DateTimeOffset UtcNow => _now = _now.AddSeconds(1);
}
