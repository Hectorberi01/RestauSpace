using Kitchen.Domain;

namespace Kitchen.Api;

public sealed record CreatedResponse(Guid Id);

public sealed record CreateStationRequest(Guid LocationId, string? Name);

public sealed record NameRequest(string? Name);

public sealed record CreateRoutingRequest(Guid DefaultStationId);

public sealed record StationRequest(Guid StationId);

public sealed record CreateTicketRequest(Guid OrderId);

public sealed record CancelTicketRequest(string? Reason = null);

public sealed record PriorityRequest(KitchenPriority Priority);

public sealed record CourseRequest(KitchenCourse Course);
