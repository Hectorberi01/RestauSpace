namespace Reporting.Domain;

/// <summary>
/// Avance d'une seconde à chaque lecture : révèle une opération qui lit l'horloge plus d'une fois (ses
/// horodatages d'état différeraient alors du OccurredAtUtc de son événement).
/// </summary>
internal sealed class TickingClock(DateTimeOffset start) : IClock
{
    private DateTimeOffset _next = start;

    public DateTimeOffset UtcNow
    {
        get
        {
            var current = _next;
            _next = _next.AddSeconds(1);
            return current;
        }
    }
}
