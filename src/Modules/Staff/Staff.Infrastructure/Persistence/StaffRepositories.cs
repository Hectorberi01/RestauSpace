using Staff.Application.Abstractions;
using Staff.Domain;
using Staff.Infrastructure.Persistence.Configurations;

namespace Staff.Infrastructure.Persistence;

/// <summary>
/// Chaque requête précise son tenant : Staff répond aussi hors des requêtes de tenant (sélection du restaurant,
/// onboarding), où le filtre de tenant de la requête du contexte masquerait tout. Le filtre est contourné
/// volontairement ICI SEULEMENT, et toujours remplacé par un prédicat explicite de tenant (ou d'utilisateur).
/// </summary>
internal sealed class StaffMemberRepository(StaffDbContext context) : IStaffMemberRepository
{
    private IQueryable<StaffMember> Members => context.Members.IgnoreQueryFilters();

    public Task<StaffMember?> FindAsync(TenantId tenantId, StaffMemberId id, CancellationToken cancellationToken = default) =>
        Members.SingleOrDefaultAsync(member => member.TenantId == tenantId && member.Id == id, cancellationToken);

    public Task<StaffMember?> FindByUserAsync(TenantId tenantId, UserId userId, CancellationToken cancellationToken = default) =>
        Members.SingleOrDefaultAsync(member => member.TenantId == tenantId && member.UserId == userId, cancellationToken);

    public async Task<StaffPage> ListAsync(TenantId tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = Members.AsNoTracking().Where(member => member.TenantId == tenantId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(member => member.InvitedAtUtc)
            .ThenBy(member => member.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
        return new StaffPage(items, total);
    }

    public async Task<IReadOnlyList<StaffMember>> ListByUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
        await Members.AsNoTracking().Where(member => member.UserId == userId).ToListAsync(cancellationToken);

    public Task<bool> IsRoleHeldAsync(TenantId tenantId, RoleId roleId, CancellationToken cancellationToken = default) =>
        Members.AnyAsync(
            member => member.TenantId == tenantId
                && member.Status != StaffStatus.Left
                && (member.TenantWideRoleId == roleId || member.Assignments.Any(assignment => assignment.RoleId == roleId)),
            cancellationToken);

    public Task<int> CountActiveTenantWideHoldersAsync(
        TenantId tenantId, RoleId roleId, StaffMemberId excluding, CancellationToken cancellationToken = default) =>
        Members.CountAsync(
            member => member.TenantId == tenantId
                && member.Status == StaffStatus.Active
                && member.TenantWideRoleId == roleId
                && member.Id != excluding,
            cancellationToken);

    public Task<int> CountCurrentAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Members.CountAsync(member => member.TenantId == tenantId && member.Status != StaffStatus.Left, cancellationToken);

    public async Task<IReadOnlyList<StaffMember>> ListActiveAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await Members.AsNoTracking().Where(member => member.TenantId == tenantId && member.Status == StaffStatus.Active).ToListAsync(cancellationToken);

    public void Add(StaffMember member) => context.Members.Add(member);
}

internal sealed class RoleRepository(StaffDbContext context) : IRoleRepository
{
    private IQueryable<Role> Roles => context.Roles.IgnoreQueryFilters();

    public Task<Role?> FindAsync(TenantId tenantId, RoleId id, CancellationToken cancellationToken = default) =>
        Roles.SingleOrDefaultAsync(role => role.TenantId == tenantId && role.Id == id, cancellationToken);

    public async Task<Role?> FindSystemRoleAsync(TenantId tenantId, string name, CancellationToken cancellationToken = default)
    {
        var roleName = RoleName.Create(name);
        return await Roles.SingleOrDefaultAsync(
            role => role.TenantId == tenantId && role.IsSystem && role.Name == roleName, cancellationToken);
    }

    public async Task<IReadOnlyList<Role>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await Roles.AsNoTracking()
            .Where(role => role.TenantId == tenantId)
            .OrderBy(role => role.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Role>> ListSystemRolesAsync(CancellationToken cancellationToken = default) =>
        await Roles.Where(role => role.IsSystem).ToListAsync(cancellationToken);

    public void Add(Role role) => context.Roles.Add(role);

    public void Remove(Role role) => context.Roles.Remove(role);

    public void GuardDecisionsOn(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        context.Entry(role).Property(candidate => candidate.IsSystem).IsModified = true;
    }
}

internal sealed class StaffMemberUniquenessChecker(StaffDbContext context) : IStaffMemberUniquenessChecker
{
    public async Task<bool> IsUserUniqueInTenantAsync(TenantId tenantId, UserId userId, CancellationToken cancellationToken = default) =>
        !await context.Members.IgnoreQueryFilters()
            .AnyAsync(member => member.TenantId == tenantId && member.UserId == userId, cancellationToken);
}

internal sealed class RoleNameUniquenessChecker(StaffDbContext context) : IRoleNameUniquenessChecker
{
    public async Task<bool> IsNameUniqueAsync(
        TenantId tenantId, RoleName name, RoleId? excluding = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        var normalized = name.Value.ToUpperInvariant();
        return !await context.Roles.IgnoreQueryFilters()
            .Where(role => role.TenantId == tenantId && (excluding == null || role.Id != excluding))
            .AnyAsync(role => EF.Property<string>(role, RoleConfiguration.NormalizedNameProperty) == normalized, cancellationToken);
    }
}

/// <summary>Une caisse est cherchée par son seul id (elle s'authentifie avant qu'aucun tenant ne soit connu).</summary>
internal sealed class TillRepository(StaffDbContext context) : ITillRepository
{
    public Task<Till?> FindAsync(TillId id, CancellationToken cancellationToken = default) =>
        context.Tills.IgnoreQueryFilters().SingleOrDefaultAsync(till => till.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Till>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await context.Tills.IgnoreQueryFilters().AsNoTracking().Where(till => till.TenantId == tenantId).ToListAsync(cancellationToken);

    public void Add(Till till) => context.Tills.Add(till);
}

internal sealed class TimeEntryRepository(StaffDbContext context) : ITimeEntryRepository
{
    private IQueryable<TimeEntry> Entries => context.TimeEntries.IgnoreQueryFilters();

    public Task<TimeEntry?> FindOpenAsync(TenantId tenantId, StaffMemberId memberId, CancellationToken cancellationToken = default) =>
        Entries.SingleOrDefaultAsync(
            entry => entry.TenantId == tenantId && entry.StaffMemberId == memberId && entry.ClockedOutAtUtc == null, cancellationToken);

    public async Task<IReadOnlyList<TimeEntry>> ListOpenAsync(
        TenantId tenantId, IReadOnlyCollection<StaffMemberId> members, CancellationToken cancellationToken = default) =>
        await Entries.AsNoTracking()
            .Where(entry => entry.TenantId == tenantId && entry.ClockedOutAtUtc == null && members.Contains(entry.StaffMemberId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TimeEntry>> ListAsync(
        TenantId tenantId, DateTimeOffset fromUtc, DateTimeOffset untilUtc, LocationId? locationId, StaffMemberId? memberId, int max,
        CancellationToken cancellationToken = default)
    {
        var query = Entries.AsNoTracking()
            .Where(entry => entry.TenantId == tenantId && entry.ClockedInAtUtc >= fromUtc && entry.ClockedInAtUtc < untilUtc);
        if (locationId is { } location)
        {
            query = query.Where(entry => entry.LocationId == location);
        }

        if (memberId is { } member)
        {
            query = query.Where(entry => entry.StaffMemberId == member);
        }

        return await query.OrderBy(entry => entry.ClockedInAtUtc).Take(max).ToListAsync(cancellationToken);
    }

    public void Add(TimeEntry entry) => context.TimeEntries.Add(entry);
}
