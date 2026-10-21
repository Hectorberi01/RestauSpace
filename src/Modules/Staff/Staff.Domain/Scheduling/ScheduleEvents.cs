namespace Staff.Domain;

/// <summary>Une version du planning est visible par l'équipe ; <see cref="AffectedMembers"/> sont à prévenir.</summary>
public sealed record ScheduleWeekPublished(
    TenantId TenantId,
    ScheduleWeekId WeekId,
    LocationId LocationId,
    DateOnly WeekStart,
    int Version,
    IReadOnlyList<Guid> AffectedMembers,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ShiftReassigned(
    TenantId TenantId,
    ScheduleWeekId WeekId,
    LocationId LocationId,
    ShiftId ShiftId,
    StaffMemberId FromMemberId,
    StaffMemberId ToMemberId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record TimeOffRequested(TenantId TenantId, TimeOffRequestId RequestId, StaffMemberId StaffMemberId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record TimeOffDecided(
    TenantId TenantId, TimeOffRequestId RequestId, StaffMemberId StaffMemberId, TimeOffStatus Status, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record ShiftSwapRequested(
    TenantId TenantId, ShiftSwapRequestId RequestId, LocationId LocationId, StaffMemberId FromMemberId, StaffMemberId ToMemberId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record ShiftSwapAccepted(TenantId TenantId, ShiftSwapRequestId RequestId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record ShiftSwapDecided(
    TenantId TenantId,
    ShiftSwapRequestId RequestId,
    LocationId LocationId,
    StaffMemberId FromMemberId,
    StaffMemberId ToMemberId,
    ShiftSwapStatus Status,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public static class ScheduleErrors
{
    public static readonly DomainError WeekStartNotMonday = new("staff.schedule.week_start_not_monday", "A schedule week starts on a Monday.");
    public static readonly DomainError OutsideWeek = new("staff.schedule.outside_week", "The shift date is outside this week.");
    public static readonly DomainError ShiftTooLong = new("staff.schedule.shift_too_long", "A shift lasts 16 hours at most.");
    public static readonly DomainError ShiftTooShort = new("staff.schedule.shift_too_short", "A shift lasts 15 minutes at least.");
    public static readonly DomainError BreakInvalid = new("staff.schedule.break_invalid", "The break must be between 0 and 180 minutes and shorter than the shift.");
    public static readonly DomainError PositionTooLong = new("staff.schedule.position_too_long", "A position name is 40 characters at most.");
    public static readonly DomainError NoteTooLong = new("staff.schedule.note_too_long", "The note is 200 characters at most.");
    public static readonly DomainError Overlap = new("staff.schedule.overlap", "This member already works at that time.", DomainErrorKind.Conflict);
    public static readonly DomainError ShiftNotFound = new("staff.schedule.shift_not_found", "This shift is not in the schedule.");
    public static readonly DomainError TooManyShifts = new("staff.schedule.too_many_shifts", "A week holds 1000 shifts at most.");
    public static readonly DomainError WeekNotEmpty = new("staff.schedule.week_not_empty", "Copy only into an empty week.", DomainErrorKind.Conflict);
    public static readonly DomainError NothingToCopy = new("staff.schedule.nothing_to_copy", "The source week has no shift to copy.");
    public static readonly DomainError CopySameWeek = new("staff.schedule.copy_same_week", "Pick another week to copy from.");
    public static readonly DomainError OtherLocation = new("staff.schedule.other_location", "Both weeks must belong to the same location.");
    public static readonly DomainError NothingToPublish = new("staff.schedule.nothing_to_publish", "There is no change to publish.");
    public static readonly DomainError ShiftNotPublished = new("staff.schedule.shift_not_published", "This shift is not in the published schedule.");
    public static readonly DomainError SameMember = new("staff.schedule.same_member", "Pick another colleague.");
    public static readonly DomainError NotYourShift = new("staff.schedule.not_your_shift", "This shift belongs to someone else.");
    public static readonly DomainError NotForYou = new("staff.schedule.not_for_you", "This request is for another colleague.");
    public static readonly DomainError ShiftStarted = new("staff.schedule.shift_started", "This shift has already started.");
    public static readonly DomainError SwapClosed = new("staff.schedule.swap_closed", "This swap request is already settled.", DomainErrorKind.Conflict);
    public static readonly DomainError TimeOffKindInvalid = new("staff.time_off.kind_invalid", "Unknown absence kind.");
    public static readonly DomainError TimeOffPeriodInvalid = new("staff.time_off.period_invalid", "An absence lasts 1 to 92 days, its last day after its first.");
    public static readonly DomainError TimeOffInPast = new("staff.time_off.in_past", "An absence cannot start in the past.");
    public static readonly DomainError TimeOffClosed = new("staff.time_off.closed", "This absence is already settled.", DomainErrorKind.Conflict);
    public static readonly DomainError WeeklyLimitInvalid = new("staff.schedule.weekly_limit_invalid", "The weekly limit is between 1 and 80 hours.");
    public static readonly DomainError TooManyPositions = new("staff.schedule.too_many_positions", "20 positions at most.");
}
