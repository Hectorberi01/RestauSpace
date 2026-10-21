namespace Files.Domain;

/// <summary>Le binaire est stocké et le fichier est devenu disponible.</summary>
public sealed record FileUploaded(
    TenantId TenantId,
    FileId FileId,
    string OwnerType,
    Guid OwnerId,
    string ContentType,
    long SizeInBytes,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record FileQuarantined(
    TenantId TenantId,
    FileId FileId,
    string OwnerType,
    Guid OwnerId,
    string Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Porte la clé de stockage pour que le handler puisse supprimer le binaire sans recharger l'agrégat.</summary>
public sealed record FileDeleted(
    TenantId TenantId,
    FileId FileId,
    string OwnerType,
    Guid OwnerId,
    string StorageKey,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
