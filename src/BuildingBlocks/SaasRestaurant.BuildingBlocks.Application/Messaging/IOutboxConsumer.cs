namespace SaasRestaurant.BuildingBlocks.Application;

/// <summary>Un domain event relu depuis l'outbox d'un module. <see cref="Content"/> est son JSON.</summary>
public sealed record OutboxEnvelope(string Module, long Id, string Type, string Content, DateTimeOffset OccurredAtUtc, int Version = 1);

/// <summary>
/// Réagit aux événements de n'importe quel module une fois commités. Livraison au moins une fois : un consommateur doit
/// tolérer de recevoir deux fois le même message, et ignorer ce qu'il ne connaît pas.
/// </summary>
public interface IOutboxConsumer
{
    Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default);
}
