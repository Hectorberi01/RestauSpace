using System.Text;
using Files.Application.Commands;
using Files.Application.Queries;
using Files.Application.Storage;
using Files.Domain;

namespace Files.Application;

public sealed class FilesUseCaseTests
{
    private readonly FilesFixture _fixture = new();

    private Task<UploadTicket> RequestPngAsync(Guid product, long size = 64, string name = "burger.png") =>
        _fixture.Request.HandleAsync(new RequestUploadCommand("catalog.product", product, name, "image/png", size));

    // --- Demande d'envoi

    [Fact]
    public async Task An_upload_is_declared_as_pending_with_a_signed_url_and_a_storage_key_unrelated_to_its_name()
    {
        var product = _fixture.Product();

        var ticket = await RequestPngAsync(product, name: "..evil name.png");

        var file = _fixture.FileById(ticket.FileId);
        Assert.Equal((MediaFileStatus.Pending, "PUT"), (file.Status, ticket.Method));
        Assert.Equal("image/png", ticket.Headers["Content-Type"]);
        Assert.Equal(_fixture.Clock.UtcNow + _fixture.Settings.UploadUrlLifetime, ticket.ExpiresAtUtc);
        Assert.StartsWith($"tenants/{_fixture.TenantA.Value:N}/catalog.product/", file.StorageKey.Value, StringComparison.Ordinal);
        Assert.DoesNotContain("evil", file.StorageKey.Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_upload_is_refused_for_an_unknown_owner_an_internal_owner_type_or_outside_the_policy()
    {
        var product = _fixture.Product();

        var unknown = await Assert.ThrowsAsync<NotFoundException>(() => RequestPngAsync(Guid.NewGuid()));
        var internalOwner = await Assert.ThrowsAsync<DomainException>(() => _fixture.Request.HandleAsync(
            new RequestUploadCommand("reporting.export_job", Guid.NewGuid(), "x.csv", "text/csv", 10)));
        var gif = await Assert.ThrowsAsync<DomainException>(() => _fixture.Request.HandleAsync(
            new RequestUploadCommand("catalog.product", product, "x.gif", "image/gif", 10)));
        var tooBig = await Assert.ThrowsAsync<DomainException>(() => RequestPngAsync(product, 5L * 1024 * 1024 + 1));
        var otherLogo = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Request.HandleAsync(
            new RequestUploadCommand("tenants.tenant", _fixture.TenantB.Value, "logo.png", "image/png", 10)));

        Assert.Equal(FilesApplicationErrors.OwnerNotFound, unknown.Error);
        Assert.Equal(FilesApplicationErrors.OwnerTypeNotUploadable, internalOwner.Error);
        Assert.Equal(FileErrors.ContentTypeNotAllowed, gif.Error);
        Assert.Equal(FileErrors.SizeExceedsLimit, tooBig.Error);
        Assert.Equal(FilesApplicationErrors.OwnerNotFound, otherLogo.Error);
        Assert.Empty(_fixture.Files);
    }

    [Fact]
    public async Task A_suspended_restaurant_cannot_upload()
    {
        var product = _fixture.Product();
        _fixture.Writable[_fixture.TenantA] = false;

        var refused = await Assert.ThrowsAsync<DomainException>(() => RequestPngAsync(product));

        Assert.Equal(FilesApplicationErrors.TenantNotWritable, refused.Error);
    }

    // --- Confirmation

    [Fact]
    public async Task A_conforming_upload_becomes_available_once_and_can_be_downloaded()
    {
        var ticket = await RequestPngAsync(_fixture.Product());
        _fixture.UploadBytes(ticket.FileId, Samples.Png());

        var first = await _fixture.Complete.HandleAsync(new CompleteUploadCommand(ticket.FileId));
        var replay = await _fixture.Complete.HandleAsync(new CompleteUploadCommand(ticket.FileId));
        var link = await _fixture.Download.HandleAsync(new GetDownloadLinkQuery(ticket.FileId));

        Assert.Equal((UploadOutcome.Available, UploadOutcome.Available), (first.Outcome, replay.Outcome));
        Assert.Single(_fixture.FileById(ticket.FileId).DomainEvents.OfType<FileUploaded>());
        Assert.Equal(_fixture.Clock.UtcNow + _fixture.Settings.DownloadUrlLifetime, link.ExpiresAtUtc);
    }

    [Fact]
    public async Task Nothing_is_confirmed_before_the_bytes_reach_the_storage()
    {
        var ticket = await RequestPngAsync(_fixture.Product());

        var early = await Assert.ThrowsAsync<DomainException>(() => _fixture.Complete.HandleAsync(new CompleteUploadCommand(ticket.FileId)));

        Assert.Equal(FilesApplicationErrors.NotUploadedYet, early.Error);
        Assert.Equal(MediaFileStatus.Pending, _fixture.FileById(ticket.FileId).Status);
    }

    [Fact]
    public async Task A_disguised_or_resized_upload_is_quarantined_and_never_served()
    {
        var product = _fixture.Product();
        var disguised = await RequestPngAsync(product);
        var resized = await RequestPngAsync(product);
        _fixture.UploadBytes(disguised.FileId, Samples.Executable());
        _fixture.UploadBytes(resized.FileId, Samples.Png(128));

        var typeResult = await _fixture.Complete.HandleAsync(new CompleteUploadCommand(disguised.FileId));
        var sizeResult = await _fixture.Complete.HandleAsync(new CompleteUploadCommand(resized.FileId));
        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.Download.HandleAsync(new GetDownloadLinkQuery(disguised.FileId)));

        Assert.Equal((UploadOutcome.Quarantined, FileErrors.ContentTypeMismatch.Code), (typeResult.Outcome, typeResult.Reason));
        Assert.Equal((UploadOutcome.Quarantined, FileErrors.ActualSizeMismatch.Code), (sizeResult.Outcome, sizeResult.Reason));
        Assert.Equal(FilesApplicationErrors.NotServable, refused.Error);
        Assert.Empty(_fixture.FileById(disguised.FileId).DomainEvents.OfType<FileUploaded>());
    }

    [Fact]
    public async Task An_infected_upload_is_quarantined_before_ever_being_available()
    {
        var ticket = await RequestPngAsync(_fixture.Product());
        _fixture.UploadBytes(ticket.FileId, Samples.Png());
        _fixture.Threat = "Eicar-Test-Signature";

        var result = await _fixture.Complete.HandleAsync(new CompleteUploadCommand(ticket.FileId));

        Assert.Equal((UploadOutcome.Quarantined, "Eicar-Test-Signature"), (result.Outcome, result.Reason));
        Assert.Empty(_fixture.FileById(ticket.FileId).DomainEvents.OfType<FileUploaded>());
    }

    // --- Suppression et nettoyage

    [Fact]
    public async Task A_deleted_file_leaves_its_owner_list_and_announces_its_storage_key()
    {
        var product = _fixture.Product();
        var ticket = await RequestPngAsync(product);
        _fixture.UploadBytes(ticket.FileId, Samples.Png());
        _ = await _fixture.Complete.HandleAsync(new CompleteUploadCommand(ticket.FileId));

        await _fixture.Delete.HandleAsync(new DeleteFileCommand(ticket.FileId));
        var listed = await _fixture.List.HandleAsync(new ListOwnerFilesQuery("catalog.product", product));

        var deleted = Assert.Single(_fixture.FileById(ticket.FileId).DomainEvents.OfType<FileDeleted>());
        Assert.Equal(_fixture.FileById(ticket.FileId).StorageKey.Value, deleted.StorageKey);
        Assert.Empty(listed);
    }

    [Fact]
    public async Task An_upload_never_confirmed_is_deleted_after_the_delay()
    {
        var product = _fixture.Product();
        var old = await RequestPngAsync(product);
        _fixture.Clock.Advance(_fixture.Settings.AbandonedUploadAfter);
        var recent = await RequestPngAsync(product);

        var oldDeleted = await _fixture.DeleteAbandoned.HandleAsync(new DeleteAbandonedUploadCommand(old.FileId));
        var recentDeleted = await _fixture.DeleteAbandoned.HandleAsync(new DeleteAbandonedUploadCommand(recent.FileId));

        Assert.Equal((true, false), (oldDeleted, recentDeleted));
        Assert.Equal(MediaFileStatus.Deleted, _fixture.FileById(old.FileId).Status);
    }

    // --- Fichiers produits par le serveur (exports de Reporting)

    [Fact]
    public async Task A_generated_csv_is_stored_verified_read_back_and_deleted_by_its_owner()
    {
        var csv = Encoding.UTF8.GetBytes("metric,value\r\nsales.revenue,12.5\r\n");

        var id = await _fixture.StoreGenerated.HandleAsync(new StoreGeneratedFileCommand(
            FileOwnerTypes.ReportingExportJob, Guid.NewGuid(), "rapport.csv", "text/csv", csv));
        var read = await _fixture.Read.HandleAsync(new ReadFileContentQuery(id));
        var deleted = await _fixture.DeleteOwned.HandleAsync(new DeleteOwnedFileCommand(id));
        var again = await _fixture.DeleteOwned.HandleAsync(new DeleteOwnedFileCommand(id));

        Assert.Equal(("rapport.csv", "text/csv"), (read.FileName, read.ContentType));
        Assert.Equal(csv, read.Content);
        Assert.Equal((true, false), (deleted, again));
    }

    [Fact]
    public async Task Storing_the_generated_file_of_the_same_owner_again_returns_the_file_already_stored()
    {
        var owner = Guid.NewGuid();
        var csv = Encoding.UTF8.GetBytes("metric,value\r\n");

        var first = await _fixture.StoreGenerated.HandleAsync(new StoreGeneratedFileCommand(FileOwnerTypes.ReportingExportJob, owner, "rapport.csv", "text/csv", csv));
        var second = await _fixture.StoreGenerated.HandleAsync(new StoreGeneratedFileCommand(FileOwnerTypes.ReportingExportJob, owner, "rapport.csv", "text/csv", csv));

        Assert.Equal(first, second);
        Assert.Single(_fixture.Files);
    }

    [Fact]
    public async Task A_generated_file_whose_content_does_not_match_is_refused()
    {
        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.StoreGenerated.HandleAsync(new StoreGeneratedFileCommand(
            FileOwnerTypes.ReportingExportJob, Guid.NewGuid(), "rapport.csv", "text/csv", Samples.Executable())));

        Assert.Equal(FileErrors.ContentTypeMismatch.Code, refused.Code);
        Assert.Equal(MediaFileStatus.Quarantined, Assert.Single(_fixture.Files).Status);
    }

    // --- Isolation

    [Fact]
    public async Task A_restaurant_never_sees_confirms_nor_deletes_the_files_of_another()
    {
        var ticket = await RequestPngAsync(_fixture.Product());
        _fixture.UploadBytes(ticket.FileId, Samples.Png());
        _fixture.CurrentTenantId = _fixture.TenantB;

        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Get.HandleAsync(new GetFileQuery(ticket.FileId)));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Complete.HandleAsync(new CompleteUploadCommand(ticket.FileId)));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Delete.HandleAsync(new DeleteFileCommand(ticket.FileId)));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Download.HandleAsync(new GetDownloadLinkQuery(ticket.FileId)));

        Assert.Equal(MediaFileStatus.Pending, _fixture.FileById(ticket.FileId).Status);
    }

    // --- Reconnaissance du type

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, "application/pdf")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, "image/webp")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x41, 0x56, 0x45 }, "application/octet-stream")]
    public void The_type_is_recognized_from_the_first_bytes_not_from_the_declaration(byte[] head, string expected)
    {
        Assert.Equal(expected, ContentSniffer.Detect(head, ContentType.Create("image/png")).Value);
    }

    [Fact]
    public void Text_is_recognized_only_for_a_declared_text_type_without_binary_bytes()
    {
        var csv = ContentType.Create("text/csv");

        Assert.Equal("text/csv", ContentSniffer.Detect("a;b\r\nété;1\n"u8, csv).Value);
        Assert.Equal("application/octet-stream", ContentSniffer.Detect(new byte[] { 0x61, 0x00, 0x62 }, csv).Value);
        Assert.Equal("application/octet-stream", ContentSniffer.Detect("a;b"u8, ContentType.Create("image/png")).Value);
    }
}
