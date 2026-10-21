namespace Reviews.Domain;

public sealed record FeedbackSubmitted(
    TenantId TenantId,
    FeedbackId FeedbackId,
    LocationId LocationId,
    CustomerId? CustomerId,
    OrderId? OrderId,
    int Rating,
    bool HasComment,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record FeedbackReviewed(
    TenantId TenantId,
    FeedbackId FeedbackId,
    UserId ReviewedBy,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record FeedbackArchived(
    TenantId TenantId,
    FeedbackId FeedbackId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Ids uniquement : l'auteur effacé est délibérément absent.</summary>
public sealed record FeedbackCustomerDetached(
    TenantId TenantId,
    FeedbackId FeedbackId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record FeedbackPublished(TenantId TenantId, FeedbackId FeedbackId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record FeedbackUnpublished(TenantId TenantId, FeedbackId FeedbackId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
