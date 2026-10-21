using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Infrastructure.Persistence;

// Comme les autres repositories de Staff : filtre de tenant contourné, remplacé par un prédicat de tenant explicite.

internal sealed class ScheduleRepository(StaffDbContext context) : IScheduleRepository
{
    private IQueryable<ScheduleWeek> Weeks => context.ScheduleWeeks.IgnoreQueryFilters();

    public Task<ScheduleWeek?> FindAsync(TenantId tenantId, LocationId locationId, DateOnly weekStart, CancellationToken cancellationToken = default) =>
        Weeks.SingleOrDefaultAsync(week => week.TenantId == tenantId && week.LocationId == locationId && week.WeekStart == weekStart, cancellationToken);

    public Task<ScheduleWeek?> FindByIdAsync(TenantId tenantId, ScheduleWeekId id, CancellationToken cancellationToken = default) =>
        Weeks.SingleOrDefaultAsync(week => week.TenantId == tenantId && week.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ScheduleWeek>> ListAsync(
        TenantId tenantId, LocationId? locationId, DateOnly fromWeek, DateOnly toWeek, CancellationToken cancellationToken = default)
    {
        var query = Weeks.AsNoTracking().Where(week => week.TenantId == tenantId && week.WeekStart >= fromWeek && week.WeekStart <= toWeek);
        if (locationId is { } location)
        {
            query = query.Where(week => week.LocationId == location);
        }

        return await query.OrderBy(week => week.WeekStart).Take(500).ToListAsync(cancellationToken);
    }

    public void Add(ScheduleWeek week) => context.ScheduleWeeks.Add(week);

    public Task<ScheduleSettings?> FindSettingsAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        context.ScheduleSettings.IgnoreQueryFilters()
            .SingleOrDefaultAsync(settings => settings.TenantId == tenantId && settings.LocationId == locationId, cancellationToken);

    public void AddSettings(ScheduleSettings settings) => context.ScheduleSettings.Add(settings);
}

internal sealed class TimeOffRepository(StaffDbContext context) : ITimeOffRepository
{
    private IQueryable<TimeOffRequest> Requests => context.TimeOffRequests.IgnoreQueryFilters();

    public Task<TimeOffRequest?> FindAsync(TenantId tenantId, TimeOffRequestId id, CancellationToken cancellationToken = default) =>
        Requests.SingleOrDefaultAsync(request => request.TenantId == tenantId && request.Id == id, cancellationToken);

    public async Task<IReadOnlyList<TimeOffRequest>> ListAsync(
        TenantId tenantId, DateOnly from, DateOnly to, StaffMemberId? memberId, TimeOffStatus? status, int max,
        CancellationToken cancellationToken = default)
    {
        var query = Requests.AsNoTracking()
            .Where(request => request.TenantId == tenantId && request.LastDay >= from && request.FirstDay <= to);
        if (memberId is { } member)
        {
            query = query.Where(request => request.StaffMemberId == member);
        }

        if (status is { } wanted)
        {
            query = query.Where(request => request.Status == wanted);
        }

        return await query.OrderBy(request => request.FirstDay).Take(max).ToListAsync(cancellationToken);
    }

    public void Add(TimeOffRequest request) => context.TimeOffRequests.Add(request);
}

internal sealed class ShiftSwapRepository(StaffDbContext context) : IShiftSwapRepository
{
    private IQueryable<ShiftSwapRequest> Requests => context.ShiftSwapRequests.IgnoreQueryFilters();

    public Task<ShiftSwapRequest?> FindAsync(TenantId tenantId, ShiftSwapRequestId id, CancellationToken cancellationToken = default) =>
        Requests.SingleOrDefaultAsync(request => request.TenantId == tenantId && request.Id == id, cancellationToken);

    public Task<ShiftSwapRequest?> FindOpenForShiftAsync(TenantId tenantId, ShiftId shiftId, CancellationToken cancellationToken = default) =>
        Requests.AsNoTracking()
            .Where(request => request.TenantId == tenantId && request.ShiftId == shiftId
                && (request.Status == ShiftSwapStatus.AwaitingColleague || request.Status == ShiftSwapStatus.AwaitingManager))
            .OrderByDescending(request => request.RequestedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ShiftSwapRequest>> ListAsync(
        TenantId tenantId, DateTimeOffset startsAfterUtc, StaffMemberId? involving, bool openOnly, int max, CancellationToken cancellationToken = default)
    {
        var query = Requests.AsNoTracking().Where(request => request.TenantId == tenantId && request.ShiftStartsAtUtc > startsAfterUtc);
        if (involving is { } member)
        {
            query = query.Where(request => request.FromMemberId == member || request.ToMemberId == member);
        }

        if (openOnly)
        {
            query = query.Where(request => request.Status == ShiftSwapStatus.AwaitingColleague || request.Status == ShiftSwapStatus.AwaitingManager);
        }

        return await query.OrderBy(request => request.ShiftStartsAtUtc).Take(max).ToListAsync(cancellationToken);
    }

    public void Add(ShiftSwapRequest request) => context.ShiftSwapRequests.Add(request);
}
