namespace Files.Api;

/// <summary>Déclaration d'un fichier à téléverser : propriétaire (code du vocabulaire, id), nom, type et taille annoncés.</summary>
/// <param name="ThumbnailOf">La photo dont ce fichier est la miniature ; absent pour une photo.</param>
public sealed record RequestUploadRequest(string? OwnerType, Guid OwnerId, string? FileName, string? ContentType, long SizeInBytes, Guid? ThumbnailOf = null);
