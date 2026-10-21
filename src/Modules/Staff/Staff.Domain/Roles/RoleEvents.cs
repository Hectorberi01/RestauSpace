namespace Staff.Domain;

public sealed record RoleCreated(
    TenantId TenantId,
    RoleId RoleId,
    string Name,
    bool IsSystem,
    IReadOnlyCollection<PermissionCode> Permissions,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record RoleRenamed(TenantId TenantId, RoleId RoleId, string PreviousName, string Name, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record RolePermissionGranted(TenantId TenantId, RoleId RoleId, PermissionCode Permission, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record RolePermissionRevoked(TenantId TenantId, RoleId RoleId, PermissionCode Permission, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record RoleDeleted(TenantId TenantId, RoleId RoleId, string Name, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
