namespace Staff.Domain;

public static class StaffMemberErrors
{
    public static readonly DomainError UserAlreadyStaffInTenant = new("staff.staff_member.user_already_in_tenant", "This user already has a staff profile in this tenant.", DomainErrorKind.Conflict);
    public static readonly DomainError HasLeft = new("staff.staff_member.has_left", "A staff member who left the tenant can no longer act or be changed.");
    public static readonly DomainError InvitationAlreadyAccepted = new("staff.staff_member.invitation_already_accepted", "This staff member has already accepted the invitation.");
    public static readonly DomainError NotInvited = new("staff.staff_member.not_invited", "Only an invited staff member can be activated.");
    public static readonly DomainError NotActive = new("staff.staff_member.not_active", "Only an active staff member can be suspended.");
    public static readonly DomainError NotSuspended = new("staff.staff_member.not_suspended", "Only a suspended staff member can be reinstated.");
    public static readonly DomainError SuspendedCannotBeAssigned = new("staff.staff_member.suspended_cannot_be_assigned", "A suspended staff member cannot be assigned to a location.");
    public static readonly DomainError LocationNotOperational = new("staff.staff_member.location_not_operational", "Staff can only be assigned to an operational location.");
    public static readonly DomainError AlreadyAssignedToLocation = new("staff.staff_member.already_assigned_to_location", "The staff member is already assigned to this location.", DomainErrorKind.Conflict);
    public static readonly DomainError NotAssignedToLocation = new("staff.staff_member.not_assigned_to_location", "The staff member is not assigned to this location.");
    public static readonly DomainError AlreadyAssignedTenantWide = new("staff.staff_member.already_assigned_tenant_wide", "The staff member already holds a tenant-wide role.");
    public static readonly DomainError NotAssignedTenantWide = new("staff.staff_member.not_assigned_tenant_wide", "The staff member holds no tenant-wide role.");
    public static readonly DomainError PinNotSet = new("staff.pin.not_set", "This staff member has not chosen a till PIN yet.");
    public static readonly DomainError PinLocked = new("staff.pin.locked", "Too many wrong PINs: this PIN is blocked for a few minutes.");
    public static readonly DomainError PinInvalidFormat = new("staff.pin.invalid_format", "A PIN has 4 to 6 digits.");
    public static readonly DomainError PinTooSimple = new("staff.pin.too_simple", "This PIN is too easy to guess (same or consecutive digits).");
}
