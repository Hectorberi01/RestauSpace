namespace Staff.Domain;

/// <summary>
/// Profil d'un utilisateur (Identity) au sein d'un tenant, avec le rôle tenu dans chaque établissement et,
/// éventuellement, un rôle à l'échelle du tenant qui s'applique partout où aucun rôle propre à l'établissement n'est tenu.
/// </summary>
public sealed class StaffMember : AggregateRoot<StaffMemberId>, ITenantOwned
{
    private readonly List<StaffAssignment> _assignments = [];

    private StaffMember(StaffMemberId id, TenantId tenantId, UserId userId, DateTimeOffset invitedAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        Status = StaffStatus.Invited;
        InvitedAtUtc = invitedAtUtc;
    }

    // Réservé à l'ORM.
    private StaffMember()
    {
    }

    public TenantId TenantId { get; private set; }

    public UserId UserId { get; private set; }

    public StaffStatus Status { get; private set; }

    public DateTimeOffset InvitedAtUtc { get; private set; }

    public IReadOnlyCollection<StaffAssignment> Assignments => _assignments.AsReadOnly();

    /// <summary>Rôle tenu pour tout le tenant (stocké quel que soit le statut). Voir <see cref="EffectiveTenantRole"/>.</summary>
    public RoleId? TenantWideRoleId { get; private set; }

    /// <summary>Empreinte du code PIN de caisse (jamais le code lui-même) ; null : aucun PIN choisi.</summary>
    public string? PinHash { get; private set; }

    /// <summary>Essais de PIN manqués depuis le dernier réussi.</summary>
    public int PinFailures { get; private set; }

    public DateTimeOffset? PinLockedUntilUtc { get; private set; }

    public bool HasPin => PinHash is not null;

    public static StaffMember Invite(TenantId tenantId, UserId userId, bool isUserUniqueInTenant, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(userId);
        DomainException.ThrowIf(!isUserUniqueInTenant, StaffMemberErrors.UserAlreadyStaffInTenant);

        var now = clock.UtcNow;
        var member = new StaffMember(StaffMemberId.New(), tenantId, userId, now);
        member.Raise(new StaffInvited(tenantId, member.Id, userId, now));
        return member;
    }

    /// <summary>
    /// Crée le fondateur d'un tenant : actif d'emblée (personne n'existe encore pour l'inviter) et titulaire de
    /// <paramref name="ownerRole"/> pour tout le tenant, afin qu'il puisse agir avant qu'aucun établissement n'existe.
    /// </summary>
    public static StaffMember CreateOwner(
        TenantId tenantId,
        UserId userId,
        RoleReference ownerRole,
        bool isUserUniqueInTenant,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(ownerRole);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(userId);
        DomainException.ThrowIf(!isUserUniqueInTenant, StaffMemberErrors.UserAlreadyStaffInTenant);
        DomainException.ThrowIf(ownerRole.TenantId != tenantId, CommonErrors.TenantMismatch);

        var now = clock.UtcNow;
        var member = new StaffMember(StaffMemberId.New(), tenantId, userId, now)
        {
            Status = StaffStatus.Active,
            TenantWideRoleId = ownerRole.RoleId,
        };
        member.Raise(new StaffOwnerCreated(tenantId, member.Id, userId, now));
        member.Raise(new StaffActivated(tenantId, member.Id, userId, now));
        member.Raise(new StaffAssignedTenantWide(tenantId, member.Id, userId, ownerRole.RoleId, now));
        return member;
    }

    public void Activate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureHasNotLeft();
        DomainException.ThrowIf(Status != StaffStatus.Invited, StaffMemberErrors.NotInvited);

        Status = StaffStatus.Active;
        Raise(new StaffActivated(TenantId, Id, UserId, clock.UtcNow));
    }

    /// <summary>Renvoie l'invitation d'un membre qui ne l'a pas encore acceptée (nouveau lien si son compte attend un mot de passe).</summary>
    public void ResendInvitation(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureHasNotLeft();
        DomainException.ThrowIf(Status != StaffStatus.Invited, StaffMemberErrors.InvitationAlreadyAccepted);
        Raise(new StaffInvitationResent(TenantId, Id, UserId, clock.UtcNow));
    }

    public void AssignToLocation(LocationReference location, RoleReference role, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureHasNotLeft();
        DomainException.ThrowIf(Status == StaffStatus.Suspended, StaffMemberErrors.SuspendedCannotBeAssigned);
        this.EnsureSameTenant(location.TenantId);
        this.EnsureSameTenant(role.TenantId);
        // Un établissement fermé ou archivé ne prend pas de nouveau personnel ; les affectations existantes relèvent de
        // RemoveFromLocation.
        DomainException.ThrowIf(!location.IsOperational, StaffMemberErrors.LocationNotOperational);
        DomainException.ThrowIf(IsAssignedTo(location.LocationId), StaffMemberErrors.AlreadyAssignedToLocation);

        _assignments.Add(new StaffAssignment(location.LocationId, role.RoleId));
        Raise(new StaffAssignedToLocation(TenantId, Id, UserId, location.LocationId, role.RoleId, clock.UtcNow));
    }

    public void RemoveFromLocation(LocationId locationId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureHasNotLeft();

        var assignment = _assignments.Find(a => a.LocationId == locationId);
        DomainException.ThrowIf(assignment is null, StaffMemberErrors.NotAssignedToLocation);

        _assignments.Remove(assignment!);
        Raise(new StaffRemovedFromLocation(TenantId, Id, UserId, locationId, assignment!.RoleId, clock.UtcNow));
    }

    /// <summary>Donne au membre un rôle valable pour tout le tenant. Mêmes règles de statut qu'une affectation.</summary>
    public void AssignTenantWide(RoleReference role, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(role);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureHasNotLeft();
        DomainException.ThrowIf(Status == StaffStatus.Suspended, StaffMemberErrors.SuspendedCannotBeAssigned);
        this.EnsureSameTenant(role.TenantId);
        DomainException.ThrowIf(TenantWideRoleId is not null, StaffMemberErrors.AlreadyAssignedTenantWide);

        TenantWideRoleId = role.RoleId;
        Raise(new StaffAssignedTenantWide(TenantId, Id, UserId, role.RoleId, clock.UtcNow));
    }

    public void RemoveTenantWide(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureHasNotLeft();
        DomainException.ThrowIf(TenantWideRoleId is null, StaffMemberErrors.NotAssignedTenantWide);

        var removed = TenantWideRoleId!.Value;
        TenantWideRoleId = null;
        Raise(new StaffTenantWideAssignmentRemoved(TenantId, Id, UserId, removed, clock.UtcNow));
    }

    public void Suspend(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureHasNotLeft();
        DomainException.ThrowIf(Status != StaffStatus.Active, StaffMemberErrors.NotActive);

        Status = StaffStatus.Suspended;
        Raise(new StaffSuspended(TenantId, Id, UserId, clock.UtcNow));
    }

    public void Reinstate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureHasNotLeft();
        DomainException.ThrowIf(Status != StaffStatus.Suspended, StaffMemberErrors.NotSuspended);

        Status = StaffStatus.Active;
        Raise(new StaffReinstated(TenantId, Id, UserId, clock.UtcNow));
    }

    /// <summary>Terminal : le profil reste pour l'historique mais perd toute affectation, rôle de tenant compris.</summary>
    public void Leave(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureHasNotLeft();

        var removedLocationIds = _assignments.Select(a => a.LocationId).ToList().AsReadOnly();
        var heldTenantWideRole = TenantWideRoleId is not null;

        Status = StaffStatus.Left;
        PinHash = null;
        _assignments.Clear();
        TenantWideRoleId = null;
        Raise(new StaffLeft(TenantId, Id, UserId, removedLocationIds, heldTenantWideRole, clock.UtcNow));
    }

    /// <summary>Le membre choisit (ou change) son PIN de caisse ; <paramref name="pinHash"/> est déjà l'empreinte.</summary>
    public void SetPin(string pinHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pinHash);
        EnsureHasNotLeft();
        PinHash = pinHash;
        PinFailures = 0;
        PinLockedUntilUtc = null;
    }

    /// <summary>PIN retiré (par le membre, ou remis à zéro par le gérant) : il devra en choisir un autre.</summary>
    public void ClearPin()
    {
        EnsureHasNotLeft();
        PinHash = null;
        PinFailures = 0;
        PinLockedUntilUtc = null;
    }

    /// <summary>
    /// Résultat d'un essai de PIN sur une caisse partagée, la comparaison des empreintes étant faite par l'appelant.
    /// Faux : essai manqué, compté ; au <see cref="StaffPin.MaxFailures"/>e, le PIN est bloqué pendant
    /// <see cref="StaffPin.Lockout"/>. Un PIN bloqué refuse tout essai, même juste, jusqu'à la fin du blocage.
    /// </summary>
    public bool TryPin(bool matches, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureHasNotLeft();
        DomainException.ThrowIf(Status != StaffStatus.Active, StaffMemberErrors.NotActive);
        DomainException.ThrowIf(PinHash is null, StaffMemberErrors.PinNotSet);
        var now = clock.UtcNow;
        DomainException.ThrowIf(PinLockedUntilUtc > now, StaffMemberErrors.PinLocked);

        if (matches)
        {
            PinFailures = 0;
            PinLockedUntilUtc = null;
            return true;
        }

        PinFailures++;
        if (PinFailures >= StaffPin.MaxFailures)
        {
            PinFailures = 0;
            PinLockedUntilUtc = now + StaffPin.Lockout;
        }

        return false;
    }

    public bool IsAssignedTo(LocationId locationId) => _assignments.Exists(a => a.LocationId == locationId);

    /// <summary>Rôle exerçable dès maintenant sur les actions du tenant : seul un membre actif en a un.</summary>
    public RoleId? EffectiveTenantRole => Status == StaffStatus.Active ? TenantWideRoleId : null;

    /// <summary>
    /// Rôle que le membre peut exercer dès maintenant dans un établissement : seul un membre actif en a un. Le rôle
    /// affecté à cet établissement l'emporte ; à défaut, le rôle de tenant s'applique. Les deux ne sont jamais fusionnés.
    /// </summary>
    public RoleId? EffectiveRoleAt(LocationId locationId) =>
        Status == StaffStatus.Active
            ? _assignments.Find(a => a.LocationId == locationId)?.RoleId ?? TenantWideRoleId
            : null;

    private void EnsureHasNotLeft() =>
        DomainException.ThrowIf(Status == StaffStatus.Left, StaffMemberErrors.HasLeft);
}
