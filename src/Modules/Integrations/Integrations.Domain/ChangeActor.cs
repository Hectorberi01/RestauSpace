namespace Integrations.Domain;

/// <summary>Qui est à l'origine d'un changement : un utilisateur, une clé d'API ou un traitement du système.</summary>
public enum ChangeActorKind
{
    User = 1,
    ApiCredential = 2,
    System = 3,
}

/// <summary>
/// L'auteur d'une action sensible, porté par les événements du module pour le journal d'audit (module Audit). Une
/// référence opaque : un id, jamais un nom, un e-mail ni une clé. Pour <see cref="ChangeActorKind.System"/>, l'id est
/// absent et <see cref="Name"/> nomme le traitement (« webhook-delivery-job »).
/// </summary>
public sealed record ChangeActor
{
    public const int MaxNameLength = 100;

    private ChangeActor(ChangeActorKind kind, Guid? id, string? name)
    {
        Kind = kind;
        Id = id;
        Name = name;
    }

    public ChangeActorKind Kind { get; }

    public Guid? Id { get; }

    public string? Name { get; }

    public static ChangeActor ForUser(Guid userId)
    {
        DomainException.ThrowIf(userId == Guid.Empty, CommonErrors.EmptyId);
        return new ChangeActor(ChangeActorKind.User, userId, null);
    }

    public static ChangeActor ForApiCredential(CredentialId credentialId)
    {
        Guard.NotEmpty(credentialId);
        return new ChangeActor(ChangeActorKind.ApiCredential, credentialId.Value, null);
    }

    public static ChangeActor ForSystem(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(name.Length, MaxNameLength, nameof(name));
        return new ChangeActor(ChangeActorKind.System, null, name);
    }
}
