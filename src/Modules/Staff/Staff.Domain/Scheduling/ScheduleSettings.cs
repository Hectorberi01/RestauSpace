namespace Staff.Domain;

/// <summary>
/// Réglages du planning d'un établissement : les postes proposés à la saisie (un créneau peut en porter un autre) et le
/// plafond d'heures par semaine au-delà duquel le gérant est alerté.
/// </summary>
public sealed class ScheduleSettings : ITenantOwned
{
    public const int MaxPositions = 20;
    public const int DefaultWeeklyHoursLimit = 48;
    public const int MaxWeeklyHoursLimit = 80;

    public static IReadOnlyList<string> DefaultPositions { get; } = ["Salle", "Cuisine", "Bar", "Plonge"];

    private ScheduleSettings(TenantId tenantId, LocationId locationId, IReadOnlyList<string> positions, int weeklyHoursLimit)
    {
        TenantId = tenantId;
        LocationId = locationId;
        Positions = positions;
        WeeklyHoursLimit = weeklyHoursLimit;
    }

    // Réservé à l'ORM.
    private ScheduleSettings()
    {
        Positions = [];
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public IReadOnlyList<string> Positions { get; private set; }

    public int WeeklyHoursLimit { get; private set; }

    public static ScheduleSettings Default(TenantId tenantId, LocationId locationId) =>
        new(Guard.NotEmpty(tenantId), Guard.NotEmpty(locationId), DefaultPositions, DefaultWeeklyHoursLimit);

    public void Update(IEnumerable<string?>? positions, int weeklyHoursLimit)
    {
        DomainException.ThrowIf(weeklyHoursLimit < 1 || weeklyHoursLimit > MaxWeeklyHoursLimit, ScheduleErrors.WeeklyLimitInvalid);
        var cleaned = (positions ?? [])
            .Where(position => !string.IsNullOrWhiteSpace(position))
            .Select(position => position!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        DomainException.ThrowIf(cleaned.Count > MaxPositions, ScheduleErrors.TooManyPositions);
        DomainException.ThrowIf(cleaned.Any(position => position.Length > Shift.PositionMaxLength), ScheduleErrors.PositionTooLong);
        Positions = cleaned;
        WeeklyHoursLimit = weeklyHoursLimit;
    }
}
