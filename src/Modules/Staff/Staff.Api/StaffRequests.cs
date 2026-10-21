namespace Staff.Api;

public sealed record InviteStaffRequest(string? Email, Guid? TenantWideRoleId = null);

public sealed record AssignToLocationRequest(Guid LocationId, Guid RoleId);

public sealed record TenantWideRoleRequest(Guid RoleId);

public sealed record RoleNameRequest(string? Name);

public sealed record PermissionRequest(string? Permission);

public sealed record OnboardOwnerRequest(string? Email);

public sealed record CreatedResponse(Guid Id);

public sealed record PinRequest(string? Pin);

public sealed record TillRequest(Guid LocationId, string? Name, int AutoLockMinutes = 5);

public sealed record TillChangeRequest(string? Name, int AutoLockMinutes);

public sealed record TillPinRequest(Guid StaffMemberId, string? Pin);
