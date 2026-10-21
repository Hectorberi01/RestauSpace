namespace Customers.Domain;

// Les événements ne portent que des ids et des indicateurs : aucune donnée personnelle ne sort de l'agrégat par eux.

public sealed record CustomerCreated(
    TenantId TenantId,
    CustomerId CustomerId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record CustomerContactChanged(
    TenantId TenantId,
    CustomerId CustomerId,
    bool EmailChanged,
    bool PhoneChanged,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record CustomerContactRequirementChanged(
    TenantId TenantId,
    CustomerId CustomerId,
    ContactRequirement Requirement,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record CustomerAnonymized(
    TenantId TenantId,
    CustomerId CustomerId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Doublon fusionné : son historique (commandes, avis, fidélité) est à rattacher à <paramref name="MergedIntoId"/>.</summary>
public sealed record CustomerMerged(
    TenantId TenantId,
    CustomerId CustomerId,
    CustomerId MergedIntoId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
