using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application.Commands;

public sealed record TimeEntryDto(
    Guid Id,
    Guid StaffMemberId,
    string? Name,
    Guid LocationId,
    DateTimeOffset ClockedInAtUtc,
    DateTimeOffset? ClockedOutAtUtc,
    bool ClosedAutomatically,
    double WorkedMinutes);

/// <summary>Pointages d'une période, pour le gérant (et l'export des heures). 92 jours au plus.</summary>
public sealed record ListTimeEntriesQuery(DateTimeOffset FromUtc, DateTimeOffset UntilUtc, Guid? LocationId, Guid? StaffMemberId)
    : IQuery<IReadOnlyList<TimeEntryDto>>
{
    public const int Max = 5000;
}

public sealed class ListTimeEntriesHandler(
    ITenantContext tenantContext, ITimeEntryRepository entries, IStaffMemberRepository members, IUserAccounts users, IClock clock)
    : IQueryHandler<ListTimeEntriesQuery, IReadOnlyList<TimeEntryDto>>
{
    public async Task<IReadOnlyList<TimeEntryDto>> HandleAsync(ListTimeEntriesQuery query, CancellationToken cancellationToken = default)
    {
        DomainException.ThrowIf(
            query.UntilUtc <= query.FromUtc || query.UntilUtc - query.FromUtc > TimeSpan.FromDays(92),
            StaffApplicationErrors.TimeRangeInvalid);
        var tenantId = tenantContext.RequireTenant();
        var found = await entries.ListAsync(
            tenantId,
            query.FromUtc,
            query.UntilUtc,
            query.LocationId is { } location && location != Guid.Empty ? LocationId.From(location) : null,
            query.StaffMemberId is { } member && member != Guid.Empty ? StaffMemberId.From(member) : null,
            ListTimeEntriesQuery.Max,
            cancellationToken);

        var userOf = new Dictionary<StaffMemberId, UserId>();
        foreach (var id in found.Select(entry => entry.StaffMemberId).Distinct())
        {
            if (await members.FindAsync(tenantId, id, cancellationToken) is { } profile)
            {
                userOf[id] = profile.UserId;
            }
        }

        var names = await users.DisplayNamesAsync(userOf.Values.Distinct().ToList(), cancellationToken);
        var now = clock.UtcNow;
        return found
            .Select(entry => new TimeEntryDto(
                entry.Id.Value,
                entry.StaffMemberId.Value,
                userOf.TryGetValue(entry.StaffMemberId, out var user) ? names.GetValueOrDefault(user) : null,
                entry.LocationId.Value,
                entry.ClockedInAtUtc,
                entry.ClockedOutAtUtc,
                entry.ClosedAutomatically,
                Math.Round(entry.WorkedUntil(entry.IsStale(now) ? entry.ClockedInAtUtc + TimeEntry.MaxShift : now).TotalMinutes, 1)))
            .ToList();
    }
}
