namespace Backups.Domain;

public sealed class BackupTests
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private readonly FakeClock _clock = new();
    private readonly UserId _admin = UserId.New();

    private Backup Manual() => Backup.RequestManual("es_restaurant", _admin, Retention, _clock);

    [Fact]
    public void A_manual_request_is_pending_and_expires_after_the_retention()
    {
        var backup = Manual();

        Assert.Equal(BackupStatus.Pending, backup.Status);
        Assert.Equal(BackupTrigger.Manual, backup.Trigger);
        Assert.Equal(_admin, backup.RequestedBy);
        Assert.Equal(_clock.UtcNow, backup.CreatedAtUtc);
        Assert.Equal(_clock.UtcNow + Retention, backup.ExpiresAtUtc);
        Assert.True(backup.IsActive);
        Assert.Null(backup.StorageKey);
        var requested = DomainAssert.Raised<BackupRequested>(backup);
        Assert.Equal((backup.Id, BackupTrigger.Manual, (UserId?)_admin), (requested.BackupId, requested.Trigger, requested.RequestedBy));
    }

    [Fact]
    public void A_scheduled_request_has_no_requester()
    {
        var backup = Backup.Schedule("es_restaurant", Retention, _clock);

        Assert.Equal(BackupTrigger.Scheduled, backup.Trigger);
        Assert.Null(backup.RequestedBy);
        Assert.Null(DomainAssert.Raised<BackupRequested>(backup).RequestedBy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Es_Restaurant")]
    [InlineData("es-restaurant")]
    [InlineData("es restaurant; drop")]
    [InlineData("1base")]
    public void The_database_name_must_be_a_plain_identifier(string name)
    {
        DomainAssert.Throws(BackupErrors.DatabaseNameInvalid, () => Backup.RequestManual(name, _admin, Retention, _clock));
    }

    [Fact]
    public void A_manual_request_needs_its_requester()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => Backup.RequestManual("es_restaurant", default, Retention, _clock));
    }

    [Fact]
    public void A_running_backup_without_heartbeat_times_out()
    {
        var backup = AgentWrites.Running(Manual(), _clock.UtcNow.AddHours(-2));
        _clock.Advance(TimeSpan.FromMinutes(1));

        Assert.True(backup.FailIfStale(_clock.UtcNow.AddMinutes(-60), _clock));

        Assert.Equal(BackupStatus.Failed, backup.Status);
        Assert.Equal(FailureCodes.Timeout, backup.FailureCode);
        Assert.Equal(_clock.UtcNow, backup.CompletedAtUtc);
        Assert.False(backup.IsActive);
    }

    [Fact]
    public void A_recent_heartbeat_keeps_the_backup_running()
    {
        var backup = AgentWrites.Running(Manual(), _clock.UtcNow.AddHours(-2), heartbeatAtUtc: _clock.UtcNow.AddSeconds(-30));

        Assert.False(backup.FailIfStale(_clock.UtcNow.AddMinutes(-60), _clock));
        Assert.Equal(BackupStatus.Running, backup.Status);
    }

    [Fact]
    public void An_uploading_backup_times_out_too_but_a_pending_one_waits_for_the_agent()
    {
        var uploading = AgentWrites.Set(AgentWrites.Running(Manual(), _clock.UtcNow.AddHours(-3)), nameof(Backup.Status), BackupStatus.Uploading);
        var pending = Manual();
        _clock.Advance(TimeSpan.FromHours(5));

        Assert.True(uploading.FailIfStale(_clock.UtcNow.AddMinutes(-60), _clock));
        Assert.False(pending.FailIfStale(_clock.UtcNow.AddMinutes(-60), _clock));
        Assert.Equal(BackupStatus.Pending, pending.Status);
    }

    [Fact]
    public void Only_a_completed_backup_can_be_deleted()
    {
        var backup = Manual();
        DomainAssert.Throws(BackupErrors.NotCompleted, () => backup.RequestDeletion(_admin, _clock));

        AgentWrites.Completed(backup);
        backup.ClearDomainEvents();
        backup.RequestDeletion(_admin, _clock);

        Assert.Equal(BackupStatus.Deleting, backup.Status);
        Assert.Equal(_admin, DomainAssert.Raised<BackupDeletionRequested>(backup).RequestedBy);
        DomainAssert.Throws(BackupErrors.NotCompleted, () => backup.RequestDeletion(_admin, _clock));
    }

    [Fact]
    public void A_download_link_is_recorded_for_a_completed_backup_only_and_changes_nothing()
    {
        var backup = Manual();
        var expires = _clock.UtcNow.AddMinutes(5);
        DomainAssert.Throws(BackupErrors.NotCompleted, () => backup.RecordDownloadLink(_admin, expires, _clock));

        AgentWrites.Completed(backup);
        backup.ClearDomainEvents();
        backup.RecordDownloadLink(_admin, expires, _clock);

        Assert.Equal(BackupStatus.Completed, backup.Status);
        var created = DomainAssert.Raised<BackupDownloadLinkCreated>(backup);
        Assert.Equal((_admin, expires), (created.RequestedBy, created.ExpiresAtUtc));
    }

    [Fact]
    public void Failure_codes_are_the_closed_lists_of_the_contract()
    {
        Assert.Equal(new[] { "dump_failed", "verify_failed", "encrypt_failed", "upload_failed", "timeout", "agent_restarted" }, FailureCodes.Backup);
        Assert.Equal(
            new[] { "disabled", "download_failed", "checksum_mismatch", "invalid_key", "key_missing", "decrypt_failed", "restore_failed", "swap_failed", "timeout" },
            FailureCodes.Restore);
    }

    [Fact]
    public void Statuses_are_the_names_of_the_contract()
    {
        Assert.Equal(new[] { "Pending", "Running", "Uploading", "Completed", "Failed", "Deleting", "Deleted" }, Enum.GetNames<BackupStatus>());
        Assert.Equal(new[] { "Pending", "Running", "Completed", "Failed", "Cancelled" }, Enum.GetNames<RestoreStatus>());
        Assert.Equal(new[] { "Manual", "Scheduled" }, Enum.GetNames<BackupTrigger>());
    }
}
