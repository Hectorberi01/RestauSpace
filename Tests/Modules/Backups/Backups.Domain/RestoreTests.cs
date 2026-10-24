namespace Backups.Domain;

public sealed class RestoreTests
{
    private const string Database = "es_restaurant";
    private static readonly TimeSpan Delay = TimeSpan.FromMinutes(5);
    private readonly FakeClock _clock = new();
    private readonly UserId _admin = UserId.New();

    private Backup CompletedBackup() =>
        AgentWrites.Completed(Backup.RequestManual(Database, _admin, TimeSpan.FromDays(30), _clock));

    [Fact]
    public void A_restore_is_pending_until_the_cancellation_delay_has_passed()
    {
        var backup = CompletedBackup();

        var restore = Restore.Request(backup, Database, Database, _admin, Delay, _clock);

        Assert.Equal(RestoreStatus.Pending, restore.Status);
        Assert.Equal((backup.Id, Database, _admin), (restore.BackupId, restore.DatabaseName, restore.RequestedBy));
        Assert.Equal(_clock.UtcNow + Delay, restore.NotBeforeUtc);
        Assert.True(restore.IsActive);
        Assert.Equal(restore.NotBeforeUtc, DomainAssert.Raised<RestoreRequested>(restore).NotBeforeUtc);
    }

    [Fact]
    public void Only_a_completed_backup_can_be_restored()
    {
        var pending = Backup.RequestManual(Database, _admin, TimeSpan.FromDays(30), _clock);

        DomainAssert.Throws(BackupErrors.NotCompleted, () => Restore.Request(pending, Database, Database, _admin, Delay, _clock));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("ES_RESTAURANT")]
    [InlineData("es_restaurant_before")]
    public void The_database_name_must_be_typed_exactly(string confirmation)
    {
        DomainAssert.Throws(
            BackupErrors.ConfirmationMismatch, () => Restore.Request(CompletedBackup(), Database, confirmation, _admin, Delay, _clock));
    }

    [Fact]
    public void A_pending_restore_can_be_cancelled_once()
    {
        var restore = Restore.Request(CompletedBackup(), Database, Database, _admin, Delay, _clock);
        _clock.Advance(TimeSpan.FromMinutes(1));

        restore.Cancel(_admin, _clock);

        Assert.Equal(RestoreStatus.Cancelled, restore.Status);
        Assert.Equal(_clock.UtcNow, restore.CompletedAtUtc);
        Assert.False(restore.IsActive);
        Assert.Equal(_admin, DomainAssert.Raised<RestoreCancelled>(restore).CancelledBy);
        DomainAssert.Throws(BackupErrors.NotCancellable, () => restore.Cancel(_admin, _clock));
    }

    [Fact]
    public void A_restore_taken_by_the_agent_cannot_be_cancelled()
    {
        var restore = AgentWrites.Running(Restore.Request(CompletedBackup(), Database, Database, _admin, Delay, _clock), _clock.UtcNow);

        DomainAssert.Throws(BackupErrors.NotCancellable, () => restore.Cancel(_admin, _clock));
    }

    [Fact]
    public void A_running_restore_without_heartbeat_times_out()
    {
        var restore = AgentWrites.Running(Restore.Request(CompletedBackup(), Database, Database, _admin, Delay, _clock), _clock.UtcNow);
        var alive = AgentWrites.Running(
            Restore.Request(CompletedBackup(), Database, Database, _admin, Delay, _clock), _clock.UtcNow, heartbeatAtUtc: _clock.UtcNow.AddMinutes(90));
        _clock.Advance(TimeSpan.FromMinutes(91));

        Assert.True(restore.FailIfStale(_clock.UtcNow.AddMinutes(-60), _clock));
        Assert.False(alive.FailIfStale(_clock.UtcNow.AddMinutes(-60), _clock));
        Assert.Equal((RestoreStatus.Failed, FailureCodes.Timeout), (restore.Status, restore.FailureCode));
        Assert.Equal(RestoreStatus.Running, alive.Status);
    }
}
