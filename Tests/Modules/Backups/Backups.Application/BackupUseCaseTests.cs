using Backups.Application.Abstractions;
using Backups.Application.Commands;
using Backups.Application.Queries;
using Backups.Domain;

namespace Backups.Application;

public sealed class BackupUseCaseTests
{
    private readonly BackupsFixture _fixture = new();

    private Guid Admin => _fixture.Admin.Value;

    private static async Task<DomainException> RefusedAsync(DomainError expected, Func<Task> action)
    {
        var exception = await Assert.ThrowsAsync<DomainException>(action);
        Assert.Equal(expected.Code, exception.Code);
        return exception;
    }

    [Fact]
    public async Task A_manual_backup_is_requested_pending_and_audited()
    {
        var backup = await _fixture.Request.HandleAsync(new RequestBackupCommand(Admin));

        Assert.Equal(("Pending", "Manual", (Guid?)Admin, "es_restaurant"), (backup.Status, backup.Trigger, backup.RequestedBy, backup.DatabaseName));
        Assert.Equal(_fixture.Clock.UtcNow.AddDays(30), backup.ExpiresAtUtc);
        Assert.Equal(backup.Id, Assert.IsType<BackupRequested>(Assert.Single(_fixture.Outbox)).BackupId.Value);
    }

    [Fact]
    public async Task A_second_backup_is_refused_while_one_is_active()
    {
        _ = _fixture.GivenActive();

        await RefusedAsync(BackupErrors.AlreadyInProgress, () => _fixture.Request.HandleAsync(new RequestBackupCommand(Admin)));
        Assert.Single(_fixture.Backups);
    }

    [Fact]
    public async Task A_concurrent_request_stopped_by_the_unique_index_gets_the_same_error()
    {
        _ = _fixture.GivenActive();
        _fixture.HideActive = true;

        await RefusedAsync(BackupErrors.AlreadyInProgress, () => _fixture.Request.HandleAsync(new RequestBackupCommand(Admin)));
        Assert.Single(_fixture.Backups);
    }

    [Fact]
    public async Task No_backup_is_requested_during_a_restore()
    {
        _ = _fixture.GivenPendingRestore();

        await RefusedAsync(BackupErrors.RestoreInProgress, () => _fixture.Request.HandleAsync(new RequestBackupCommand(Admin)));
    }

    [Fact]
    public async Task The_scheduled_backup_is_requested_once_per_daily_slot()
    {
        // FakeClock : 15 janvier, 12:00 UTC ; créneau 02:30.
        var first = await _fixture.Schedule.HandleAsync(new RunScheduledBackupCommand());
        AgentWrites.Completed(_fixture.Backups.Single());
        var again = await _fixture.Schedule.HandleAsync(new RunScheduledBackupCommand());
        _fixture.Clock.Advance(TimeSpan.FromHours(14));
        var beforeNextSlot = await _fixture.Schedule.HandleAsync(new RunScheduledBackupCommand());
        _fixture.Clock.Advance(TimeSpan.FromMinutes(31));
        var nextDay = await _fixture.Schedule.HandleAsync(new RunScheduledBackupCommand());

        Assert.Equal(("Scheduled", (Guid?)null), (first!.Trigger, first.RequestedBy));
        Assert.Null(again);
        Assert.Null(beforeNextSlot);
        Assert.NotNull(nextDay);
        Assert.Equal(2, _fixture.Backups.Count);
    }

    [Fact]
    public async Task The_schedule_waits_while_a_backup_or_a_restore_is_active_and_can_be_disabled()
    {
        var active = _fixture.GivenActive();
        Assert.Null(await _fixture.Schedule.HandleAsync(new RunScheduledBackupCommand()));

        _fixture.HideActive = true;
        Assert.Null(await _fixture.Schedule.HandleAsync(new RunScheduledBackupCommand()));

        _fixture.HideActive = false;
        AgentWrites.Completed(active);
        _fixture.Settings.ScheduleEnabled = false;
        Assert.Null(await _fixture.Schedule.HandleAsync(new RunScheduledBackupCommand()));
        Assert.Single(_fixture.Backups);
    }

    [Fact]
    public async Task Deleting_keeps_the_minimum_number_of_completed_backups()
    {
        var oldest = _fixture.GivenCompleted(_fixture.Clock.UtcNow.AddDays(-2));
        _ = _fixture.GivenCompleted(_fixture.Clock.UtcNow.AddDays(-1));

        await RefusedAsync(BackupErrors.KeepMinReached, () => _fixture.Delete.HandleAsync(new DeleteBackupCommand(oldest.Id.Value, Admin)));

        _ = _fixture.GivenCompleted();
        var deleting = await _fixture.Delete.HandleAsync(new DeleteBackupCommand(oldest.Id.Value, Admin));

        Assert.Equal("Deleting", deleting.Status);
        Assert.Equal(_fixture.Admin, Assert.IsType<BackupDeletionRequested>(Assert.Single(_fixture.Outbox)).RequestedBy);
        Assert.Equal(2, _fixture.RetentionLocks);
    }

    [Fact]
    public async Task Only_a_completed_backup_is_deleted()
    {
        var active = _fixture.GivenActive();

        await RefusedAsync(BackupErrors.NotCompleted, () => _fixture.Delete.HandleAsync(new DeleteBackupCommand(active.Id.Value, Admin)));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Delete.HandleAsync(new DeleteBackupCommand(Guid.NewGuid(), Admin)));
    }

    [Fact]
    public async Task A_download_link_needs_a_valid_two_factor_code()
    {
        var backup = _fixture.GivenCompleted();

        await RefusedAsync(
            BackupsApplicationErrors.TwoFactorInvalid,
            () => _fixture.DownloadLink.HandleAsync(new CreateDownloadLinkCommand(backup.Id.Value, Admin, "000000")));
        await RefusedAsync(
            BackupsApplicationErrors.TwoFactorInvalid, () => _fixture.DownloadLink.HandleAsync(new CreateDownloadLinkCommand(backup.Id.Value, Admin, " ")));

        Assert.Equal((Admin, (string?)"000000"), Assert.Single(_fixture.TwoFactorCalls));
        Assert.Empty(_fixture.Outbox);
    }

    [Fact]
    public async Task A_download_link_is_presigned_for_five_minutes_and_audited_without_the_url()
    {
        var backup = _fixture.GivenCompleted();

        var link = await _fixture.DownloadLink.HandleAsync(new CreateDownloadLinkCommand(backup.Id.Value, Admin, BackupsFixture.ValidCode));

        Assert.StartsWith($"https://storage.test/bucket/{backup.StorageKey}?X-Amz-Expires=300", link.Url);
        Assert.Equal(_fixture.Clock.UtcNow.AddMinutes(5), link.ExpiresAtUtc);
        var audited = Assert.IsType<BackupDownloadLinkCreated>(Assert.Single(_fixture.Outbox));
        Assert.Equal((backup.Id, _fixture.Admin, link.ExpiresAtUtc), (audited.BackupId, audited.RequestedBy, audited.ExpiresAtUtc));
        Assert.DoesNotContain("storage.test", audited.ToString());
        Assert.DoesNotContain("storage.test", link.ToString());
    }

    [Fact]
    public async Task A_download_link_is_refused_before_the_code_is_used()
    {
        var active = _fixture.GivenActive();
        var completed = _fixture.GivenCompleted();
        AgentWrites.Set(completed, nameof(Backup.StorageKey), "../../etc/passwd");

        await RefusedAsync(
            BackupErrors.NotCompleted, () => _fixture.DownloadLink.HandleAsync(new CreateDownloadLinkCommand(active.Id.Value, Admin, BackupsFixture.ValidCode)));
        await RefusedAsync(
            BackupErrors.NotCompleted,
            () => _fixture.DownloadLink.HandleAsync(new CreateDownloadLinkCommand(completed.Id.Value, Admin, BackupsFixture.ValidCode)));
        _fixture.StorageConfigured = false;
        await RefusedAsync(
            BackupsApplicationErrors.StorageNotConfigured,
            () => _fixture.DownloadLink.HandleAsync(new CreateDownloadLinkCommand(completed.Id.Value, Admin, BackupsFixture.ValidCode)));

        Assert.Empty(_fixture.TwoFactorCalls);
    }

    [Fact]
    public async Task Too_many_two_factor_attempts_are_reported_as_such()
    {
        var backup = _fixture.GivenCompleted();
        _fixture.TwoFactorFailure = new TooManyRequestsException(new DomainError("identity.auth.too_many_attempts", "x"), TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<TooManyRequestsException>(
            () => _fixture.DownloadLink.HandleAsync(new CreateDownloadLinkCommand(backup.Id.Value, Admin, BackupsFixture.ValidCode)));
    }

    [Fact]
    public async Task A_restore_is_pending_for_the_cancellation_delay_and_audited()
    {
        var backup = _fixture.GivenCompleted();

        var restore = await _fixture.Restore.HandleAsync(new RequestRestoreCommand(backup.Id.Value, Admin, BackupsFixture.ValidCode, "es_restaurant"));

        Assert.Equal(("Pending", backup.Id.Value, "es_restaurant"), (restore.Status, restore.BackupId, restore.DatabaseName));
        Assert.Equal(_fixture.Clock.UtcNow.AddMinutes(5), restore.NotBeforeUtc);
        Assert.IsType<RestoreRequested>(Assert.Single(_fixture.Outbox));
    }

    [Fact]
    public async Task A_restore_needs_the_exact_database_name_and_a_valid_code()
    {
        var backup = _fixture.GivenCompleted();

        await RefusedAsync(
            BackupErrors.ConfirmationMismatch,
            () => _fixture.Restore.HandleAsync(new RequestRestoreCommand(backup.Id.Value, Admin, BackupsFixture.ValidCode, "es_restaurant_x")));
        await RefusedAsync(
            BackupsApplicationErrors.TwoFactorInvalid,
            () => _fixture.Restore.HandleAsync(new RequestRestoreCommand(backup.Id.Value, Admin, "999999", "es_restaurant")));

        Assert.Empty(_fixture.Restores);
        Assert.Single(_fixture.TwoFactorCalls);
    }

    [Fact]
    public async Task A_restore_is_refused_while_a_backup_or_a_restore_is_active()
    {
        var backup = _fixture.GivenCompleted();
        var active = _fixture.GivenActive();

        await RefusedAsync(
            BackupErrors.AlreadyInProgress,
            () => _fixture.Restore.HandleAsync(new RequestRestoreCommand(backup.Id.Value, Admin, BackupsFixture.ValidCode, "es_restaurant")));

        AgentWrites.Completed(active);
        _ = _fixture.GivenPendingRestore();
        await RefusedAsync(
            BackupErrors.RestoreInProgress,
            () => _fixture.Restore.HandleAsync(new RequestRestoreCommand(backup.Id.Value, Admin, BackupsFixture.ValidCode, "es_restaurant")));

        _fixture.HideActive = true;
        await RefusedAsync(
            BackupErrors.RestoreInProgress,
            () => _fixture.Restore.HandleAsync(new RequestRestoreCommand(backup.Id.Value, Admin, BackupsFixture.ValidCode, "es_restaurant")));
        Assert.Single(_fixture.Restores);
    }

    [Fact]
    public async Task Only_a_pending_restore_is_cancelled()
    {
        var restore = _fixture.GivenPendingRestore();

        var cancelled = await _fixture.Cancel.HandleAsync(new CancelRestoreCommand(restore.Id.Value, Admin));

        Assert.Equal("Cancelled", cancelled.Status);
        Assert.IsType<RestoreCancelled>(Assert.Single(_fixture.Outbox));
        await RefusedAsync(BackupErrors.NotCancellable, () => _fixture.Cancel.HandleAsync(new CancelRestoreCommand(restore.Id.Value, Admin)));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Cancel.HandleAsync(new CancelRestoreCommand(Guid.NewGuid(), Admin)));
    }

    [Fact]
    public async Task Maintenance_times_out_work_without_heartbeat()
    {
        var stale = AgentWrites.Running(_fixture.GivenActive(), _fixture.Clock.UtcNow.AddHours(-2));
        var restore = AgentWrites.Running(_fixture.GivenPendingRestore(), _fixture.Clock.UtcNow.AddHours(-2), _fixture.Clock.UtcNow.AddMinutes(-61));

        var report = await _fixture.Maintenance.HandleAsync(new RunMaintenanceCommand());

        Assert.Equal((1, 1), (report.BackupsTimedOut, report.RestoresTimedOut));
        Assert.Equal((BackupStatus.Failed, FailureCodes.Timeout), (stale.Status, stale.FailureCode));
        Assert.Equal((RestoreStatus.Failed, FailureCodes.Timeout), (restore.Status, restore.FailureCode));
    }

    [Fact]
    public async Task Maintenance_skips_a_row_the_agent_wrote_meanwhile()
    {
        _ = AgentWrites.Running(_fixture.GivenActive(), _fixture.Clock.UtcNow.AddHours(-2));
        _fixture.ConcurrentWrites = 1;

        var report = await _fixture.Maintenance.HandleAsync(new RunMaintenanceCommand());

        Assert.Equal(0, report.BackupsTimedOut);
    }

    [Fact]
    public async Task Retention_deletes_expired_backups_but_always_keeps_the_minimum()
    {
        var now = _fixture.Clock.UtcNow;
        var oldest = _fixture.GivenCompleted(now.AddDays(-40));
        var older = _fixture.GivenCompleted(now.AddDays(-35));
        var old = _fixture.GivenCompleted(now.AddDays(-32));
        var recent = _fixture.GivenCompleted(now.AddDays(-1));

        var report = await _fixture.Maintenance.HandleAsync(new RunMaintenanceCommand());

        // KeepMin = 2 : la plus récente et « old » (expirée) restent.
        Assert.Equal(2, report.BackupsExpired);
        Assert.Equal(
            (BackupStatus.Deleting, BackupStatus.Deleting, BackupStatus.Completed, BackupStatus.Completed),
            (oldest.Status, older.Status, old.Status, recent.Status));
        Assert.All(_fixture.Outbox, message => Assert.Null(Assert.IsType<BackupDeletionRequested>(message).RequestedBy));
    }

    [Fact]
    public async Task The_agent_is_online_when_seen_within_three_minutes()
    {
        Assert.Equal(new BackupAgentDto(null, null, null, null, false), await _fixture.AgentStatus.HandleAsync(new GetBackupAgentQuery()));

        _fixture.Agent = new AgentHeartbeat("vps-31d9dc8a", "1.0.0", _fixture.Clock.UtcNow.AddMinutes(-2), RestoreEnabled: false);
        var online = await _fixture.AgentStatus.HandleAsync(new GetBackupAgentQuery());
        _fixture.Clock.Advance(TimeSpan.FromMinutes(2));
        var offline = await _fixture.AgentStatus.HandleAsync(new GetBackupAgentQuery());

        Assert.Equal(("vps-31d9dc8a", true, (bool?)false), (online.Host, online.Online, online.RestoreEnabled));
        Assert.False(offline.Online);
    }

    [Fact]
    public async Task Settings_show_the_schedule_and_whether_the_storage_is_configured()
    {
        var settings = await _fixture.SettingsQuery.HandleAsync(new GetBackupSettingsQuery());

        Assert.Equal(new BackupSettingsDto("es_restaurant", 30, 2, "02:30", 5, true), settings);
    }

    [Fact]
    public async Task Lists_are_paged_most_recent_first()
    {
        var older = _fixture.GivenCompleted(_fixture.Clock.UtcNow.AddDays(-1));
        var newer = _fixture.GivenCompleted();

        var page = await _fixture.List.HandleAsync(new ListBackupsQuery(1, 1));

        Assert.Equal(newer.Id.Value, Assert.Single(page.Items).Id);
        Assert.Equal((2, true), (page.TotalCount, page.HasMore));
        Assert.Equal(older.Id.Value, (await _fixture.Get.HandleAsync(new GetBackupQuery(older.Id.Value))).Id);
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Get.HandleAsync(new GetBackupQuery(Guid.NewGuid())));
    }

    [Fact]
    public void Commands_never_show_the_code()
    {
        Assert.DoesNotContain("123456", new CreateDownloadLinkCommand(Guid.NewGuid(), Admin, "123456").ToString());
        Assert.DoesNotContain("123456", new RequestRestoreCommand(Guid.NewGuid(), Admin, "123456", "es_restaurant").ToString());
    }

    [Theory]
    [InlineData("02:30", "2026-01-15T12:00:00Z", "2026-01-15T02:30:00Z")]
    [InlineData("02:30", "2026-01-15T02:29:00Z", "2026-01-14T02:30:00Z")]
    [InlineData("23:59", "2026-01-15T12:00:00Z", "2026-01-14T23:59:00Z")]
    public void The_last_slot_is_today_once_passed_otherwise_yesterday(string dailyAt, string now, string expected)
    {
        var settings = new BackupsSettings { DatabaseName = "x", DailyAtUtc = TimeOnly.Parse(dailyAt, System.Globalization.CultureInfo.InvariantCulture) };

        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), settings.LastSlotAt(DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
