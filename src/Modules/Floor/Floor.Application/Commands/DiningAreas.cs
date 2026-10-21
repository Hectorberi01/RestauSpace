using Floor.Application.Abstractions;
using Floor.Domain;

namespace Floor.Application.Commands;

// Plan de salle : zones d'un établissement et leurs tables. Les règles (libellé unique dans la zone, capacité > 0,
// transitions de service) sont celles de l'agrégat DiningArea.

/// <summary>Crée une zone vide pour un établissement du tenant courant (ouvert ou pas encore). Renvoie son id.</summary>
public sealed record CreateDiningAreaCommand(Guid LocationId, string? Name) : ICommand<Guid>;

public sealed class CreateDiningAreaHandler(FloorWorkspace workspace, IFloorLocations locations, IClock clock)
    : ICommandHandler<CreateDiningAreaCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateDiningAreaCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = (command.LocationId == Guid.Empty
                ? null
                : await locations.FindAsync(tenant, LocationId.From(command.LocationId), cancellationToken))
            ?? throw new NotFoundException(FloorApplicationErrors.LocationNotFound);

        var area = DiningArea.Create(tenant, location, command.Name, clock);

        workspace.DiningAreas.Add(area);
        await workspace.SaveAsync(cancellationToken);
        return area.Id.Value;
    }
}

public sealed record RenameDiningAreaCommand(Guid DiningAreaId, string? Name) : ICommand;

public sealed class RenameDiningAreaHandler(FloorWorkspace workspace, IClock clock) : ICommandHandler<RenameDiningAreaCommand>
{
    public Task HandleAsync(RenameDiningAreaCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditDiningAreaAsync(command.DiningAreaId, area => area.Rename(command.Name, clock), cancellationToken);
}

/// <summary>Ajoute une table disponible à la zone. Renvoie son id.</summary>
public sealed record AddTableCommand(Guid DiningAreaId, string? Label, int Capacity) : ICommand<Guid>;

public sealed class AddTableHandler(FloorWorkspace workspace, IClock clock) : ICommandHandler<AddTableCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddTableCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var area = await workspace.LoadDiningAreaAsync(workspace.CurrentTenant, command.DiningAreaId, cancellationToken);

        var tableId = area.AddTable(command.Label, command.Capacity, clock);

        await workspace.SaveAsync(cancellationToken);
        return tableId.Value;
    }
}

public sealed record ChangeTableCapacityCommand(Guid DiningAreaId, Guid TableId, int Capacity) : ICommand;

public sealed class ChangeTableCapacityHandler(FloorWorkspace workspace, IClock clock) : ICommandHandler<ChangeTableCapacityCommand>
{
    public Task HandleAsync(ChangeTableCapacityCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditDiningAreaAsync(
            command.DiningAreaId,
            area => area.ChangeTableCapacity(TableId.From(command.TableId), command.Capacity, clock),
            cancellationToken);
}

/// <summary>Autorisé même si la table est occupée : seule l'ouverture d'une nouvelle session est ensuite bloquée.</summary>
public sealed record SetTableOutOfServiceCommand(Guid DiningAreaId, Guid TableId) : ICommand;

public sealed class SetTableOutOfServiceHandler(FloorWorkspace workspace, IClock clock) : ICommandHandler<SetTableOutOfServiceCommand>
{
    public Task HandleAsync(SetTableOutOfServiceCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditDiningAreaAsync(
            command.DiningAreaId, area => area.SetTableOutOfService(TableId.From(command.TableId), clock), cancellationToken);
}

public sealed record RestoreTableCommand(Guid DiningAreaId, Guid TableId) : ICommand;

public sealed class RestoreTableHandler(FloorWorkspace workspace, IClock clock) : ICommandHandler<RestoreTableCommand>
{
    public Task HandleAsync(RestoreTableCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditDiningAreaAsync(
            command.DiningAreaId, area => area.RestoreTable(TableId.From(command.TableId), clock), cancellationToken);
}

/// <summary>Refusé tant qu'une session est ouverte sur la table.</summary>
public sealed record RemoveTableCommand(Guid DiningAreaId, Guid TableId) : ICommand;

public sealed class RemoveTableHandler(FloorWorkspace workspace, ITableOccupancyChecker occupancy, IClock clock)
    : ICommandHandler<RemoveTableCommand>
{
    public async Task HandleAsync(RemoveTableCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var area = await workspace.LoadDiningAreaAsync(tenant, command.DiningAreaId, cancellationToken);
        var tableId = TableId.From(command.TableId);

        area.RemoveTable(tableId, await occupancy.HasOpenSessionAsync(tenant, tableId, cancellationToken), clock);

        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record RenameTableCommand(Guid DiningAreaId, Guid TableId, string? Label) : ICommand;

public sealed class RenameTableHandler(FloorWorkspace workspace, IClock clock) : ICommandHandler<RenameTableCommand>
{
    public Task HandleAsync(RenameTableCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditDiningAreaAsync(
            command.DiningAreaId, area => area.RenameTable(TableId.From(command.TableId), command.Label, clock), cancellationToken);
}

public sealed record PlaceTableCommand(Guid DiningAreaId, Guid TableId, int X, int Y, int Width, int Height, TableShape Shape, int Rotation = 0) : ICommand;

public sealed class PlaceTableHandler(FloorWorkspace workspace, IClock clock) : ICommandHandler<PlaceTableCommand>
{
    public Task HandleAsync(PlaceTableCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditDiningAreaAsync(
            command.DiningAreaId,
            area => area.PlaceTable(
                TableId.From(command.TableId), TableLayout.Create(command.X, command.Y, command.Width, command.Height, command.Shape, command.Rotation),
                clock),
            cancellationToken);
}

/// <summary>Supprime la salle et ses tables ; refusé tant qu'une de ses tables est occupée.</summary>
public sealed record DeleteDiningAreaCommand(Guid DiningAreaId) : ICommand;

public sealed class DeleteDiningAreaHandler(FloorWorkspace workspace, ITableOccupancyChecker occupancy, IClock clock)
    : ICommandHandler<DeleteDiningAreaCommand>
{
    public async Task HandleAsync(DeleteDiningAreaCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var area = await workspace.LoadDiningAreaAsync(tenant, command.DiningAreaId, cancellationToken);
        var occupied = new List<TableId>();
        foreach (var table in area.Tables)
        {
            if (await occupancy.HasOpenSessionAsync(tenant, table.Id, cancellationToken))
            {
                occupied.Add(table.Id);
            }
        }

        area.Delete(occupied, clock);
        workspace.DiningAreas.Remove(area);
        await workspace.SaveAsync(cancellationToken);
    }
}
