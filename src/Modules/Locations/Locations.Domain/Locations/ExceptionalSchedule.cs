namespace Locations.Domain.Locations;

/// <summary>
/// Exception datée qui REMPLACE l'horaire hebdomadaire de sa date : soit un jour fermé, soit des plages spécifiques.
/// Créée et supprimée uniquement via <see cref="RestaurantLocation"/>, qui garantit une seule exception par date.
/// </summary>
public sealed class ExceptionalSchedule : Entity<ExceptionalScheduleId>
{
    public const int ReasonMaxLength = 200;

    // Pas readonly : l'ORM l'affecte au chargement (stocké en une seule valeur jsonb). Jamais modifié après création.
    private List<TimeRange> _ranges = [];

    private ExceptionalSchedule(ExceptionalScheduleId id, DateOnly date, IEnumerable<TimeRange> ranges, string? reason): base(id)
    {
        Date = date;
        _ranges.AddRange(ranges);
        Reason = reason;
    }

    // Réservé à l'ORM.
    private ExceptionalSchedule()
    {
    }

    public DateOnly Date { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>Vide quand le jour est fermé ; sinon trié par heure de début.</summary>
    public IReadOnlyCollection<TimeRange> Ranges => _ranges.AsReadOnly();

    public bool IsClosed => _ranges.Count == 0;

    internal static ExceptionalSchedule Closed(DateOnly date, string? reason) =>
        new(ExceptionalScheduleId.New(), date, [], NormalizeReason(reason));

    internal static ExceptionalSchedule Open(DateOnly date, IEnumerable<TimeRange> ranges, string? reason)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        var sorted = ranges.ToList();
        foreach (var range in sorted)
        {
            // Un élément null est une erreur de programmation de l'appelant, pas une règle métier.
            ArgumentNullException.ThrowIfNull(range, nameof(ranges));
        }

        DomainException.ThrowIf(sorted.Count == 0, LocationErrors.ExceptionalOpeningWithoutRange);

        sorted.Sort((left, right) => left.Start.CompareTo(right.Start));
        for (var i = 1; i < sorted.Count; i++)
        {
            // Trié par début : une plage ne peut chevaucher que l'une des précédentes.
            DomainException.ThrowIf(
                sorted.Take(i).Any(previous => previous.Overlaps(sorted[i])),
                LocationErrors.HoursRangesOverlap);
        }

        return new ExceptionalSchedule(ExceptionalScheduleId.New(), date, sorted, NormalizeReason(reason));
    }

    private static string? NormalizeReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return null;
        }

        var trimmed = reason.Trim();
        DomainException.ThrowIf(trimmed.Length > ReasonMaxLength, LocationErrors.ReasonTooLong);
        return trimmed;
    }
}
