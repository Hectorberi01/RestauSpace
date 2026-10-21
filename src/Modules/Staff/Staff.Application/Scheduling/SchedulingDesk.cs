using Staff.Application.Abstractions;
using Staff.Domain;

namespace Staff.Application.Scheduling;

/// <summary>
/// Étapes partagées par les cas d'usage du planning : l'établissement est cherché dans le tenant de la requête (404
/// sinon), un membre planifié doit être actif et travailler dans l'établissement, les heures locales sont converties avec
/// le fuseau de l'établissement, et les noms affichés viennent d'Identity.
/// </summary>
public sealed class SchedulingDesk(
    StaffWorkspace workspace,
    IScheduleRepository schedules,
    IScheduleLocations locations,
    IUserAccounts users,
    IClock clock)
{
    public IScheduleRepository Schedules => schedules;

    public StaffWorkspace Workspace => workspace;

    public IClock Clock => clock;

    public async Task<ScheduleLocation> RequireLocationAsync(TenantId tenantId, Guid locationId, CancellationToken cancellationToken)
    {
        var location = locationId == Guid.Empty ? null : await locations.FindAsync(tenantId, LocationId.From(locationId), cancellationToken);
        return location ?? throw new NotFoundException(StaffApplicationErrors.ScheduleLocationNotFound);
    }

    public Task<ScheduleLocation?> FindLocationAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        locations.FindAsync(tenantId, locationId, cancellationToken);

    /// <summary>La semaine, créée vide si rien n'y a encore été saisi.</summary>
    public async Task<ScheduleWeek> LoadOrCreateWeekAsync(TenantId tenantId, LocationId locationId, DateOnly weekStart, CancellationToken cancellationToken)
    {
        if (await schedules.FindAsync(tenantId, locationId, weekStart, cancellationToken) is { } week)
        {
            return week;
        }

        week = ScheduleWeek.Create(tenantId, locationId, weekStart, clock);
        schedules.Add(week);
        return week;
    }

    public async Task<ScheduleWeek> LoadWeekAsync(TenantId tenantId, LocationId locationId, DateOnly weekStart, CancellationToken cancellationToken) =>
        await schedules.FindAsync(tenantId, locationId, weekStart, cancellationToken)
        ?? throw new NotFoundException(StaffApplicationErrors.ScheduleWeekNotFound);

    /// <summary>Un membre qu'on peut planifier ici : actif, avec un rôle dans l'établissement (ou sur tout le restaurant).</summary>
    public async Task<StaffMember> RequirePlannableAsync(TenantId tenantId, Guid memberId, LocationId locationId, CancellationToken cancellationToken)
    {
        var member = await workspace.LoadMemberAsync(tenantId, memberId, cancellationToken);
        DomainException.ThrowIf(member.Status != StaffStatus.Active, StaffMemberErrors.NotActive);
        DomainException.ThrowIf(member.EffectiveRoleAt(locationId) is null, StaffMemberErrors.NotAssignedToLocation);
        return member;
    }

    /// <summary>Les membres actifs qui travaillent dans l'établissement.</summary>
    public async Task<IReadOnlyList<StaffMember>> MembersAtAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        (await workspace.Members.ListActiveAsync(tenantId, cancellationToken))
            .Where(member => member.EffectiveRoleAt(locationId) is not null)
            .ToList();

    /// <summary>Nom court de chaque membre (« Awa K. »), y compris ceux qui ne sont plus actifs ; « Membre » à défaut.</summary>
    public async Task<IReadOnlyDictionary<StaffMemberId, string>> NamesAsync(
        TenantId tenantId, IEnumerable<StaffMemberId> memberIds, IReadOnlyList<StaffMember>? known, CancellationToken cancellationToken)
    {
        var userOf = (known ?? []).ToDictionary(member => member.Id, member => member.UserId);
        foreach (var id in memberIds.Distinct().Where(id => !userOf.ContainsKey(id)).ToList())
        {
            if (await workspace.Members.FindAsync(tenantId, id, cancellationToken) is { } member)
            {
                userOf[id] = member.UserId;
            }
        }

        var names = await users.DisplayNamesAsync(userOf.Values.Distinct().ToList(), cancellationToken);
        return userOf.ToDictionary(pair => pair.Key, pair => names.GetValueOrDefault(pair.Value) ?? "Membre");
    }

    public async Task<ScheduleSettings> SettingsAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        await schedules.FindSettingsAsync(tenantId, locationId, cancellationToken) ?? ScheduleSettings.Default(tenantId, locationId);

    /// <summary>Le jour d'aujourd'hui dans le fuseau de l'établissement.</summary>
    public DateOnly Today(string timeZoneId) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, Zone(timeZoneId)).DateTime);

    public static TimeZoneInfo Zone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>Une heure locale en UTC ; une heure qui n'existe pas (passage à l'heure d'été) est avancée d'une heure.</summary>
    public static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        var offset = zone.IsAmbiguousTime(unspecified) ? zone.GetAmbiguousTimeOffsets(unspecified).Max() : zone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset).ToUniversalTime();
    }

    public static (DateTimeOffset Start, DateTimeOffset End) UtcWindow(Shift shift, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(shift);
        return (ToUtc(shift.StartsAt, zone), ToUtc(shift.EndsAt, zone));
    }
}
