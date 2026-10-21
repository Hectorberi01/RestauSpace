using Tenants.Application.Abstractions;
using Tenants.Domain.Prospects;

namespace Tenants.Application.Prospects;

public static class DemoRequestApplicationErrors
{
    public static readonly DomainError NotFound = new("tenants.demo_request.not_found", "The demo request does not exist.");

    public static readonly DomainError PageInvalid = new("tenants.demo_request.page_invalid", "skip must be positive and take between 1 and 100.");
}

public sealed record DemoRequestDto(
    Guid Id,
    string ContactName,
    string RestaurantName,
    string? Email,
    string? Phone,
    string Country,
    string? PlanInterest,
    string? Message,
    DemoRequestStatus Status,
    string? Note,
    DateTimeOffset SubmittedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static DemoRequestDto From(DemoRequest request) => new(
        request.Id.Value,
        request.ContactName,
        request.RestaurantName,
        request.Email?.Value,
        request.Phone?.Value,
        request.Country,
        request.PlanInterest,
        request.Message,
        request.Status,
        request.Note,
        request.SubmittedAtUtc,
        request.UpdatedAtUtc);
}

/// <summary>Demande envoyée par un prospect, sans compte : route publique, limitée en débit par l'hôte.</summary>
public sealed record SubmitDemoRequestCommand(
    string? ContactName,
    string? RestaurantName,
    string? Email,
    string? Phone,
    string? Country,
    string? PlanInterest,
    string? Message) : ICommand<Guid>;

public sealed class SubmitDemoRequestHandler(IDemoRequestRepository requests, ITenantsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<SubmitDemoRequestCommand, Guid>
{
    public async Task<Guid> HandleAsync(SubmitDemoRequestCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = DemoRequest.Submit(
            command.ContactName, command.RestaurantName, command.Email, command.Phone, command.Country, command.PlanInterest,
            command.Message, clock);
        requests.Add(request);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return request.Id.Value;
    }
}

public sealed record ListDemoRequestsQuery(DemoRequestStatus? Status, int Skip = 0, int Take = 50) : IQuery<IReadOnlyList<DemoRequestDto>>;

public sealed class ListDemoRequestsHandler(IDemoRequestRepository requests)
    : IQueryHandler<ListDemoRequestsQuery, IReadOnlyList<DemoRequestDto>>
{
    public async Task<IReadOnlyList<DemoRequestDto>> HandleAsync(ListDemoRequestsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        DomainException.ThrowIf(query.Skip < 0 || query.Take is < 1 or > 100, DemoRequestApplicationErrors.PageInvalid);
        return (await requests.ListAsync(query.Status, query.Skip, query.Take, cancellationToken)).Select(DemoRequestDto.From).ToList();
    }
}

public sealed record GetDemoRequestQuery(Guid Id) : IQuery<DemoRequestDto>;

public sealed class GetDemoRequestHandler(IDemoRequestRepository requests) : IQueryHandler<GetDemoRequestQuery, DemoRequestDto>
{
    public async Task<DemoRequestDto> HandleAsync(GetDemoRequestQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var request = await requests.FindAsync(DemoRequestId.From(query.Id), cancellationToken)
            ?? throw new NotFoundException(DemoRequestApplicationErrors.NotFound);
        return DemoRequestDto.From(request);
    }
}

/// <summary>Suivi par l'équipe commerciale : statut et note interne, remplacés en bloc.</summary>
public sealed record UpdateDemoRequestCommand(Guid Id, DemoRequestStatus Status, string? Note) : ICommand;

public sealed class UpdateDemoRequestHandler(IDemoRequestRepository requests, ITenantsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<UpdateDemoRequestCommand>
{
    public async Task HandleAsync(UpdateDemoRequestCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = await requests.FindAsync(DemoRequestId.From(command.Id), cancellationToken)
            ?? throw new NotFoundException(DemoRequestApplicationErrors.NotFound);
        request.ChangeStatus(command.Status, clock);
        request.ChangeNote(command.Note, clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Suppression définitive, à la demande du prospect ou quand la demande n'a plus d'utilité.</summary>
public sealed record DeleteDemoRequestCommand(Guid Id) : ICommand;

public sealed class DeleteDemoRequestHandler(IDemoRequestRepository requests, ITenantsUnitOfWork unitOfWork)
    : ICommandHandler<DeleteDemoRequestCommand>
{
    public async Task HandleAsync(DeleteDemoRequestCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = await requests.FindAsync(DemoRequestId.From(command.Id), cancellationToken)
            ?? throw new NotFoundException(DemoRequestApplicationErrors.NotFound);
        requests.Remove(request);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
