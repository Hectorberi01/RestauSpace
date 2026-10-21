namespace Staff.Domain;

/// <summary>
/// Référence à un établissement détenu par le module Locations. Publique car la location vit dans un autre module :
/// la couche Application la construit à partir du TenantId et de l'Id de la location qu'elle y a chargée, donc
/// <see cref="TenantId"/> est le vrai propriétaire de l'établissement, jamais une saisie utilisateur.
/// <see cref="IsOperational"/> n'est vrai que tant que la location y est Active (ni fermée ni archivée).
/// </summary>
public sealed record LocationReference(TenantId TenantId, LocationId LocationId, bool IsOperational)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);

    public LocationId LocationId { get; } = Guard.NotEmpty(LocationId);
}
