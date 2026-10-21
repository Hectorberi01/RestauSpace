using Floor.Application.Abstractions;
using Floor.Domain;

namespace Floor.Application.Commands;

// File d'attente à la porte : les groupes arrivés sans réservation, du jour. L'équipe les ajoute, les prévient quand la
// table est prête (message au numéro donné, via l'hôte), puis les installe (une session de table s'ouvre) ou les retire.
// Rien n'est gardé au-delà de la journée ; le numéro est effacé dès la sortie de la file.

/// <param name="Phone">Facultatif (E.164) : pour prévenir le groupe par WhatsApp ou SMS.</param>
/// <param name="QuotedMinutes">Attente annoncée au client, en minutes.</param>
public sealed record AddWaitingPartyCommand(Guid LocationId, string? Name, int PartySize, string? Phone = null, string? Note = null, int? QuotedMinutes = null)
    : ICommand<Guid>
{
    public override string ToString() => $"AddWaitingPartyCommand ({LocationId}, {PartySize}, ***)";
}

public sealed class AddWaitingPartyHandler(FloorWorkspace workspace, IFloorLocations locations, IWaitingPartyRepository parties, IClock clock)
    : ICommandHandler<AddWaitingPartyCommand, Guid>
{
    /// <summary>Au-delà, un groupe sorti de la file est effacé.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(12);

    public async Task<Guid> HandleAsync(AddWaitingPartyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = (command.LocationId == Guid.Empty ? null : await locations.FindAsync(tenant, LocationId.From(command.LocationId), cancellationToken))
            ?? throw new NotFoundException(FloorApplicationErrors.LocationNotFound);
        var phone = string.IsNullOrWhiteSpace(command.Phone) ? null : PhoneNumber.Create(command.Phone);
        var party = WaitingParty.Create(tenant, location, command.Name, phone, command.PartySize, command.Note, command.QuotedMinutes, clock);
        await parties.PurgeClosedAsync(tenant, location.LocationId, clock.UtcNow - Retention, cancellationToken);
        parties.Add(party);
        await workspace.SaveAsync(cancellationToken);
        return party.Id.Value;
    }
}

/// <summary>Prévient le groupe que sa table est prête (message envoyé par l'hôte au numéro donné).</summary>
public sealed record NotifyWaitingPartyCommand(Guid PartyId) : ICommand;

public sealed class NotifyWaitingPartyHandler(FloorWorkspace workspace, IWaitingPartyRepository parties, IClock clock) : ICommandHandler<NotifyWaitingPartyCommand>
{
    public async Task HandleAsync(NotifyWaitingPartyCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var party = await WaitingParties.LoadAsync(workspace, parties, command.PartyId, cancellationToken);
        party.Notify(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Installe le groupe à une table libre et en service du même établissement. Renvoie l'id de la session ouverte.</summary>
public sealed record SeatWaitingPartyCommand(Guid PartyId, Guid TableId) : ICommand<Guid>;

public sealed class SeatWaitingPartyHandler(FloorWorkspace workspace, IWaitingPartyRepository parties, ITableOccupancyChecker occupancy, IClock clock)
    : ICommandHandler<SeatWaitingPartyCommand, Guid>
{
    public async Task<Guid> HandleAsync(SeatWaitingPartyCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var party = await WaitingParties.LoadAsync(workspace, parties, command.PartyId, cancellationToken);
        var area = await workspace.LoadDiningAreaOfTableAsync(tenant, command.TableId, cancellationToken);
        var table = area.GetTableReference(TableId.From(command.TableId));
        DomainException.ThrowIf(table.LocationId != party.LocationId, WaitingPartyErrors.OtherLocation);
        var session = TableSession.Open(
            tenant, table, party.PartySize, await occupancy.HasOpenSessionAsync(tenant, table.TableId, cancellationToken), clock);
        party.Seat(session.Id, clock);
        workspace.Sessions.Add(session);
        await workspace.SaveAsync(cancellationToken);
        return session.Id.Value;
    }
}

/// <summary>Le groupe est parti, ou l'équipe le retire de la file.</summary>
public sealed record RemoveWaitingPartyCommand(Guid PartyId) : ICommand;

public sealed class RemoveWaitingPartyHandler(FloorWorkspace workspace, IWaitingPartyRepository parties, IClock clock) : ICommandHandler<RemoveWaitingPartyCommand>
{
    public async Task HandleAsync(RemoveWaitingPartyCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var party = await WaitingParties.LoadAsync(workspace, parties, command.PartyId, cancellationToken);
        if (!party.IsOpen)
        {
            return;
        }

        party.Leave(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <param name="Position">Rang dans la file (1 = le prochain) ; null une fois sorti.</param>
public sealed record WaitingPartyDto(
    Guid Id, Guid LocationId, string Name, bool HasPhone, string? Phone, int PartySize, string? Note, int? QuotedMinutes, WaitingPartyStatus Status,
    int? Position, DateTimeOffset CreatedAtUtc, DateTimeOffset? NotifiedAtUtc, int NotificationCount, DateTimeOffset? ClosedAtUtc, Guid? TableSessionId);

/// <summary>La file d'un établissement : les groupes en attente, puis ceux sortis dans les 3 dernières heures.</summary>
public sealed record ListWaitingPartiesQuery(Guid LocationId) : IQuery<IReadOnlyList<WaitingPartyDto>>;

public sealed class ListWaitingPartiesHandler(FloorWorkspace workspace, IWaitingPartyRepository parties, IClock clock)
    : IQueryHandler<ListWaitingPartiesQuery, IReadOnlyList<WaitingPartyDto>>
{
    public async Task<IReadOnlyList<WaitingPartyDto>> HandleAsync(ListWaitingPartiesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.LocationId == Guid.Empty)
        {
            return [];
        }

        var list = await parties.ListAsync(workspace.CurrentTenant, LocationId.From(query.LocationId), clock.UtcNow.AddHours(-3), cancellationToken);
        var position = 0;
        return list.Where(party => party.IsOpen).OrderBy(party => party.CreatedAtUtc)
            .Concat(list.Where(party => !party.IsOpen).OrderByDescending(party => party.ClosedAtUtc))
            .Select(party => new WaitingPartyDto(
                party.Id.Value, party.LocationId.Value, party.Name, party.Phone is not null, party.Phone?.Value, party.PartySize, party.Note, party.QuotedMinutes,
                party.Status, party.IsOpen ? ++position : null, party.CreatedAtUtc, party.NotifiedAtUtc, party.NotificationCount, party.ClosedAtUtc,
                party.TableSessionId?.Value))
            .ToList();
    }
}

/// <summary>Pour l'hôte (message « votre table est prête ») : nom, numéro et établissement d'un groupe encore dans la file.</summary>
public sealed record WaitingPartyContactDto(Guid Id, Guid LocationId, string Name, string Phone);

public sealed record GetWaitingPartyContactQuery(Guid PartyId) : IQuery<WaitingPartyContactDto?>;

public sealed class GetWaitingPartyContactHandler(FloorWorkspace workspace, IWaitingPartyRepository parties)
    : IQueryHandler<GetWaitingPartyContactQuery, WaitingPartyContactDto?>
{
    public async Task<WaitingPartyContactDto?> HandleAsync(GetWaitingPartyContactQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var party = query.PartyId == Guid.Empty ? null : await parties.FindAsync(workspace.CurrentTenant, WaitingPartyId.From(query.PartyId), cancellationToken);
        return party is { IsOpen: true, Phone: { } phone } ? new WaitingPartyContactDto(party.Id.Value, party.LocationId.Value, party.Name, phone.Value) : null;
    }
}

internal static class WaitingParties
{
    public static async Task<WaitingParty> LoadAsync(FloorWorkspace workspace, IWaitingPartyRepository parties, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await parties.FindAsync(workspace.CurrentTenant, WaitingPartyId.From(id), cancellationToken))
        ?? throw new NotFoundException(FloorApplicationErrors.WaitingPartyNotFound);
}
