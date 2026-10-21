using Files.Domain;

namespace Files.Domain;

public class MediaFileTests
{
    private static readonly ContentType Png = ContentType.Create("image/png");
    private static readonly FileUploadPolicy Policy = FileUploadPolicy.Create(1000, [Png]);

    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();
    private readonly FileOwner _owner = FileOwner.Create("catalog.product", Guid.NewGuid());

    private MediaFile Pending(long size = 500, ContentType? contentType = null, FileUploadPolicy? policy = null) =>
        MediaFile.CreatePending(
            _tenantId,
            _owner,
            FileName.Create("dish.png"),
            contentType ?? Png,
            size,
            StorageKey.Create("t/abc/dish"),
            policy ?? Policy,
            _clock);

    private MediaFile Available()
    {
        var file = Pending();
        file.MarkAvailable(500, Png, _clock);
        file.ClearDomainEvents();
        return file;
    }

    private MediaFile Quarantined()
    {
        var file = Available();
        file.Quarantine("malware detected", _clock);
        file.ClearDomainEvents();
        return file;
    }

    private MediaFile Deleted()
    {
        var file = Available();
        file.Delete(_clock);
        file.ClearDomainEvents();
        return file;
    }

    [Fact]
    public void CreatePending_records_metadata_without_raising_an_event()
    {
        var file = Pending();

        Assert.NotEqual(default, file.Id);
        Assert.Equal(_tenantId, file.TenantId);
        Assert.Equal(_owner, file.Owner);
        Assert.Equal("dish.png", file.FileName.Value);
        Assert.Equal(Png, file.ContentType);
        Assert.Equal(500, file.SizeInBytes);
        Assert.Equal(1000, file.MaxSizeInBytes);
        Assert.Equal("t/abc/dish", file.StorageKey.Value);
        Assert.Equal(MediaFileStatus.Pending, file.Status);
        Assert.Equal(_clock.UtcNow, file.CreatedAtUtc);
        Assert.False(file.CanBeServed);
        DomainAssert.NoEvents(file);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreatePending_rejects_a_non_positive_size(long size) =>
        DomainAssert.Throws(FileErrors.SizeNotPositive, () => Pending(size));

    [Fact]
    public void CreatePending_accepts_a_size_equal_to_the_limit() =>
        Assert.Equal(1000, Pending(size: 1000).SizeInBytes);

    [Fact]
    public void CreatePending_rejects_a_size_over_the_limit() =>
        DomainAssert.Throws(FileErrors.SizeExceedsLimit, () => Pending(size: 1001));

    [Fact]
    public void CreatePending_rejects_a_content_type_outside_the_policy() =>
        DomainAssert.Throws(
            FileErrors.ContentTypeNotAllowed,
            () => Pending(contentType: ContentType.Create("application/x-msdownload")));

    [Fact]
    public void CreatePending_applies_the_policy_it_is_given()
    {
        var pdf = ContentType.Create("application/pdf");
        var widerPolicy = FileUploadPolicy.Create(5000, [Png, pdf]);

        var file = Pending(size: 4000, contentType: pdf, policy: widerPolicy);

        Assert.Equal(pdf, file.ContentType);
        DomainAssert.Throws(FileErrors.SizeExceedsLimit, () => Pending(size: 4000, policy: Policy));
    }

    [Fact]
    public void CreatePending_with_the_default_policy_rejects_an_executable() =>
        DomainAssert.Throws(
            FileErrors.ContentTypeNotAllowed,
            () => Pending(contentType: ContentType.Create("application/x-msdownload"), policy: FileUploadPolicy.Default));

    [Fact]
    public void CreatePending_rejects_a_default_tenant() =>
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => MediaFile.CreatePending(
                default, _owner, FileName.Create("a.png"), Png, 10, StorageKey.Create("k"), Policy, _clock));

    [Fact]
    public void MarkAvailable_makes_a_pending_file_servable_and_raises_FileUploaded()
    {
        var file = Pending();
        _clock.Advance(TimeSpan.FromSeconds(5));

        file.MarkAvailable(500, Png, _clock);

        Assert.Equal(MediaFileStatus.Available, file.Status);
        Assert.Equal(_clock.UtcNow, file.AvailableAtUtc);
        Assert.True(file.CanBeServed);
        var uploaded = DomainAssert.Raised<FileUploaded>(file);
        Assert.Equal(_tenantId, uploaded.TenantId);
        Assert.Equal(file.Id, uploaded.FileId);
        Assert.Equal("catalog.product", uploaded.OwnerType);
        Assert.Equal(_owner.OwnerId, uploaded.OwnerId);
        Assert.Equal("image/png", uploaded.ContentType);
        Assert.Equal(500, uploaded.SizeInBytes);
        Assert.Equal(_clock.UtcNow, uploaded.OccurredAtUtc);
    }

    [Fact]
    public void MarkAvailable_rejects_an_already_available_file()
    {
        var file = Available();

        DomainAssert.Throws(FileErrors.NotPending, () => file.MarkAvailable(500, Png, _clock));
        DomainAssert.NoEvents(file);
    }

    [Fact]
    public void MarkAvailable_rejects_a_quarantined_file()
    {
        var file = Quarantined();

        DomainAssert.Throws(FileErrors.NotPending, () => file.MarkAvailable(500, Png, _clock));
        Assert.Equal(MediaFileStatus.Quarantined, file.Status);
        DomainAssert.NoEvents(file);
    }

    [Fact]
    public void Quarantine_isolates_a_pending_file()
    {
        var file = Pending();

        file.Quarantine("  scan failed ", _clock);

        Assert.Equal(MediaFileStatus.Quarantined, file.Status);
        Assert.Equal("scan failed", file.QuarantineReason);
        Assert.Equal(_clock.UtcNow, file.QuarantinedAtUtc);
        var quarantined = DomainAssert.Raised<FileQuarantined>(file);
        Assert.Equal(file.Id, quarantined.FileId);
        Assert.Equal(_tenantId, quarantined.TenantId);
        Assert.Equal("scan failed", quarantined.Reason);
        Assert.Equal("catalog.product", quarantined.OwnerType);
        Assert.Equal(_owner.OwnerId, quarantined.OwnerId);
    }

    [Fact]
    public void Quarantine_isolates_an_available_file_which_can_no_longer_be_served()
    {
        var file = Available();

        file.Quarantine("malware detected", _clock);

        Assert.Equal(MediaFileStatus.Quarantined, file.Status);
        Assert.False(file.CanBeServed);
        DomainAssert.Raised<FileQuarantined>(file);
    }

    [Fact]
    public void Quarantine_rejects_an_already_quarantined_file()
    {
        var file = Quarantined();

        DomainAssert.Throws(FileErrors.AlreadyQuarantined, () => file.Quarantine("again", _clock));
        Assert.Equal("malware detected", file.QuarantineReason);
        DomainAssert.NoEvents(file);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Quarantine_requires_a_reason(string? reason)
    {
        var file = Available();

        DomainAssert.Throws(FileErrors.QuarantineReasonInvalid, () => file.Quarantine(reason, _clock));
        Assert.Equal(MediaFileStatus.Available, file.Status);
        DomainAssert.NoEvents(file);
    }

    [Fact]
    public void Quarantine_rejects_a_reason_over_the_limit()
    {
        var file = Available();

        DomainAssert.Throws(
            FileErrors.QuarantineReasonInvalid,
            () => file.Quarantine(new string('r', MediaFile.MaxQuarantineReasonLength + 1), _clock));
    }

    [Fact]
    public void Delete_is_allowed_from_pending()
    {
        var file = Pending();

        file.Delete(_clock);

        Assert.Equal(MediaFileStatus.Deleted, file.Status);
        DomainAssert.Raised<FileDeleted>(file);
    }

    [Fact]
    public void Delete_is_allowed_from_available_and_raises_FileDeleted_with_the_storage_key()
    {
        var file = Available();
        _clock.Advance(TimeSpan.FromDays(1));

        file.Delete(_clock);

        Assert.Equal(MediaFileStatus.Deleted, file.Status);
        Assert.Equal(_clock.UtcNow, file.DeletedAtUtc);
        Assert.False(file.CanBeServed);
        var deleted = DomainAssert.Raised<FileDeleted>(file);
        Assert.Equal(file.Id, deleted.FileId);
        Assert.Equal(_tenantId, deleted.TenantId);
        Assert.Equal("t/abc/dish", deleted.StorageKey);
        Assert.Equal("catalog.product", deleted.OwnerType);
        Assert.Equal(_owner.OwnerId, deleted.OwnerId);
    }

    [Fact]
    public void Delete_is_allowed_from_quarantined()
    {
        var file = Quarantined();

        file.Delete(_clock);

        Assert.Equal(MediaFileStatus.Deleted, file.Status);
        DomainAssert.Raised<FileDeleted>(file);
    }

    [Fact]
    public void Deleted_is_terminal()
    {
        var file = Deleted();

        DomainAssert.Throws(FileErrors.Deleted, () => file.MarkAvailable(500, Png, _clock));
        DomainAssert.Throws(FileErrors.Deleted, () => file.Quarantine("late", _clock));
        DomainAssert.Throws(FileErrors.Deleted, () => file.Delete(_clock));
        Assert.Equal(MediaFileStatus.Deleted, file.Status);
        DomainAssert.NoEvents(file);
    }

    [Fact]
    public void Tenant_is_guarded_against_cross_tenant_references()
    {
        var file = Pending();

        file.EnsureSameTenant(_tenantId);
        DomainAssert.Throws(CommonErrors.TenantMismatch, () => file.EnsureSameTenant(TenantId.New()));
    }

    [Fact]
    public void Aggregate_exposes_no_public_setter() =>
        Assert.All(
            typeof(MediaFile).GetProperties(),
            property => Assert.True(property.SetMethod is null || !property.SetMethod.IsPublic, property.Name));

    // ---- MarkAvailable confronte les valeurs déclarées aux valeurs observées

    [Theory]
    [InlineData(499)]
    [InlineData(501)]
    public void MarkAvailable_rejects_an_actual_size_different_from_the_declared_one(long actualSize)
    {
        var file = Pending(size: 500);

        DomainAssert.Throws(FileErrors.ActualSizeMismatch, () => file.MarkAvailable(actualSize, Png, _clock));
        Assert.Equal(MediaFileStatus.Pending, file.Status);
        Assert.False(file.CanBeServed);
        DomainAssert.NoEvents(file);
    }

    [Fact]
    public void MarkAvailable_rejects_an_actual_size_over_the_limit_pinned_at_creation()
    {
        var file = Pending(size: 500);

        DomainAssert.Throws(FileErrors.SizeExceedsLimit, () => file.MarkAvailable(1001, Png, _clock));
        Assert.Equal(MediaFileStatus.Pending, file.Status);
        DomainAssert.NoEvents(file);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MarkAvailable_rejects_a_non_positive_actual_size(long actualSize)
    {
        var file = Pending();

        DomainAssert.Throws(FileErrors.SizeNotPositive, () => file.MarkAvailable(actualSize, Png, _clock));
    }

    [Fact]
    public void MarkAvailable_rejects_a_detected_content_type_different_from_the_declared_one()
    {
        var file = Pending();

        DomainAssert.Throws(
            FileErrors.ContentTypeMismatch,
            () => file.MarkAvailable(500, ContentType.Create("application/x-msdownload"), _clock));
        Assert.Equal(MediaFileStatus.Pending, file.Status);
        DomainAssert.NoEvents(file);
    }

    [Fact]
    public void A_file_whose_observed_facts_were_rejected_can_be_quarantined()
    {
        var file = Pending();
        DomainAssert.Throws(
            FileErrors.ContentTypeMismatch,
            () => file.MarkAvailable(500, ContentType.Create("application/x-msdownload"), _clock));

        file.Quarantine("content type mismatch", _clock);

        Assert.Equal(MediaFileStatus.Quarantined, file.Status);
    }

    [Fact]
    public void The_size_limit_is_pinned_from_the_policy_given_at_creation()
    {
        var file = Pending(size: 4000, policy: FileUploadPolicy.Create(5000, [Png]));

        Assert.Equal(5000, file.MaxSizeInBytes);
        DomainAssert.Throws(FileErrors.SizeExceedsLimit, () => file.MarkAvailable(5001, Png, _clock));
        file.MarkAvailable(4000, Png, _clock);
        Assert.True(file.CanBeServed);
    }

    [Fact]
    public void State_timestamps_equal_the_event_instant()
    {
        var ticking = new TickingClock(_clock.UtcNow);
        var file = Pending();

        file.MarkAvailable(500, Png, ticking);
        Assert.Equal(file.AvailableAtUtc, DomainAssert.Raised<FileUploaded>(file).OccurredAtUtc);

        file.Quarantine("malware detected", ticking);
        Assert.Equal(file.QuarantinedAtUtc, DomainAssert.Raised<FileQuarantined>(file).OccurredAtUtc);

        file.Delete(ticking);
        Assert.Equal(file.DeletedAtUtc, DomainAssert.Raised<FileDeleted>(file).OccurredAtUtc);
    }

    /// <summary>Avance à chaque lecture : prouve qu'une opération ne lit l'horloge qu'une fois.</summary>
    private sealed class TickingClock(DateTimeOffset start) : IClock
    {
        private DateTimeOffset _now = start;

        public DateTimeOffset UtcNow => _now = _now.AddSeconds(1);
    }
}
