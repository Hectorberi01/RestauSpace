namespace Staff.Application;

/// <summary>Erreurs levées par les cas d'usage de Staff (celles des règles métier sont dans Staff.Domain).</summary>
public static class StaffApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("staff.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    // C'est aussi la réponse pour un membre, rôle ou établissement d'un autre tenant : ne jamais révéler qu'il existe.
    public static readonly DomainError MemberNotFound = new("staff.staff_member.not_found", "The staff member does not exist.");
    public static readonly DomainError RoleNotFound = new("staff.role.not_found", "The role does not exist.");
    public static readonly DomainError LocationNotFound = new("staff.location.not_found", "The location does not exist.");
    public static readonly DomainError TenantNotFound = new("staff.tenant.not_found", "The restaurant account does not exist.");
    public static readonly DomainError InvitationNotFound =
        new("staff.invitation.not_found", "There is no invitation of this restaurant for you.");

    public static readonly DomainError TenantNotWritable =
        new("staff.tenant.not_writable", "The restaurant account is suspended or closed: its staff can be read, not changed.");

    public static readonly DomainError PlanLimitReached =
        new("staff.plan.limit_reached", "The plan's staff limit is reached: remove a member or upgrade the plan.");

    public static readonly DomainError LastOwner =
        new("staff.staff_member.last_owner", "The restaurant must keep at least one active owner.");

    public static readonly DomainError InvalidAccessToken =
        new("staff.auth.invalid_access_token", "The access token does not identify a user.");

    public static readonly DomainError PagingInvalid =
        new("staff.paging.invalid", "Page must be 1 or more and page size between 1 and 100.");

    public static readonly DomainError TillNotFound = new("staff.till.not_found", "The till does not exist.");

    // Même réponse pour une clé inconnue, mal formée ou révoquée.
    public static readonly DomainError TillKeyInvalid = new("staff.till.invalid_key", "This device is not (or no longer) a registered till.");

    public static readonly DomainError PinWrong = new("staff.pin.wrong", "Wrong PIN.");

    public static readonly DomainError NotAtThisLocation =
        new("staff.till.member_not_here", "This staff member does not work at the location of this till.");

    public static readonly DomainError SessionRefused =
        new("staff.till.session_refused", "This staff member's account cannot sign in right now.");

    public static readonly DomainError TimeRangeInvalid =
        new("staff.time_entries.range_invalid", "The period must end after it starts and last at most 92 days.");

    public static readonly DomainError ScheduleLocationNotFound = new("staff.schedule.location_not_found", "The location does not exist.");
    public static readonly DomainError ScheduleWeekNotFound = new("staff.schedule.week_not_found", "Nothing is planned for this week yet.");
    public static readonly DomainError ShiftNotFound = new("staff.schedule.shift_not_found", "This shift is not in the schedule.");
    public static readonly DomainError TimeOffNotFound = new("staff.time_off.not_found", "This absence does not exist.");
    public static readonly DomainError ShiftSwapNotFound = new("staff.shift_swap.not_found", "This swap request does not exist.");
    public static readonly DomainError SwapAlreadyOpen =
        new("staff.shift_swap.already_open", "A swap is already pending for this shift.", DomainErrorKind.Conflict);

    public static readonly DomainError ColleagueNotHere =
        new("staff.shift_swap.colleague_not_here", "This colleague does not work at this location.");
}
