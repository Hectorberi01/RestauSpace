using System.Collections.Concurrent;
using Files.Application.Abstractions;
using Files.Domain;

namespace Files.Infrastructure.Storage;

/// <summary>
/// Stockage en mémoire du processus (développement et tests). Les « URLs signées » pointent vers un schéma fictif :
/// le test dépose lui-même les octets avec <see cref="Upload"/>, comme le ferait le navigateur.
/// </summary>
public sealed class InMemoryObjectStorage : IObjectStorage
{
    private readonly ConcurrentDictionary<string, (string ContentType, byte[] Content)> _objects = new(StringComparer.Ordinal);

    public int Count => _objects.Count;

    public bool Contains(string key) => _objects.ContainsKey(key);

    /// <summary>Simule l'envoi direct du navigateur vers l'URL signée.</summary>
    public void Upload(string key, string contentType, byte[] content) => _objects[key] = (contentType, content);

    public Uri PresignUpload(StorageKey key, ContentType contentType, long sizeInBytes, TimeSpan lifetime) =>
        new($"memory://upload/{key.Value}");

    public Uri PresignDownload(StorageKey key, FileName fileName, TimeSpan lifetime) => new($"memory://download/{key.Value}");

    public Task<StoredObject?> InspectAsync(StorageKey key, int headLength, CancellationToken cancellationToken = default) =>
        Task.FromResult<StoredObject?>(_objects.TryGetValue(key.Value, out var stored)
            ? new StoredObject(stored.Content.LongLength, stored.Content[..Math.Min(headLength, stored.Content.Length)])
            : null);

    public Task PutAsync(StorageKey key, ContentType contentType, byte[] content, CancellationToken cancellationToken = default)
    {
        _objects[key.Value] = (contentType.Value, content);
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetAsync(StorageKey key, CancellationToken cancellationToken = default) =>
        Task.FromResult<byte[]?>(_objects.TryGetValue(key.Value, out var stored) ? stored.Content : null);

    public Task DeleteAsync(StorageKey key, CancellationToken cancellationToken = default)
    {
        _ = _objects.TryRemove(key.Value, out _);
        return Task.CompletedTask;
    }
}

/// <summary>Pas d'antivirus configuré : tout fichier est considéré propre (l'hôte peut en fournir un).</summary>
internal sealed class NoFileScanner : IFileScanner
{
    public Task<string?> ScanAsync(StorageKey key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}
