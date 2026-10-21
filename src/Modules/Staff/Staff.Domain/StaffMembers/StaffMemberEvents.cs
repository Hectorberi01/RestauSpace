namespace Staff.Domain;

public sealed record StaffInvited(TenantId TenantId, StaffMemberId StaffMemberId, UserId UserId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record StaffInvitationResent(TenantId TenantId, StaffMemberId StaffMemberId, UserId UserId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record StaffActivated(TenantId TenantId, StaffMemberId StaffMemberId, UserId UserId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record StaffAssignedToLocation(
    TenantId TenantId,
    StaffMemberId StaffMemberId,
    UserId UserId,
    LocationId LocationId,
    RoleId RoleId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record StaffRemovedFromLocation(
    TenantId TenantId,
    StaffMemberId StaffMemberId,
    UserId UserId,
    LocationId LocationId,
    RoleId RoleId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record StaffSuspended(TenantId TenantId, StaffMemberId StaffMemberId, UserId UserId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record StaffReinstated(TenantId TenantId, StaffMemberId StaffMemberId, UserId UserId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <summary>
/// Liste ce que le départ a retiré (établissements auxquels le membre était affecté, et s'il détenait un rôle sur
/// tout le tenant) pour que les projections puissent être mises à jour sans recharger le membre.
/// </summary>
public sealed record StaffLeft(
    TenantId TenantId,
    StaffMemberId StaffMemberId,
    UserId UserId,
    IReadOnlyCollection<LocationId> RemovedLocationIds,
    bool HeldTenantWideRole,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Le fondateur d'un tenant a reçu un profil directement, sans invitation. Suivi de <see cref="StaffActivated"/> et <see cref="StaffAssignedTenantWide"/>.</summary>
public sealed record StaffOwnerCreated(TenantId TenantId, StaffMemberId StaffMemberId, UserId UserId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record StaffAssignedTenantWide(
    TenantId TenantId,
    StaffMemberId StaffMemberId,
    UserId UserId,
    RoleId RoleId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record StaffTenantWideAssignmentRemoved(
    TenantId TenantId,
    StaffMemberId StaffMemberId,
    UserId UserId,
    RoleId RoleId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
