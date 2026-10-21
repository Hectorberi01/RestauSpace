using Reporting.Domain;

namespace Reporting.Domain;

public class ExportJobTests
{
    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();
    private readonly UserId _requestedBy = UserId.New();

    private ExportRequester Requester => ExportRequester.ForUser(_requestedBy);

    private ExportedFileReference File(FileId? fileId = null) => new(_tenantId, fileId ?? FileId.New());

    // Une référence ne peut provenir que d'une vraie définition.
    private ReportDefinition DefinitionIn(TenantId tenantId) =>
        ReportDefinition.Create(
            tenantId, "Weekly sales", ["sales.net_revenue"], null, ReportCatalog.Create(["sales.net_revenue"], []), _clock);

    private ExportJob Queued()
    {
        var job = ExportJob.Queue(_tenantId, ExportType.Create("orders.csv"), Requester, null, _clock);
        job.ClearDomainEvents();
        return job;
    }

    private ExportJob Running()
    {
        var job = Queued();
        job.Start(_clock);
        return job;
    }

    private ExportJob Completed()
    {
        var job = Running();
        job.Complete(File(), _clock);
        job.ClearDomainEvents();
        return job;
    }

    private ExportJob Failed()
    {
        var job = Running();
        job.Fail("timeout", _clock);
        job.ClearDomainEvents();
        return job;
    }

    private ExportJob Expired()
    {
        var job = Completed();
        job.Expire(_clock);
        job.ClearDomainEvents();
        return job;
    }

    [Fact]
    public void Queue_creates_a_queued_job_and_raises_ExportRequested()
    {
        var job = ExportJob.Queue(_tenantId, ExportType.Create("orders.csv"), Requester, null, _clock);

        Assert.NotEqual(default, job.Id);
        Assert.Equal(_tenantId, job.TenantId);
        Assert.Equal("orders.csv", job.Type.Value);
        Assert.Equal(ExportJobStatus.Queued, job.Status);
        Assert.Null(job.FileId);
        Assert.Equal(_clock.UtcNow, job.QueuedAtUtc);
        Assert.False(job.IsDownloadable);
        var requested = DomainAssert.Raised<ExportRequested>(job);
        Assert.Equal(_tenantId, requested.TenantId);
        Assert.Equal(job.Id, requested.ExportJobId);
        Assert.Equal("orders.csv", requested.ExportType);
        Assert.Equal(Requester, job.RequestedBy);
        Assert.Equal(Requester, requested.Actor);
        Assert.Null(job.ReportDefinitionId);
        Assert.Null(requested.ReportDefinitionId);
    }

    [Fact]
    public void Queue_records_the_exported_report_definition()
    {
        var definition = DefinitionIn(_tenantId);

        var job = ExportJob.Queue(_tenantId, ExportType.Create("report.pdf"), Requester, definition.ToReference(), _clock);

        Assert.Equal(definition.Id, job.ReportDefinitionId);
        Assert.Equal(definition.Id, DomainAssert.Raised<ExportRequested>(job).ReportDefinitionId);
    }

    [Fact]
    public void Queue_rejects_a_report_definition_of_another_tenant()
    {
        var foreignDefinition = DefinitionIn(TenantId.New());

        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => ExportJob.Queue(_tenantId, ExportType.Create("report.pdf"), Requester, foreignDefinition.ToReference(), _clock));
    }

    [Fact]
    public void ExportedFileReference_rejects_an_empty_tenant() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => new ExportedFileReference(default, FileId.New()));

    [Fact]
    public void Each_operation_reads_the_clock_once_so_state_and_event_carry_the_same_instant()
    {
        var clock = new TickingClock(_clock.UtcNow);

        var job = ExportJob.Queue(_tenantId, ExportType.Create("orders.csv"), Requester, null, clock);
        Assert.Equal(job.QueuedAtUtc, DomainAssert.Raised<ExportRequested>(job).OccurredAtUtc);

        job.Start(clock);
        job.Complete(File(), clock);
        Assert.Equal(job.CompletedAtUtc, DomainAssert.Raised<ExportCompleted>(job).OccurredAtUtc);

        job.Expire(clock);
        Assert.Equal(job.ExpiredAtUtc, DomainAssert.Raised<ExportExpired>(job).OccurredAtUtc);

        var failing = ExportJob.Queue(_tenantId, ExportType.Create("orders.csv"), Requester, null, clock);
        failing.Start(clock);
        failing.Fail("timeout", clock);
        Assert.Equal(failing.FailedAtUtc, DomainAssert.Raised<ExportFailed>(failing).OccurredAtUtc);
    }

    [Fact]
    public void Queue_rejects_a_default_tenant() =>
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => ExportJob.Queue(default, ExportType.Create("orders.csv"), Requester, null, _clock));

    [Fact]
    public void Queue_rejects_a_default_requester()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => ExportRequester.ForUser(default));
        DomainAssert.Throws(CommonErrors.EmptyId, () => ExportRequester.ForApiCredential(Guid.Empty));
    }

    [Fact]
    public void An_api_key_requests_an_export_and_no_user_is_named()
    {
        var credential = Guid.NewGuid();
        var job = ExportJob.Queue(_tenantId, ExportType.Create("orders.csv"), ExportRequester.ForApiCredential(credential), null, _clock);
        job.Start(_clock);
        job.Complete(File(), _clock);

        Assert.Equal((ExportRequesterKind.ApiCredential, credential), (job.RequestedBy.Kind, job.RequestedBy.Id));
        Assert.Null(job.RequestedBy.User);
        Assert.Null(DomainAssert.Raised<ExportCompleted>(job).RequestedBy);
    }

    [Fact]
    public void Start_moves_a_queued_job_to_running()
    {
        var job = Queued();
        _clock.Advance(TimeSpan.FromSeconds(3));

        job.Start(_clock);

        Assert.Equal(ExportJobStatus.Running, job.Status);
        Assert.Equal(_clock.UtcNow, job.StartedAtUtc);
        DomainAssert.NoEvents(job);
    }

    [Fact]
    public void Start_rejects_a_job_that_is_already_running_or_completed()
    {
        DomainAssert.Throws(ExportErrors.NotQueued, () => Running().Start(_clock));
        DomainAssert.Throws(ExportErrors.NotQueued, () => Completed().Start(_clock));
    }

    [Fact]
    public void Complete_attaches_the_file_and_raises_ExportCompleted()
    {
        var job = Running();
        var fileId = FileId.New();
        _clock.Advance(TimeSpan.FromMinutes(2));

        job.Complete(File(fileId), _clock);

        Assert.Equal(ExportJobStatus.Completed, job.Status);
        Assert.Equal(fileId, job.FileId);
        Assert.Equal(_clock.UtcNow, job.CompletedAtUtc);
        Assert.True(job.IsDownloadable);
        var completed = DomainAssert.Raised<ExportCompleted>(job);
        Assert.Equal(_tenantId, completed.TenantId);
        Assert.Equal(job.Id, completed.ExportJobId);
        Assert.Equal(fileId, completed.FileId);
        Assert.Equal(_requestedBy, completed.RequestedBy);
    }

    [Fact]
    public void Complete_rejects_a_file_of_another_tenant()
    {
        var job = Running();
        var foreignFile = new ExportedFileReference(TenantId.New(), FileId.New());

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => job.Complete(foreignFile, _clock));

        Assert.Equal(ExportJobStatus.Running, job.Status);
        Assert.Null(job.FileId);
        Assert.Null(job.CompletedAtUtc);
        Assert.False(job.IsDownloadable);
        DomainAssert.NotRaised<ExportCompleted>(job);
    }

    [Fact]
    public void Complete_without_a_file_is_rejected()
    {
        var job = Running();

        DomainAssert.Throws(ExportErrors.FileRequired, () => job.Complete(new ExportedFileReference(_tenantId, default), _clock));
        DomainAssert.Throws(ExportErrors.FileRequired, () => job.Complete(new ExportedFileReference(_tenantId, new FileId()), _clock));

        Assert.Equal(ExportJobStatus.Running, job.Status);
        Assert.Null(job.FileId);
        Assert.Null(job.CompletedAtUtc);
        DomainAssert.NotRaised<ExportCompleted>(job);
    }

    [Fact]
    public void Complete_is_rejected_before_the_job_is_started()
    {
        var job = Queued();

        DomainAssert.Throws(ExportErrors.NotRunning, () => job.Complete(File(), _clock));
        Assert.Equal(ExportJobStatus.Queued, job.Status);
        Assert.Null(job.FileId);
    }

    [Fact]
    public void Complete_cannot_replace_the_file_of_a_completed_job()
    {
        var job = Completed();
        var originalFile = job.FileId;

        DomainAssert.Throws(ExportErrors.NotRunning, () => job.Complete(File(), _clock));
        Assert.Equal(originalFile, job.FileId);
        DomainAssert.NoEvents(job);
    }

    [Fact]
    public void A_completed_job_always_has_a_file_whatever_the_path()
    {
        // Completed n'est atteignable que via Complete, qui refuse un identifiant de fichier vide.
        Assert.NotNull(Completed().FileId);
        Assert.NotEqual(default, Completed().FileId!.Value);
        Assert.Null(Queued().FileId);
        Assert.Null(Running().FileId);
        Assert.Null(Failed().FileId);
        Assert.True(typeof(ExportJob).GetProperty(nameof(ExportJob.FileId))!.SetMethod!.IsPrivate);
        Assert.True(typeof(ExportJob).GetProperty(nameof(ExportJob.Status))!.SetMethod!.IsPrivate);
    }

    [Fact]
    public void Fail_records_the_reason_and_raises_ExportFailed()
    {
        var job = Running();

        job.Fail("  storage unavailable ", _clock);

        Assert.Equal(ExportJobStatus.Failed, job.Status);
        Assert.Equal("storage unavailable", job.FailureReason);
        Assert.Equal(_clock.UtcNow, job.FailedAtUtc);
        Assert.Null(job.FileId);
        var failed = DomainAssert.Raised<ExportFailed>(job);
        Assert.Equal(job.Id, failed.ExportJobId);
        Assert.Equal("storage unavailable", failed.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Fail_requires_a_reason(string? reason)
    {
        var job = Running();

        DomainAssert.Throws(ExportErrors.FailureReasonInvalid, () => job.Fail(reason, _clock));
        Assert.Equal(ExportJobStatus.Running, job.Status);
    }

    [Fact]
    public void Fail_rejects_a_reason_over_the_limit() =>
        DomainAssert.Throws(
            ExportErrors.FailureReasonInvalid,
            () => Running().Fail(new string('r', ExportJob.MaxFailureReasonLength + 1), _clock));

    [Fact]
    public void Fail_is_rejected_when_the_job_is_not_running()
    {
        DomainAssert.Throws(ExportErrors.NotRunning, () => Queued().Fail("x", _clock));
        DomainAssert.Throws(ExportErrors.NotRunning, () => Completed().Fail("x", _clock));
    }

    [Fact]
    public void Expire_makes_a_completed_job_no_longer_downloadable_and_raises_ExportExpired()
    {
        var job = Completed();
        var fileId = job.FileId!.Value;
        _clock.Advance(TimeSpan.FromDays(7));

        job.Expire(_clock);

        Assert.Equal(ExportJobStatus.Expired, job.Status);
        Assert.Equal(_clock.UtcNow, job.ExpiredAtUtc);
        Assert.False(job.IsDownloadable);
        Assert.Equal(fileId, job.FileId);
        var expired = DomainAssert.Raised<ExportExpired>(job);
        Assert.Equal(job.Id, expired.ExportJobId);
        Assert.Equal(fileId, expired.FileId);
    }

    [Fact]
    public void Expire_is_rejected_when_the_job_is_not_completed()
    {
        DomainAssert.Throws(ExportErrors.NotCompleted, () => Queued().Expire(_clock));
        DomainAssert.Throws(ExportErrors.NotCompleted, () => Running().Expire(_clock));
    }

    [Fact]
    public void Failed_is_terminal()
    {
        var job = Failed();

        DomainAssert.Throws(ExportErrors.AlreadyFailed, () => job.Start(_clock));
        DomainAssert.Throws(ExportErrors.AlreadyFailed, () => job.Complete(File(), _clock));
        DomainAssert.Throws(ExportErrors.AlreadyFailed, () => job.Fail("again", _clock));
        DomainAssert.Throws(ExportErrors.AlreadyFailed, () => job.Expire(_clock));
        Assert.Equal(ExportJobStatus.Failed, job.Status);
        Assert.Equal("timeout", job.FailureReason);
        DomainAssert.NoEvents(job);
    }

    [Fact]
    public void Expired_is_terminal()
    {
        var job = Expired();

        DomainAssert.Throws(ExportErrors.AlreadyExpired, () => job.Start(_clock));
        DomainAssert.Throws(ExportErrors.AlreadyExpired, () => job.Complete(File(), _clock));
        DomainAssert.Throws(ExportErrors.AlreadyExpired, () => job.Fail("late", _clock));
        DomainAssert.Throws(ExportErrors.AlreadyExpired, () => job.Expire(_clock));
        Assert.Equal(ExportJobStatus.Expired, job.Status);
        DomainAssert.NoEvents(job);
    }
}
