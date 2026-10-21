using Kitchen.Application.Abstractions;
using Kitchen.Domain;

namespace Kitchen.Application.Commands;

// Configuration de la cuisine d'un établissement : ses postes, et le routage qui dit quel poste prépare quel produit
// (un poste par défaut pour tout le reste). Les règles sont celles des agrégats KitchenStation et StationRouting.

/// <summary>Crée un poste actif ; nom unique dans l'établissement (sans tenir compte de la casse). Renvoie son id.</summary>
public sealed record CreateStationCommand(Guid LocationId, string? Name) : ICommand<Guid>;

public sealed class CreateStationHandler(KitchenWorkspace workspace, IKitchenStationNameUniquenessChecker uniqueness, IClock clock)
    : ICommandHandler<CreateStationCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateStationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, command.LocationId, cancellationToken);
        var name = StationName.Create(command.Name);

        var station = KitchenStation.Create(
            tenant, location, name, await uniqueness.IsUniqueAsync(tenant, location.LocationId, name, null, cancellationToken), clock);

        workspace.Stations.Add(station);
        await workspace.SaveAsync(cancellationToken);
        return station.Id.Value;
    }
}

public sealed record RenameStationCommand(Guid StationId, string? Name) : ICommand;

public sealed class RenameStationHandler(KitchenWorkspace workspace, IKitchenStationNameUniquenessChecker uniqueness)
    : ICommandHandler<RenameStationCommand>
{
    public async Task HandleAsync(RenameStationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var station = await workspace.LoadStationAsync(tenant, command.StationId, cancellationToken);
        var name = StationName.Create(command.Name);

        station.Rename(name, await uniqueness.IsUniqueAsync(tenant, station.LocationId, name, station.Id, cancellationToken));

        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record ActivateStationCommand(Guid StationId) : ICommand;

public sealed class ActivateStationHandler(KitchenWorkspace workspace, IClock clock) : ICommandHandler<ActivateStationCommand>
{
    public async Task HandleAsync(ActivateStationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var station = await workspace.LoadStationAsync(workspace.CurrentTenant, command.StationId, cancellationToken);
        station.Activate(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Refusé tant que le routage de l'établissement l'utilise (poste par défaut ou cible d'une route).</summary>
public sealed record DeactivateStationCommand(Guid StationId) : ICommand;

public sealed class DeactivateStationHandler(KitchenWorkspace workspace, IClock clock) : ICommandHandler<DeactivateStationCommand>
{
    public async Task HandleAsync(DeactivateStationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var station = await workspace.LoadStationAsync(tenant, command.StationId, cancellationToken);
        var routing = await workspace.Routings.FindByLocationAsync(tenant, station.LocationId, cancellationToken);
        DomainException.ThrowIf(routing?.Uses(station.Id) == true, KitchenApplicationErrors.StationInUse);

        station.Deactivate(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Crée le routage (unique) d'un établissement avec son poste par défaut. Renvoie son id.</summary>
public sealed record CreateStationRoutingCommand(Guid LocationId, Guid DefaultStationId) : ICommand<Guid>;

public sealed class CreateStationRoutingHandler(KitchenWorkspace workspace, IStationRoutingUniquenessChecker uniqueness, IClock clock)
    : ICommandHandler<CreateStationRoutingCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateStationRoutingCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, command.LocationId, cancellationToken);
        var station = await workspace.LoadStationAsync(tenant, command.DefaultStationId, cancellationToken);

        var routing = StationRouting.Create(
            tenant, location, station.ToReference(), await uniqueness.IsUniqueAsync(tenant, location.LocationId, cancellationToken), clock);

        workspace.Routings.Add(routing);
        await workspace.SaveAsync(cancellationToken);
        return routing.Id.Value;
    }
}

public sealed record ChangeDefaultStationCommand(Guid LocationId, Guid StationId) : ICommand;

public sealed class ChangeDefaultStationHandler(KitchenWorkspace workspace, IClock clock) : ICommandHandler<ChangeDefaultStationCommand>
{
    public async Task HandleAsync(ChangeDefaultStationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, command.LocationId, cancellationToken);
        var routing = await workspace.LoadRoutingAsync(tenant, location.LocationId, cancellationToken);
        var station = await workspace.LoadStationAsync(tenant, command.StationId, cancellationToken);

        routing.ChangeDefaultStation(station.ToReference(), clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Le produit (du catalogue du tenant) est préparé à ce poste ; ajoute ou remplace sa route.</summary>
public sealed record SetStationRouteCommand(Guid LocationId, Guid ProductId, Guid StationId) : ICommand;

public sealed class SetStationRouteHandler(KitchenWorkspace workspace, IKitchenProducts products, IClock clock)
    : ICommandHandler<SetStationRouteCommand>
{
    public async Task HandleAsync(SetStationRouteCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, command.LocationId, cancellationToken);
        var routing = await workspace.LoadRoutingAsync(tenant, location.LocationId, cancellationToken);
        var station = await workspace.LoadStationAsync(tenant, command.StationId, cancellationToken);
        var productId = command.ProductId == Guid.Empty ? (ProductId?)null : ProductId.From(command.ProductId);
        if (productId is not { } product || !await products.ExistsAsync(tenant, product, cancellationToken))
        {
            throw new NotFoundException(KitchenApplicationErrors.ProductNotFound);
        }

        routing.SetRoute(product, station.ToReference(), clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Le service du produit (entrée, plat, dessert) ; <see cref="KitchenCourse.Direct"/> le fait partir tout de suite.</summary>
public sealed record SetProductCourseCommand(Guid LocationId, Guid ProductId, KitchenCourse Course) : ICommand;

public sealed class SetProductCourseHandler(KitchenWorkspace workspace, IKitchenProducts products, IClock clock)
    : ICommandHandler<SetProductCourseCommand>
{
    public async Task HandleAsync(SetProductCourseCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, command.LocationId, cancellationToken);
        var routing = await workspace.LoadRoutingAsync(tenant, location.LocationId, cancellationToken);
        var productId = command.ProductId == Guid.Empty ? (ProductId?)null : ProductId.From(command.ProductId);
        if (productId is not { } product || !await products.ExistsAsync(tenant, product, cancellationToken))
        {
            throw new NotFoundException(KitchenApplicationErrors.ProductNotFound);
        }

        routing.SetCourse(product, command.Course, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Le produit revient au poste par défaut.</summary>
public sealed record RemoveStationRouteCommand(Guid LocationId, Guid ProductId) : ICommand;

public sealed class RemoveStationRouteHandler(KitchenWorkspace workspace, IClock clock) : ICommandHandler<RemoveStationRouteCommand>
{
    public async Task HandleAsync(RemoveStationRouteCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, command.LocationId, cancellationToken);
        var routing = await workspace.LoadRoutingAsync(tenant, location.LocationId, cancellationToken);

        routing.RemoveRoute(ProductId.From(command.ProductId), clock);
        await workspace.SaveAsync(cancellationToken);
    }
}
