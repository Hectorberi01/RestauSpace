using Files.Application.Abstractions;
using Files.Domain;
using Files.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Files.Infrastructure;

/// <summary>
/// Persistance sur un vrai PostgreSQL (Docker) : relecture par le domaine, isolation par tenant, recherche par
/// propriétaire, garde de la clé de stockage, file du job, outbox et effacement de l'objet sur <c>FileDeleted</c>.
/// </summary>
public sealed class FilesPersistenceTests(FilesDatabase database) : IClassFixture<FilesDatabase>
{
    private FakeClock Clock => database.Clock;

    private MediaFile NewFile(TenantId tenant, FileOwner owner, string? key = null) => MediaFile.CreatePending(
        tenant,
        owner,
        FileName.Create("burger.png"),
        ContentType.Create("image/png"),
        64,
        StorageKey.Create(key ?? $"tenants/{tenant.Value:N}/catalog.product/{Guid.NewGuid():N}"),
        FileUploadPolicy.Default,
        Clock);

    private async Task<MediaFile> SavedAsync(TenantId tenant, FileOwner owner, string? key = null)
    {
        var file = NewFile(tenant, owner, key);
        await database.SaveAsync(tenant, services =>
        {
            services.GetRequiredService<IMediaFileRepository>().Add(file);
            return Task.CompletedTask;
        });
        return file;
    }

    [Fact]
    public async Task A_file_is_stored_read_back_through_the_domain_and_seen_only_by_its_restaurant()
    {
        var tenant = TenantId.New();
        var owner = FileOwner.Create("catalog.product", Guid.NewGuid());
        var file = await SavedAsync(tenant, owner);

        await using var own = database.NewScope(tenant);
        await using var other = database.NewScope(TenantId.New());
        var loaded = await own.ServiceProvider.GetRequiredService<IMediaFileRepository>().FindAsync(tenant, file.Id);

        Assert.NotNull(loaded);
        Assert.Equal((owner, file.StorageKey, MediaFileStatus.Pending, 64L), (loaded.Owner, loaded.StorageKey, loaded.Status, loaded.SizeInBytes));
        Assert.Null(await other.ServiceProvider.GetRequiredService<IMediaFileRepository>().FindAsync(tenant, file.Id));
    }

    [Fact]
    public async Task The_files_of_an_owner_are_listed_without_the_deleted_ones()
    {
        var tenant = TenantId.New();
        var owner = FileOwner.Create("catalog.product", Guid.NewGuid());
        var kept = await SavedAsync(tenant, owner);
        var removed = await SavedAsync(tenant, owner);
        _ = await SavedAsync(tenant, FileOwner.Create("catalog.product", Guid.NewGuid()));
        await database.SaveAsync(tenant, async services =>
            (await services.GetRequiredService<IMediaFileRepository>().FindAsync(tenant, removed.Id))!.Delete(Clock));

        await using var scope = database.NewScope(tenant);
        var listed = await scope.ServiceProvider.GetRequiredService<IMediaFileRepository>().ListByOwnerAsync(tenant, owner);

        Assert.Equal([kept.Id], listed.Select(file => file.Id));
    }

    [Fact]
    public async Task Two_files_never_share_a_storage_key()
    {
        var tenant = TenantId.New();
        var owner = FileOwner.Create("catalog.product", Guid.NewGuid());
        var key = $"tenants/{tenant.Value:N}/catalog.product/{Guid.NewGuid():N}";
        _ = await SavedAsync(tenant, owner, key);

        await Assert.ThrowsAsync<ConcurrencyException>(() => SavedAsync(tenant, owner, key));
    }

    [Fact]
    public async Task Abandoned_uploads_are_listed_for_the_job_across_restaurants()
    {
        var tenant = TenantId.New();
        var file = await SavedAsync(tenant, FileOwner.Create("catalog.product", Guid.NewGuid()));

        await using var platform = database.NewScope(null);
        var queue = platform.ServiceProvider.GetRequiredService<IAbandonedUploads>();
        var before = await queue.ListAsync(file.CreatedAtUtc.AddSeconds(-1), 1000);
        var after = await queue.ListAsync(file.CreatedAtUtc, 1000);

        Assert.DoesNotContain(new AbandonedUpload(tenant, file.Id), before);
        Assert.Contains(new AbandonedUpload(tenant, file.Id), after);
    }

    [Fact]
    public async Task Deleting_a_file_writes_FileDeleted_and_its_reaction_erases_the_stored_object()
    {
        var tenant = TenantId.New();
        var file = await SavedAsync(tenant, FileOwner.Create("catalog.product", Guid.NewGuid()));
        database.Storage.Upload(file.StorageKey.Value, "image/png", new byte[64]);
        await database.SaveAsync(tenant, async services =>
            (await services.GetRequiredService<IMediaFileRepository>().FindAsync(tenant, file.Id))!.Delete(Clock));

        await using var scope = database.NewScope(null);
        // content est en jsonb : le filtre sur le texte se fait après lecture.
        var message = (await scope.ServiceProvider.GetRequiredService<FilesDbContext>().OutboxMessages.AsNoTracking()
            .Where(candidate => candidate.Type == nameof(FileDeleted)).ToListAsync())
            .Single(candidate => candidate.Content.Contains(file.Id.Value.ToString(), StringComparison.Ordinal));
        foreach (var consumer in scope.ServiceProvider.GetServices<IOutboxConsumer>())
        {
            await consumer.HandleAsync(new OutboxEnvelope(DependencyInjection.Schema, message.Id, message.Type, message.Content, message.OccurredAtUtc));
        }

        Assert.False(database.Storage.Contains(file.StorageKey.Value));
    }
}
