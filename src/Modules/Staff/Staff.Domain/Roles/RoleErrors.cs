namespace Staff.Domain;

public static class RoleErrors
{
    public static readonly DomainError NameRequired = new("staff.role.name_required", "Role name is required.");
    public static readonly DomainError NameTooLong = new("staff.role.name_too_long", "Role name is too long.");
    public static readonly DomainError NameAlreadyUsed = new("staff.role.name_already_used", "Another role of this tenant already has this name.", DomainErrorKind.Conflict);
    public static readonly DomainError NotSystemRole = new("staff.role.not_system_role", "Only a system role is completed by the platform.");
    public static readonly DomainError SystemRoleProtected = new("staff.role.system_role_protected", "A system role cannot be renamed nor have its permissions changed.");
    public static readonly DomainError InUse = new("staff.role.in_use", "The role is still held by staff members.", DomainErrorKind.Conflict);
    public static readonly DomainError PermissionUnknown = new("staff.role.permission_unknown", "This permission is not part of the known permission catalog.");
    public static readonly DomainError PermissionAlreadyGranted = new("staff.role.permission_already_granted", "The role already has this permission.");
    public static readonly DomainError PermissionNotGranted = new("staff.role.permission_not_granted", "The role does not have this permission.");
    public static readonly DomainError PermissionCodeInvalid = new("staff.permission_code.invalid_format", "A permission code is made of lowercase segments separated by dots, e.g. orders.cancel.");
    public static readonly DomainError PermissionCatalogEmpty = new("staff.permission_catalog.empty", "A permission catalog must contain at least one permission.");
}
