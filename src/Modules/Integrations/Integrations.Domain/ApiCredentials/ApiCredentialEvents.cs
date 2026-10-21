namespace Integrations.Domain;

// Événements des identifiants d'API : jamais une clé ni son empreinte. Actor : l'auteur, pour l'audit (null quand
// l'appelant ne l'a pas fourni).

public sealed record ApiCredentialIssued(
    TenantId TenantId,
    CredentialId CredentialId,
    string Name,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset OccurredAtUtc,
    ChangeActor? Actor = null,
    IReadOnlyList<string>? Scopes = null) : DomainEvent(OccurredAtUtc);

public sealed record ApiCredentialRotated(
    TenantId TenantId,
    CredentialId CredentialId,
    DateTimeOffset OccurredAtUtc,
    ChangeActor? Actor = null) : DomainEvent(OccurredAtUtc);

public sealed record ApiCredentialRevoked(
    TenantId TenantId,
    CredentialId CredentialId,
    DateTimeOffset OccurredAtUtc,
    ChangeActor? Actor = null) : DomainEvent(OccurredAtUtc);
