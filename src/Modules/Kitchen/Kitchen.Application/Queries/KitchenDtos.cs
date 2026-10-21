using Kitchen.Domain;

namespace Kitchen.Application.Queries;

public sealed record StationDto(Guid Id, Guid LocationId, string Name, bool IsActive)
{
    public static StationDto From(KitchenStation station)
    {
        ArgumentNullException.ThrowIfNull(station);
        return new StationDto(station.Id.Value, station.LocationId.Value, station.Name.Value, station.IsActive);
    }
}

public sealed record StationRouteDto(Guid ProductId, Guid StationId);

public sealed record ProductCourseDto(Guid ProductId, KitchenCourse Course);

/// <param name="Courses">Les produits servis en entrée, plat ou dessert ; les autres partent directement.</param>
public sealed record StationRoutingDto(
    Guid Id, Guid LocationId, Guid DefaultStationId, IReadOnlyList<StationRouteDto> Routes, IReadOnlyList<ProductCourseDto>? Courses = null)
{
    public static StationRoutingDto From(StationRouting routing)
    {
        ArgumentNullException.ThrowIfNull(routing);
        return new StationRoutingDto(
            routing.Id.Value,
            routing.LocationId.Value,
            routing.DefaultStationId.Value,
            routing.Routes.Select(route => new StationRouteDto(route.ProductId.Value, route.StationId.Value)).ToList(),
            routing.Courses.Select(course => new ProductCourseDto(course.ProductId.Value, course.Course)).ToList());
    }
}

public sealed record KitchenTicketItemDto(
    Guid Id,
    Guid OrderItemId,
    Guid StationId,
    string Name,
    string? VariantName,
    IReadOnlyList<string> Modifiers,
    string? Note,
    int Quantity,
    KitchenTicketItemStatus Status,
    KitchenCourse Course = KitchenCourse.Direct,
    bool IsHeld = false);

/// <param name="CanBeMarkedReady">Le passe peut « bumper » le ticket maintenant.</param>
/// <param name="CanBeCompleted">Le ticket peut être remis au service maintenant.</param>
public sealed record KitchenTicketDto(
    Guid Id,
    Guid OrderId,
    Guid LocationId,
    KitchenTicketStatus Status,
    KitchenPriority Priority,
    int RequiredReadyPercentage,
    bool CanBeMarkedReady,
    bool CanBeCompleted,
    IReadOnlyList<KitchenTicketItemDto> Items,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? ReadyAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    string? CancellationReason,
    IReadOnlyList<KitchenCourse>? HeldCourses = null)
{
    public static KitchenTicketDto From(KitchenTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return new KitchenTicketDto(
            ticket.Id.Value,
            ticket.OrderId.Value,
            ticket.LocationId.Value,
            ticket.Status,
            ticket.Priority,
            ticket.ReadinessPolicy.RequiredReadyPercentage,
            ticket.CanBeMarkedReady,
            ticket.CanBeCompleted,
            ticket.Items
                .Select(item => new KitchenTicketItemDto(
                    item.Id.Value,
                    item.OrderItemId.Value,
                    item.StationId.Value,
                    item.Name,
                    item.VariantName,
                    item.Modifiers.ToList(),
                    item.Note,
                    item.Quantity,
                    item.Status,
                    item.Course,
                    item.IsHeld))
                .ToList(),
            ticket.CreatedAtUtc,
            ticket.StartedAtUtc,
            ticket.ReadyAtUtc,
            ticket.CompletedAtUtc,
            ticket.CancelledAtUtc,
            ticket.CancellationReason,
            ticket.HeldCourses);
    }
}
