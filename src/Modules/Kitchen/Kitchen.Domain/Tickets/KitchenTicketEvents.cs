namespace Kitchen.Domain;

/// <summary>Où est préparée une ligne d'un nouveau ticket : de quoi alimenter l'écran d'un poste depuis
/// l'événement.</summary>
public sealed record KitchenTicketLineRouting(KitchenTicketItemId ItemId, OrderItemId OrderItemId, StationId StationId);

/// <summary><see cref="Lines"/> liste chaque article du ticket, dans l'ordre du ticket.</summary>
public sealed record KitchenTicketCreated(
    TenantId TenantId,
    KitchenTicketId TicketId,
    OrderId OrderId,
    LocationId LocationId,
    KitchenPriority Priority,
    int ItemCount,
    IReadOnlyList<KitchenTicketLineRouting> Lines,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record KitchenPreparationStarted(TenantId TenantId, KitchenTicketId TicketId, OrderId OrderId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record KitchenTicketItemStarted(
    TenantId TenantId,
    KitchenTicketId TicketId,
    LocationId LocationId,
    KitchenTicketItemId ItemId,
    OrderItemId OrderItemId,
    StationId StationId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record KitchenTicketItemReady(
    TenantId TenantId,
    KitchenTicketId TicketId,
    LocationId LocationId,
    KitchenTicketItemId ItemId,
    OrderItemId OrderItemId,
    StationId StationId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Avec une policy de disponibilité partielle, <see cref="ReadyItemCount"/> peut être inférieur à
/// <see cref="TotalItemCount"/>.</summary>
public sealed record KitchenTicketReady(
    TenantId TenantId,
    KitchenTicketId TicketId,
    OrderId OrderId,
    LocationId LocationId,
    int ReadyItemCount,
    int TotalItemCount,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record KitchenTicketCompleted(TenantId TenantId, KitchenTicketId TicketId, OrderId OrderId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <summary><see cref="Reason"/> est le motif amont (ex. celui de l'annulation de la commande) quand il a été
/// fourni.</summary>
public sealed record KitchenTicketCancelled(
    TenantId TenantId,
    KitchenTicketId TicketId,
    OrderId OrderId,
    LocationId LocationId,
    string? Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record KitchenTicketPriorityChanged(
    TenantId TenantId,
    KitchenTicketId TicketId,
    LocationId LocationId,
    KitchenPriority PreviousPriority,
    KitchenPriority NewPriority,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>La salle envoie un service en cuisine : ses plats en attente peuvent être préparés.</summary>
public sealed record KitchenCourseFired(
    TenantId TenantId,
    KitchenTicketId TicketId,
    LocationId LocationId,
    KitchenCourse Course,
    int ItemCount,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
