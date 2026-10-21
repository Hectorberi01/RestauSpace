namespace Customers.Domain;

public sealed record ConsentGranted(
    TenantId TenantId,
    ConsentId ConsentId,
    CustomerId CustomerId,
    CommunicationChannel Channel,
    string Source,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record ConsentWithdrawn(
    TenantId TenantId,
    ConsentId ConsentId,
    CustomerId CustomerId,
    CommunicationChannel Channel,
    string Source,
    WithdrawalReason Reason,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
