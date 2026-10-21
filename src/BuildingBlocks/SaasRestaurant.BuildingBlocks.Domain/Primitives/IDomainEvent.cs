namespace SaasRestaurant.BuildingBlocks.Domain;

/// <summary>Un fait survenu dans le domaine. Nommé au passé.</summary>
public interface IDomainEvent
{
    DateTimeOffset OccurredAtUtc { get; }
}

public abstract record DomainEvent(DateTimeOffset OccurredAtUtc) : IDomainEvent;
