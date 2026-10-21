namespace Audit.Domain;

/// <summary>
/// Enregistrement d'audit en ajout seul. C'est sa propre racine de persistance, mais délibérément PAS un AggregateRoot :
/// aucun comportement après création, aucun mutateur, aucun événement levé. Tous les membres sont en lecture seule.
/// L'ORM reconstruit une écriture par son constructeur complet (aucun constructeur vide, aucun setter).
/// </summary>
public sealed class AuditEntry : Entity<AuditId>
{
    private AuditEntry(
        AuditId id,
        TenantId? tenantId,
        ActorId? actorId,
        AuditAction action,
        AuditTarget target,
        CorrelationId correlationId,
        AuditMetadata metadata,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset recordedAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        ActorId = actorId;
        Action = action;
        Target = target;
        CorrelationId = correlationId;
        Metadata = metadata;
        OccurredAtUtc = occurredAtUtc;
        RecordedAtUtc = recordedAtUtc;
    }

    /// <summary>Null pour les actions de niveau plateforme qui n'appartiennent à aucun tenant.</summary>
    public TenantId? TenantId { get; }

    /// <summary>Null quand l'action n'a pas d'acteur identifiable (requête anonyme).</summary>
    public ActorId? ActorId { get; }

    public AuditAction Action { get; }

    public AuditTarget Target { get; }

    public string TargetType => Target.Type;

    public string TargetId => Target.Id;

    public CorrelationId CorrelationId { get; }

    public AuditMetadata Metadata { get; }

    /// <summary>Quand l'action a eu lieu (l'événement source), jamais après <see cref="RecordedAtUtc"/>.</summary>
    public DateTimeOffset OccurredAtUtc { get; }

    /// <summary>Quand l'écriture a été faite dans le journal (horloge du module).</summary>
    public DateTimeOffset RecordedAtUtc { get; }

    /// <param name="occurredAtUtc">
    /// Instant de l'action quand l'écriture la suit (événement relayé par l'outbox) ; par défaut, maintenant. Un instant
    /// futur est ramené à maintenant : le journal ne peut pas dater une action après son enregistrement.
    /// </param>
    public static AuditEntry Create(
        TenantId? tenantId,
        ActorId? actorId,
        AuditAction action,
        AuditTarget target,
        CorrelationId correlationId,
        AuditMetadata? metadata,
        IClock clock,
        DateTimeOffset? occurredAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(correlationId);
        ArgumentNullException.ThrowIfNull(clock);
        if (tenantId is { } knownTenantId)
        {
            Guard.NotEmpty(knownTenantId);
        }

        var now = clock.UtcNow;
        var occurred = occurredAtUtc?.ToUniversalTime() is { } given && given < now ? given : now;
        return new AuditEntry(
            AuditId.New(),
            tenantId,
            actorId,
            action,
            target,
            correlationId,
            metadata ?? AuditMetadata.Empty,
            occurred,
            now);
    }
}
