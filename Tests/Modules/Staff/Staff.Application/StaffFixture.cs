using Staff.Application.Abstractions;
using Staff.Application.Commands;
using Staff.Application.Queries;
using Staff.Domain;

namespace Staff.Application;

/// <summary>
/// Ports en mémoire du module Staff. Comme les vrais repositories, ils répondent dans le tenant qu'on leur donne.
/// Le tenant de « la requête » est <see cref="CurrentTenantId"/> ; les comptes d'Identity sont <see cref="Accounts"/>.
/// </summary>
public sealed class StaffFixture :
    IStaffMemberRepository, IRoleRepository, IStaffUnitOfWork, IStaffTenants, ILocationDirectory, IUserAccounts, IStaffPlanLimits,
    IStaffMemberUniquenessChecker, IRoleNameUniquenessChecker, ITenantContext
{
    public const string LocationsManage = "locations.manage";

    private readonly List<StaffMember> _members = [];
    private readonly List<Role> _roles = [];

    public StaffFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        Tenants[TenantA] = TenantAccess.Writable;
        Tenants[TenantB] = TenantAccess.Writable;
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public Dictionary<TenantId, TenantAccess> Tenants { get; } = [];

    /// <summary>Établissements connus du faux module Locations, avec leur tenant et leur caractère opérationnel.</summary>
    public Dictionary<LocationId, (TenantId TenantId, bool IsOperational)> Locations { get; } = [];

    /// <summary>Comptes du faux module Identity, par email.</summary>
    public Dictionary<string, UserId> Accounts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> InvitedAccounts { get; } = [];

    public TenantId? CurrentTenantId { get; set; }

    public FakeClock Clock { get; } = new();

    public KnownPermissions Permissions { get; } = KnownPermissions.Of([LocationsManage]);

    public int SaveCount { get; private set; }

    /// <summary>Lignes « réécrites » pour protéger une décision (le rôle Owner quand un propriétaire est retiré).</summary>
    public List<Role> Guarded { get; } = [];

    public StaffWorkspace Workspace => new(this, this, this, this, this);

    public OnboardTenantOwnerHandler Onboard => new(Workspace, this, this, this, Permissions, Clock);

    public InviteStaffHandler Invite => new(Workspace, this, this, this, Clock);

    /// <summary>Plafond de personnel du plan (null : aucun).</summary>
    public long? MaxStaffMembers { get; set; }

    public AcceptStaffInvitationHandler Accept => new(Workspace, Clock);

    public AssignStaffToLocationHandler AssignToLocation => new(Workspace, this, Clock);

    public RemoveTenantWideRoleHandler RemoveTenantRole => new(Workspace, Clock);

    public SuspendStaffHandler Suspend => new(Workspace, Clock);

    public RemoveStaffHandler Remove => new(Workspace, Clock);

    public CreateRoleHandler CreateRole => new(Workspace, this, Clock);

    public GrantPermissionHandler Grant => new(Workspace, Permissions, Clock);

    public RevokePermissionHandler Revoke => new(Workspace, Clock);

    public DeleteRoleHandler DeleteRole => new(Workspace, Clock);

    public ResendStaffInvitationHandler Resend => new(Workspace, Clock);

    public ListMyMembershipsHandler MyMemberships => new(this, this);

    public GetMyPermissionsHandler MyPermissions => new(this, this, this);

    public IsActiveStaffMemberHandler IsActiveMember => new(this);

    public ListSoleOwnershipsHandler SoleOwnerships => new(this, this);

    public StaffPermissionChecker Checker => new(this, this);

    public StaffMember Member(Guid id) => _members.Single(member => member.Id.Value == id);

    public Role SystemRole(TenantId tenantId, string name) =>
        _roles.Single(role => role.TenantId == tenantId && role.IsSystem && role.Name.Value == name);

    public UserId Account(string email) => Accounts[email];

    public IReadOnlyList<Role> ListRoles(TenantId tenantId) => _roles.Where(role => role.TenantId == tenantId).ToList();

    /// <summary>Tenant A intégré avec <paramref name="email"/> pour propriétaire ; la requête agit ensuite pour A.</summary>
    public async Task<Guid> GivenOnboardedTenantAsync(string email = "owner@example.com")
    {
        var ownerId = await Onboard.HandleAsync(new OnboardTenantOwnerCommand(TenantA.Value, email));
        CurrentTenantId = TenantA;
        SaveCount = 0;
        return ownerId;
    }

    public LocationId GivenLocation(TenantId tenantId, bool isOperational = true)
    {
        var id = LocationId.New();
        Locations[id] = (tenantId, isOperational);
        return id;
    }

    // --- IStaffMemberRepository.

    public Task<StaffMember?> FindAsync(TenantId tenantId, StaffMemberId id, CancellationToken cancellationToken = default) =>
        Task.FromResult<StaffMember?>(_members.SingleOrDefault(member => member.TenantId == tenantId && member.Id == id));

    public Task<StaffMember?> FindByUserAsync(TenantId tenantId, UserId userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<StaffMember?>(_members.SingleOrDefault(member => member.TenantId == tenantId && member.UserId == userId));

    public Task<StaffPage> ListAsync(TenantId tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var matching = _members.Where(member => member.TenantId == tenantId).ToList();
        return Task.FromResult(new StaffPage(matching.Skip(skip).Take(take).ToList(), matching.Count));
    }

    public Task<IReadOnlyList<StaffMember>> ListByUserAsync(UserId userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StaffMember>>(_members.Where(member => member.UserId == userId).ToList());

    public Task<bool> IsRoleHeldAsync(TenantId tenantId, RoleId roleId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_members.Any(member =>
            member.TenantId == tenantId
            && member.Status != StaffStatus.Left
            && (member.TenantWideRoleId == roleId || member.Assignments.Any(assignment => assignment.RoleId == roleId))));

    public Task<int> CountActiveTenantWideHoldersAsync(
        TenantId tenantId, RoleId roleId, StaffMemberId excluding, CancellationToken cancellationToken = default) =>
        Task.FromResult(_members.Count(member =>
            member.TenantId == tenantId
            && member.Status == StaffStatus.Active
            && member.TenantWideRoleId == roleId
            && member.Id != excluding));

    public Task<int> CountCurrentAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_members.Count(member => member.TenantId == tenantId && member.Status != StaffStatus.Left));

    public Task<long?> MaxStaffMembersAsync(TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult(MaxStaffMembers);

    public Task<IReadOnlyList<StaffMember>> ListActiveAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StaffMember>>(_members.Where(member => member.TenantId == tenantId && member.Status == StaffStatus.Active).ToList());

    public void Add(StaffMember member) => _members.Add(member);

    // --- IRoleRepository.

    public Task<Role?> FindAsync(TenantId tenantId, RoleId id, CancellationToken cancellationToken = default) =>
        Task.FromResult<Role?>(_roles.SingleOrDefault(role => role.TenantId == tenantId && role.Id == id));

    public Task<Role?> FindSystemRoleAsync(TenantId tenantId, string name, CancellationToken cancellationToken = default) =>
        Task.FromResult<Role?>(_roles.SingleOrDefault(role => role.TenantId == tenantId && role.IsSystem && role.Name.Value == name));

    Task<IReadOnlyList<Role>> IRoleRepository.ListAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Role>>(_roles.Where(role => role.TenantId == tenantId).ToList());

    Task<IReadOnlyList<Role>> IRoleRepository.ListSystemRolesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Role>>(_roles.Where(role => role.IsSystem).ToList());

    public void Add(Role role) => _roles.Add(role);

    void IRoleRepository.Remove(Role role) => _roles.Remove(role);

    public void GuardDecisionsOn(Role role) => Guarded.Add(role);

    // --- Unit of work, tenants, établissements, comptes, unicité.

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<TenantAccess> GetAccessAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tenants.TryGetValue(tenantId, out var access) ? access : TenantAccess.Unknown);

    public static string TenantName(TenantId tenantId) => "Restaurant " + tenantId.Value.ToString("N")[..6];

    public Task<string?> GetNameAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Tenants.ContainsKey(tenantId) ? TenantName(tenantId) : null);

    Task<LocationReference?> ILocationDirectory.FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult<LocationReference?>(Locations.TryGetValue(locationId, out var location) && location.TenantId == tenantId
            ? new LocationReference(tenantId, locationId, location.IsOperational)
            : null);

    public Task<UserId> FindOrInviteAsync(string? email, CancellationToken cancellationToken = default)
    {
        var address = Email.Create(email).Value;
        if (!Accounts.TryGetValue(address, out var userId))
        {
            userId = UserId.New();
            Accounts[address] = userId;
            InvitedAccounts.Add(address);
        }

        return Task.FromResult(userId);
    }

    public Task<IReadOnlyDictionary<UserId, string>> EmailsAsync(IReadOnlyCollection<UserId> users, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<UserId, string>>(
            Accounts.Where(account => users.Contains(account.Value)).ToDictionary(account => account.Value, account => account.Key));

    public Task<IReadOnlyDictionary<UserId, string>> DisplayNamesAsync(IReadOnlyCollection<UserId> users, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<UserId, string>>(
            Accounts.Where(account => users.Contains(account.Value)).ToDictionary(account => account.Value, account => account.Key.Split('@')[0]));

    public Task<bool> IsUserUniqueInTenantAsync(TenantId tenantId, UserId userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(!_members.Any(member => member.TenantId == tenantId && member.UserId == userId));

    public Task<bool> IsNameUniqueAsync(
        TenantId tenantId, RoleName name, RoleId? excluding = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(!_roles.Any(role =>
            role.TenantId == tenantId
            && role.Id != excluding
            && string.Equals(role.Name.Value, name.Value, StringComparison.OrdinalIgnoreCase)));
}
