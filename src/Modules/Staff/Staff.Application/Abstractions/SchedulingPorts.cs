using Staff.Domain;

namespace Staff.Application.Abstractions;

public interface IScheduleRepository
{
    /// <summary>La semaine d'un établissement, suivie pour modification. Null tant que rien n'y a été saisi.</summary>
    Task<ScheduleWeek?> FindAsync(TenantId tenantId, LocationId locationId, DateOnly weekStart, CancellationToken cancellationToken = default);

    Task<ScheduleWeek?> FindByIdAsync(TenantId tenantId, ScheduleWeekId id, CancellationToken cancellationToken = default);

    /// <summary>Semaines commençant dans [<paramref name="fromWeek"/>, <paramref name="toWeek"/>] (lecture seule), tous établissements si null.</summary>
    Task<IReadOnlyList<ScheduleWeek>> ListAsync(
        TenantId tenantId, LocationId? locationId, DateOnly fromWeek, DateOnly toWeek, CancellationToken cancellationToken = default);

    void Add(ScheduleWeek week);

    Task<ScheduleSettings?> FindSettingsAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    void AddSettings(ScheduleSettings settings);
}

public interface ITimeOffRepository
{
    Task<TimeOffRequest?> FindAsync(TenantId tenantId, TimeOffRequestId id, CancellationToken cancellationToken = default);

    /// <summary>Absences qui touchent [<paramref name="from"/>, <paramref name="to"/>] (lecture seule).</summary>
    Task<IReadOnlyList<TimeOffRequest>> ListAsync(
        TenantId tenantId, DateOnly from, DateOnly to, StaffMemberId? memberId, TimeOffStatus? status, int max,
        CancellationToken cancellationToken = default);

    void Add(TimeOffRequest request);
}

public interface IShiftSwapRepository
{
    Task<ShiftSwapRequest?> FindAsync(TenantId tenantId, ShiftSwapRequestId id, CancellationToken cancellationToken = default);

    /// <summary>Une demande encore ouverte sur ce créneau (lecture seule).</summary>
    Task<ShiftSwapRequest?> FindOpenForShiftAsync(TenantId tenantId, ShiftId shiftId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Demandes dont le créneau commence après <paramref name="startsAfterUtc"/> (lecture seule) ; celles d'un membre
    /// (demandeur ou collègue) si <paramref name="involving"/> est donné.
    /// </summary>
    Task<IReadOnlyList<ShiftSwapRequest>> ListAsync(
        TenantId tenantId, DateTimeOffset startsAfterUtc, StaffMemberId? involving, bool openOnly, int max, CancellationToken cancellationToken = default);

    void Add(ShiftSwapRequest request);
}

/// <summary>Un établissement du tenant de la requête vu par le planning, servi par l'hôte depuis Locations.</summary>
public sealed record ScheduleLocation(LocationId Id, string Name, string TimeZoneId);

public interface IScheduleLocations
{
    Task<ScheduleLocation?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);
}
