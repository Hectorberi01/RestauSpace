using Audit.Application.Abstractions;
using Audit.Domain;

namespace Audit.Application.Commands;

/// <summary>
/// Journalise une action sensible. SEUL point d'écriture du journal ; appelé par l'hôte qui traduit les événements des
/// autres modules (outbox), jamais par une route. Le tenant est celui de l'événement source ; l'origine
/// (<paramref name="CorrelationId"/>) rend l'écriture idempotente : un rejeu ne crée rien (renvoie faux).
/// Les métadonnées doivent venir d'une LISTE BLANCHE de champs par action ; le domaine expurge en plus ce qui ressemble
/// à un secret.
/// </summary>
public sealed record RecordAuditEntryCommand(
    Guid? TenantId,
    ActorType? ActorKind,
    string? ActorValue,
    string Action,
    string TargetType,
    string TargetId,
    string CorrelationId,
    IReadOnlyDictionary<string, string>? Metadata = null,
    DateTimeOffset? OccurredAtUtc = null) : ICommand<bool>;

public sealed class RecordAuditEntryHandler(IAuditEntryRepository entries, IAuditUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<RecordAuditEntryCommand, bool>
{
    public async Task<bool> HandleAsync(RecordAuditEntryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = command.TenantId is { } tenantId ? TenantId.From(tenantId) : (TenantId?)null;
        var actor = command.ActorKind is { } kind ? ActorId.Create(kind, command.ActorValue) : null;
        var action = AuditAction.Create(command.Action);
        var target = AuditTarget.Create(command.TargetType, command.TargetId);
        var correlation = CorrelationId.Create(command.CorrelationId);
        var metadata = AuditMetadata.Create(command.Metadata);

        if (await entries.ExistsAsync(correlation, action, target, cancellationToken))
        {
            return false;
        }

        entries.Add(AuditEntry.Create(tenant, actor, action, target, correlation, metadata, clock, command.OccurredAtUtc));
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (ConcurrencyException exception) when (exception.Error == AuditApplicationErrors.AlreadyRecorded)
        {
            // Deux consommateurs ont journalisé le même événement en même temps : l'index unique a gardé le premier.
            return false;
        }
    }
}
