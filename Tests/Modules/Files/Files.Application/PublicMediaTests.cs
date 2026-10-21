using Files.Application.Commands;
using Files.Application.Queries;
using Files.Application.Storage;
using Files.Domain;

namespace Files.Application;

public sealed class PublicMediaTests
{
    private readonly FilesFixture _fixture = new();

    private async Task<Guid> AvailablePhotoAsync(Guid product)
    {
        var ticket = await _fixture.Request.HandleAsync(new RequestUploadCommand("catalog.product", product, "photo.png", "image/png", 64));
        _fixture.UploadBytes(ticket.FileId, Samples.Png());
        await _fixture.Complete.HandleAsync(new CompleteUploadCommand(ticket.FileId));
        return ticket.FileId;
    }

    [Fact]
    public async Task Each_available_photo_gets_a_stable_public_link_oldest_first()
    {
        var burger = _fixture.Product();
        var drink = _fixture.Product();
        var first = await AvailablePhotoAsync(burger);
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await AvailablePhotoAsync(burger);
        var pending = await _fixture.Request.HandleAsync(new RequestUploadCommand("catalog.product", drink, "photo.png", "image/png", 64));

        var images = await _fixture.PublicImages.HandleAsync(new ListPublicImagesQuery("catalog.product", [burger, drink]));
        var again = await _fixture.PublicImages.HandleAsync(new ListPublicImagesQuery("catalog.product", [burger]));

        Assert.Equal([first, second], images.Select(image => image.FileId));
        Assert.All(images, image => Assert.Equal(burger, image.OwnerId));
        Assert.DoesNotContain(images, image => image.FileId == pending.FileId);
        Assert.Equal(images.Select(image => image.Url), again.Select(image => image.Url));
        Assert.StartsWith("https://api.test/v1/media/", images[0].Url.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(_fixture.FileById(first).StorageKey.Value, images[0].Url.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_public_link_serves_the_photo_of_its_own_restaurant_only()
    {
        var photo = await AvailablePhotoAsync(_fixture.Product());
        var url = (await _fixture.PublicImages.HandleAsync(new ListPublicImagesQuery("catalog.product", [_fixture.FileById(photo).Owner.OwnerId]))).Single().Url;
        var token = url.Segments[^1];
        _fixture.CurrentTenantId = null;

        var served = await _fixture.PublicMedia.HandleAsync(new ReadPublicMediaQuery(token));
        var forgedTenant = _fixture.Links.UrlFor(_fixture.TenantB, FileId.From(photo))!.Segments[^1];
        var tampered = token[..^2] + (token[^2] == 'A' ? "BA" : "AA");

        Assert.NotNull(served);
        Assert.Equal(("image/png", photo), (served.ContentType, served.FileId));
        Assert.Null(await _fixture.PublicMedia.HandleAsync(new ReadPublicMediaQuery(forgedTenant)));
        Assert.Null(await _fixture.PublicMedia.HandleAsync(new ReadPublicMediaQuery(tampered)));
        Assert.Null(await _fixture.PublicMedia.HandleAsync(new ReadPublicMediaQuery("not-a-token")));
        Assert.Null(await _fixture.PublicMedia.HandleAsync(new ReadPublicMediaQuery(null)));
    }

    [Fact]
    public async Task A_deleted_photo_an_export_or_a_suspended_restaurant_is_never_served()
    {
        var deleted = await AvailablePhotoAsync(_fixture.Product());
        await _fixture.Delete.HandleAsync(new DeleteFileCommand(deleted));
        var export = await _fixture.StoreGenerated.HandleAsync(
            new StoreGeneratedFileCommand("reporting.export_job", Guid.NewGuid(), "ventes.csv", "text/csv", "a;b\n1;2\n"u8.ToArray()));
        var photo = await AvailablePhotoAsync(_fixture.Product());

        var exports = await _fixture.PublicImages.HandleAsync(new ListPublicImagesQuery("reporting.export_job", [_fixture.FileById(export).Owner.OwnerId]));
        var exportServed = await _fixture.PublicMedia.HandleAsync(new ReadPublicMediaQuery(Token(export)));
        var deletedServed = await _fixture.PublicMedia.HandleAsync(new ReadPublicMediaQuery(Token(deleted)));
        _fixture.Writable[_fixture.TenantA] = false;
        var suspended = await _fixture.PublicMedia.HandleAsync(new ReadPublicMediaQuery(Token(photo)));

        Assert.Empty(exports);
        Assert.Null(exportServed);
        Assert.Null(deletedServed);
        Assert.Null(suspended);
    }

    [Fact]
    public async Task Without_a_signing_key_no_link_is_produced_nor_accepted()
    {
        var photo = await AvailablePhotoAsync(_fixture.Product());
        var token = Token(photo);
        _fixture.Links = PublicMediaLinks.Disabled;

        var images = await _fixture.PublicImages.HandleAsync(new ListPublicImagesQuery("catalog.product", [_fixture.FileById(photo).Owner.OwnerId]));

        Assert.Empty(images);
        Assert.Null(await _fixture.PublicMedia.HandleAsync(new ReadPublicMediaQuery(token)));
        Assert.Throws<InvalidOperationException>(() => new PublicMediaLinks(new Uri("https://api.test"), new byte[16]));
    }

    private string Token(Guid fileId) => _fixture.Links.UrlFor(_fixture.TenantA, FileId.From(fileId))!.Segments[^1];

    [Fact]
    public async Task A_logo_gets_its_public_link_but_an_export_never_does()
    {
        _fixture.Owners.Add((_fixture.TenantA, "tenants.tenant", _fixture.TenantA.Value));
        var ticket = await _fixture.Request.HandleAsync(new RequestUploadCommand("tenants.tenant", _fixture.TenantA.Value, "logo.png", "image/png", 64));
        _fixture.UploadBytes(ticket.FileId, Samples.Png());
        await _fixture.Complete.HandleAsync(new CompleteUploadCommand(ticket.FileId));
        var export = await _fixture.StoreGenerated.HandleAsync(
            new StoreGeneratedFileCommand("reporting.export_job", Guid.NewGuid(), "ventes.csv", "text/csv", "a;b\n1;2\n"u8.ToArray()));

        var link = await new GetPublicLinkHandler(_fixture.Workspace, _fixture.Links).HandleAsync(new GetPublicLinkQuery(ticket.FileId));
        var refused = await Assert.ThrowsAsync<DomainException>(() => new GetPublicLinkHandler(_fixture.Workspace, _fixture.Links).HandleAsync(new GetPublicLinkQuery(export)));

        Assert.Equal(_fixture.Links.UrlFor(_fixture.TenantA, FileId.From(ticket.FileId)), link.Url);
        Assert.Equal(FilesApplicationErrors.NotPublic, refused.Error);
    }

    [Fact]
    public async Task A_photo_carries_its_thumbnail_which_goes_away_with_it()
    {
        var burger = _fixture.Product();
        var photo = await AvailablePhotoAsync(burger);
        var ticket = await _fixture.Request.HandleAsync(new RequestUploadCommand("catalog.product", burger, "mini.png", "image/png", 64, ThumbnailOf: photo));
        _fixture.UploadBytes(ticket.FileId, Samples.Png());
        await _fixture.Complete.HandleAsync(new CompleteUploadCommand(ticket.FileId));

        var listed = await _fixture.List.HandleAsync(new ListOwnerFilesQuery("catalog.product", burger));
        var images = await _fixture.PublicImages.HandleAsync(new ListPublicImagesQuery("catalog.product", [burger]));
        var otherOwner = await Assert.ThrowsAsync<DomainException>(() => _fixture.Request.HandleAsync(
            new RequestUploadCommand("catalog.product", _fixture.Product(), "mini.png", "image/png", 64, ThumbnailOf: photo)));
        var tooBig = await Assert.ThrowsAsync<DomainException>(() => _fixture.Request.HandleAsync(
            new RequestUploadCommand("catalog.product", burger, "mini.png", "image/png", MediaFile.MaxThumbnailSizeInBytes + 1, ThumbnailOf: photo)));
        var ofThumbnail = await Assert.ThrowsAsync<DomainException>(() => _fixture.Request.HandleAsync(
            new RequestUploadCommand("catalog.product", burger, "mini.png", "image/png", 64, ThumbnailOf: ticket.FileId)));
        await _fixture.Delete.HandleAsync(new DeleteFileCommand(photo));

        var file = Assert.Single(listed);
        Assert.Equal((photo, ticket.FileId), (file.Id, file.ThumbnailId));
        var image = Assert.Single(images);
        Assert.Equal(_fixture.Links.UrlFor(_fixture.TenantA, FileId.From(ticket.FileId)), image.ThumbnailUrl);
        Assert.Equal(FilesApplicationErrors.ThumbnailOwnerMismatch, otherOwner.Error);
        Assert.Equal(FileErrors.SizeExceedsLimit, tooBig.Error);
        Assert.Equal(FileErrors.ThumbnailOfThumbnail, ofThumbnail.Error);
        Assert.Equal(MediaFileStatus.Deleted, _fixture.FileById(ticket.FileId).Status);
    }
}
