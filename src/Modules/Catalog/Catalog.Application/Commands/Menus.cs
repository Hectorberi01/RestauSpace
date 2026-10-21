using Catalog.Application.Abstractions;
using Catalog.Domain;

namespace Catalog.Application.Commands;

// Menus d'un établissement. Le contenu (nom, sections, produits, ordre) ne change qu'en Draft : un menu publié doit
// d'abord être dépublié (règle de l'agrégat). Les positions sont en base zéro.

/// <summary>Crée un menu vide, en Draft, pour un établissement du tenant courant (ouvert ou pas encore). Renvoie son id.</summary>
public sealed record CreateMenuCommand(Guid LocationId, string? Name) : ICommand<Guid>;

public sealed class CreateMenuHandler(CatalogWorkspace workspace, ICatalogLocations locations) : ICommandHandler<CreateMenuCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateMenuCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var name = CatalogName.Create(command.Name);
        var location = (command.LocationId == Guid.Empty
                ? null
                : await locations.FindAsync(tenant, LocationId.From(command.LocationId), cancellationToken))
            ?? throw new NotFoundException(CatalogApplicationErrors.LocationNotFound);

        var menu = Menu.Create(tenant, location, name);

        workspace.Menus.Add(menu);
        await workspace.SaveAsync(cancellationToken);
        return menu.Id.Value;
    }
}

public sealed record RenameMenuCommand(Guid MenuId, string? Name) : ICommand;

public sealed class RenameMenuHandler(CatalogWorkspace workspace) : ICommandHandler<RenameMenuCommand>
{
    public Task HandleAsync(RenameMenuCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditMenuAsync(command.MenuId, menu => menu.Rename(CatalogName.Create(command.Name)), cancellationToken);
}

/// <param name="Start">Heure locale de début, au format HH:mm.</param>
/// <param name="End">Heure locale de fin, au format HH:mm ; avant le début, le créneau passe minuit.</param>
public sealed record MenuSlotInput(DayOfWeek Day, string? Start, string? End);

/// <summary>Les créneaux du menu (midi, soir, happy hour) ; liste vide = proposé à toute heure.</summary>
public sealed record SetMenuScheduleCommand(Guid MenuId, IReadOnlyList<MenuSlotInput>? Slots) : ICommand;

public sealed class SetMenuScheduleHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<SetMenuScheduleCommand>
{
    public Task HandleAsync(SetMenuScheduleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var slots = (command.Slots ?? []).Select(slot => new MenuSlot(slot.Day, Time(slot.Start), Time(slot.End))).ToList();
        return workspace.EditMenuAsync(command.MenuId, menu => menu.SetSchedule(slots, clock), cancellationToken);
    }

    private static TimeOnly Time(string? value) =>
        TimeOnly.TryParseExact(value ?? string.Empty, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time)
            ? time
            : throw new DomainException(MenuErrors.ScheduleInvalid);
}

/// <summary>Ajoute une section vide en fin de menu. Renvoie son id.</summary>
public sealed record AddMenuSectionCommand(Guid MenuId, string? Name) : ICommand<Guid>;

public sealed class AddMenuSectionHandler(CatalogWorkspace workspace) : ICommandHandler<AddMenuSectionCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddMenuSectionCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var menu = await workspace.LoadMenuAsync(workspace.CurrentTenant, command.MenuId, cancellationToken);

        var id = menu.AddSection(CatalogName.Create(command.Name));

        await workspace.SaveAsync(cancellationToken);
        return id.Value;
    }
}

public sealed record RenameMenuSectionCommand(Guid MenuId, Guid SectionId, string? Name) : ICommand;

public sealed class RenameMenuSectionHandler(CatalogWorkspace workspace) : ICommandHandler<RenameMenuSectionCommand>
{
    public Task HandleAsync(RenameMenuSectionCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditMenuAsync(
            command.MenuId,
            menu => menu.RenameSection(MenuSectionId.From(command.SectionId), CatalogName.Create(command.Name)),
            cancellationToken);
}

public sealed record MoveMenuSectionCommand(Guid MenuId, Guid SectionId, int Position) : ICommand;

public sealed class MoveMenuSectionHandler(CatalogWorkspace workspace) : ICommandHandler<MoveMenuSectionCommand>
{
    public Task HandleAsync(MoveMenuSectionCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditMenuAsync(
            command.MenuId, menu => menu.MoveSection(MenuSectionId.From(command.SectionId), command.Position), cancellationToken);
}

public sealed record RemoveMenuSectionCommand(Guid MenuId, Guid SectionId) : ICommand;

public sealed class RemoveMenuSectionHandler(CatalogWorkspace workspace) : ICommandHandler<RemoveMenuSectionCommand>
{
    public Task HandleAsync(RemoveMenuSectionCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditMenuAsync(command.MenuId, menu => menu.RemoveSection(MenuSectionId.From(command.SectionId)), cancellationToken);
}

/// <summary>Ajoute un produit non archivé du tenant courant en fin de section.</summary>
public sealed record AddProductToMenuSectionCommand(Guid MenuId, Guid SectionId, Guid ProductId) : ICommand;

public sealed class AddProductToMenuSectionHandler(CatalogWorkspace workspace) : ICommandHandler<AddProductToMenuSectionCommand>
{
    public async Task HandleAsync(AddProductToMenuSectionCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var menu = await workspace.LoadMenuAsync(tenant, command.MenuId, cancellationToken);
        var product = await workspace.LoadProductAsync(tenant, command.ProductId, cancellationToken);
        DomainException.ThrowIf(!product.IsSoldAt(menu.LocationId), ProductErrors.NotSoldHere);

        menu.AddProductToSection(MenuSectionId.From(command.SectionId), product.ToReference());

        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record MoveProductInMenuSectionCommand(Guid MenuId, Guid SectionId, Guid ProductId, int Position) : ICommand;

public sealed class MoveProductInMenuSectionHandler(CatalogWorkspace workspace) : ICommandHandler<MoveProductInMenuSectionCommand>
{
    public Task HandleAsync(MoveProductInMenuSectionCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditMenuAsync(
            command.MenuId,
            menu => menu.MoveProductInSection(
                MenuSectionId.From(command.SectionId), ProductId.From(command.ProductId), command.Position),
            cancellationToken);
}

public sealed record RemoveProductFromMenuSectionCommand(Guid MenuId, Guid SectionId, Guid ProductId) : ICommand;

public sealed class RemoveProductFromMenuSectionHandler(CatalogWorkspace workspace) : ICommandHandler<RemoveProductFromMenuSectionCommand>
{
    public Task HandleAsync(RemoveProductFromMenuSectionCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditMenuAsync(
            command.MenuId,
            menu => menu.RemoveProductFromSection(MenuSectionId.From(command.SectionId), ProductId.From(command.ProductId)),
            cancellationToken);
}

/// <summary>
/// Draft -> Published. Le menu reçoit l'état ACTUEL de chaque produit listé (un produit archivé depuis son ajout bloque),
/// et chaque groupe d'options attaché à ces produits doit pouvoir être complété par un client (min atteignable avec
/// les options disponibles).
/// </summary>
public sealed record PublishMenuCommand(Guid MenuId) : ICommand;

public sealed class PublishMenuHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<PublishMenuCommand>
{
    public async Task HandleAsync(PublishMenuCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var menu = await workspace.LoadMenuAsync(tenant, command.MenuId, cancellationToken);

        var listed = await workspace.Products.FindManyAsync(
            tenant, menu.Sections.SelectMany(section => section.ProductIds).Distinct().ToList(), cancellationToken);
        var groups = await workspace.ModifierGroups.FindManyAsync(
            tenant,
            listed.Where(product => product.Status != ProductStatus.Archived)
                .SelectMany(product => product.ModifierGroupIds)
                .Distinct()
                .ToList(),
            cancellationToken);

        // Les groupes d'abord : une vérification qui ne change rien, avant la transition du menu.
        foreach (var group in groups)
        {
            group.EnsureSatisfiable();
        }

        menu.Publish(listed.Select(product => product.ToReference()), clock);

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Published -> Draft, pour pouvoir le modifier.</summary>
public sealed record UnpublishMenuCommand(Guid MenuId) : ICommand;

public sealed class UnpublishMenuHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<UnpublishMenuCommand>
{
    public Task HandleAsync(UnpublishMenuCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditMenuAsync(command.MenuId, menu => menu.Unpublish(clock), cancellationToken);
}

public sealed record ArchiveMenuCommand(Guid MenuId) : ICommand;

public sealed class ArchiveMenuHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<ArchiveMenuCommand>
{
    public Task HandleAsync(ArchiveMenuCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditMenuAsync(command.MenuId, menu => menu.Archive(clock), cancellationToken);
}
