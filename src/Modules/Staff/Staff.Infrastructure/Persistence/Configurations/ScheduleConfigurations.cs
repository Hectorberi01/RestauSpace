using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Staff.Domain;

namespace Staff.Infrastructure.Persistence.Configurations;

/// <summary>Les créneaux d'une semaine en une valeur jsonb, relus par <see cref="Shift.Restore"/>.</summary>
internal static class ScheduleJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static readonly ValueConverter<List<Shift>, string> Shifts = new(shifts => Write(shifts), json => Read(json));

    public static readonly ValueConverter<IReadOnlyList<string>, string> Texts = new(
        texts => JsonSerializer.Serialize(texts.ToList(), Options),
        json => JsonSerializer.Deserialize<List<string>>(json, Options) ?? new List<string>());

    public static ValueComparer<List<Shift>> ShiftComparer { get; } = new(
        (left, right) => left!.SequenceEqual(right!),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
        list => list.ToList());

    public static ValueComparer<IReadOnlyList<string>> TextComparer { get; } = new(
        (left, right) => left!.SequenceEqual(right!),
        list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
        list => list.ToList());

    private static string Write(List<Shift> shifts) => JsonSerializer.Serialize(
        shifts.Select(shift => new ShiftRow(
                shift.Id.Value,
                shift.StaffMemberId.Value,
                shift.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                shift.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
                shift.End.ToString("HH:mm", CultureInfo.InvariantCulture),
                shift.BreakMinutes,
                shift.Position,
                shift.Note))
            .ToList(),
        Options);

    private static List<Shift> Read(string json) =>
        (JsonSerializer.Deserialize<List<ShiftRow>>(json, Options) ?? new List<ShiftRow>())
            .Select(row => Shift.Restore(
                ShiftId.From(row.Id),
                StaffMemberId.From(row.Member),
                DateOnly.ParseExact(row.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                TimeOnly.ParseExact(row.Start, "HH:mm", CultureInfo.InvariantCulture),
                TimeOnly.ParseExact(row.End, "HH:mm", CultureInfo.InvariantCulture),
                row.Break,
                row.Position,
                row.Note))
            .ToList();

    internal sealed record ShiftRow(Guid Id, Guid Member, string Date, string Start, string End, int Break, string? Position, string? Note);
}

internal sealed class ScheduleWeekConfiguration : IEntityTypeConfiguration<ScheduleWeek>
{
    public const string WeekIndex = "ix_schedule_weeks_tenant_id_location_id_week_start";

    public void Configure(EntityTypeBuilder<ScheduleWeek> builder)
    {
        builder.ToTable("schedule_weeks");
        builder.HasKey(week => week.Id);
        builder.Property(week => week.Id).HasColumnName("id").HasConversion(id => id.Value, value => ScheduleWeekId.From(value)).ValueGeneratedNever();
        builder.Property(week => week.TenantId).HasColumnName("tenant_id").HasConversion(StaffConverters.Tenant);
        builder.Property(week => week.LocationId).HasColumnName("location_id").HasConversion(StaffConverters.Location);
        builder.Property(week => week.WeekStart).HasColumnName("week_start");
        builder.Property(week => week.PublishedVersion).HasColumnName("published_version");
        builder.Property(week => week.PublishedAtUtc).HasColumnName("published_at_utc");
        builder.Property(week => week.UpdatedAtUtc).HasColumnName("updated_at_utc");
        builder.Property<List<Shift>>("_shifts")
            .HasColumnName("shifts")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(ScheduleJson.Shifts, ScheduleJson.ShiftComparer);
        builder.Property<List<Shift>>("_published")
            .HasColumnName("published_shifts")
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb")
            .HasConversion(ScheduleJson.Shifts, ScheduleJson.ShiftComparer);
        builder.HasIndex(week => new { week.TenantId, week.LocationId, week.WeekStart }).IsUnique().HasDatabaseName(WeekIndex);
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(week => week.Shifts);
        builder.Ignore(week => week.PublishedShifts);
        builder.Ignore(week => week.WeekEnd);
        builder.Ignore(week => week.IsPublished);
        builder.Ignore(week => week.HasUnpublishedChanges);
        builder.Ignore(week => week.DomainEvents);
    }
}

internal sealed class ScheduleSettingsConfiguration : IEntityTypeConfiguration<ScheduleSettings>
{
    public void Configure(EntityTypeBuilder<ScheduleSettings> builder)
    {
        builder.ToTable("schedule_settings");
        builder.HasKey(settings => new { settings.TenantId, settings.LocationId });
        builder.Property(settings => settings.TenantId).HasColumnName("tenant_id").HasConversion(StaffConverters.Tenant);
        builder.Property(settings => settings.LocationId).HasColumnName("location_id").HasConversion(StaffConverters.Location);
        builder.Property(settings => settings.Positions)
            .HasColumnName("positions")
            .HasColumnType("jsonb")
            .HasConversion(ScheduleJson.Texts, ScheduleJson.TextComparer);
        builder.Property(settings => settings.WeeklyHoursLimit).HasColumnName("weekly_hours_limit");
        builder.Property<uint>("Version").IsRowVersion();
    }
}

internal sealed class TimeOffRequestConfiguration : IEntityTypeConfiguration<TimeOffRequest>
{
    public void Configure(EntityTypeBuilder<TimeOffRequest> builder)
    {
        builder.ToTable("time_off_requests");
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).HasColumnName("id").HasConversion(id => id.Value, value => TimeOffRequestId.From(value)).ValueGeneratedNever();
        builder.Property(request => request.TenantId).HasColumnName("tenant_id").HasConversion(StaffConverters.Tenant);
        builder.Property(request => request.StaffMemberId).HasColumnName("staff_member_id").HasConversion(StaffConverters.Member);
        builder.Property(request => request.FirstDay).HasColumnName("first_day");
        builder.Property(request => request.LastDay).HasColumnName("last_day");
        builder.Property(request => request.Kind).HasColumnName("kind");
        builder.Property(request => request.Comment).HasColumnName("comment").HasMaxLength(TimeOffRequest.CommentMaxLength);
        builder.Property(request => request.Status).HasColumnName("status");
        builder.Property(request => request.RequestedAtUtc).HasColumnName("requested_at_utc");
        builder.Property(request => request.DecidedAtUtc).HasColumnName("decided_at_utc");
        builder.Property(request => request.DecisionNote).HasColumnName("decision_note").HasMaxLength(TimeOffRequest.CommentMaxLength);
        builder.HasIndex(request => new { request.TenantId, request.LastDay }).HasDatabaseName("ix_time_off_requests_tenant_id_last_day");
        builder.HasIndex(request => new { request.TenantId, request.StaffMemberId }).HasDatabaseName("ix_time_off_requests_tenant_id_staff_member_id");
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(request => request.DomainEvents);
    }
}

internal sealed class ShiftSwapRequestConfiguration : IEntityTypeConfiguration<ShiftSwapRequest>
{
    public void Configure(EntityTypeBuilder<ShiftSwapRequest> builder)
    {
        builder.ToTable("shift_swap_requests");
        builder.HasKey(request => request.Id);
        builder.Property(request => request.Id).HasColumnName("id").HasConversion(id => id.Value, value => ShiftSwapRequestId.From(value)).ValueGeneratedNever();
        builder.Property(request => request.TenantId).HasColumnName("tenant_id").HasConversion(StaffConverters.Tenant);
        builder.Property(request => request.LocationId).HasColumnName("location_id").HasConversion(StaffConverters.Location);
        builder.Property(request => request.WeekId).HasColumnName("week_id").HasConversion(id => id.Value, value => ScheduleWeekId.From(value));
        builder.Property(request => request.ShiftId).HasColumnName("shift_id").HasConversion(id => id.Value, value => ShiftId.From(value));
        builder.Property(request => request.FromMemberId).HasColumnName("from_member_id").HasConversion(StaffConverters.Member);
        builder.Property(request => request.ToMemberId).HasColumnName("to_member_id").HasConversion(StaffConverters.Member);
        builder.Property(request => request.ShiftStartsAtUtc).HasColumnName("shift_starts_at_utc");
        builder.Property(request => request.Note).HasColumnName("note").HasMaxLength(ShiftSwapRequest.NoteMaxLength);
        builder.Property(request => request.Status).HasColumnName("status");
        builder.Property(request => request.RequestedAtUtc).HasColumnName("requested_at_utc");
        builder.Property(request => request.DecidedAtUtc).HasColumnName("decided_at_utc");
        builder.HasIndex(request => new { request.TenantId, request.ShiftStartsAtUtc }).HasDatabaseName("ix_shift_swap_requests_tenant_id_shift_starts_at_utc");
        builder.HasIndex(request => new { request.TenantId, request.ShiftId }).HasDatabaseName("ix_shift_swap_requests_tenant_id_shift_id");
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(request => request.IsOpen);
        builder.Ignore(request => request.DomainEvents);
    }
}
