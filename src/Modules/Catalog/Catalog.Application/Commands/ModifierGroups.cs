using Catalog.Domain;

namespace Catalog.Application.Commands;

// Groupes d'options réutilisables. Les prix sont des montants dans la devise du groupe (celle du tenant à sa création) :
// la requête ne choisit jamais la devise.

/// <summary>Crée un groupe vide dans la devise par défaut du tenant. Renvoie son id.</summary>
public sealed record CreateModifierGroupCommand(string? Name, int MinSelections, int MaxSelections) : ICommand<Guid>;

public sealed class CreateModifierGroupHandler(CatalogWorkspace workspace) : ICommandHandler<CreateModifierGroupCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateModifierGroupCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = await workspace.RequireWritableTenantAsync(cancellationToken);

        var group = ModifierGroup.Create(
            workspace.CurrentTenant,
            CatalogName.Create(command.Name),
            tenant.DefaultCurrency,
            SelectionRule.Create(command.MinSelections, command.MaxSelections));

        workspace.ModifierGroups.Add(group);
        await workspace.SaveAsync(cancellationToken);
        return group.Id.Value;
    }
}

public sealed record RenameModifierGroupCommand(Guid ModifierGroupId, string? Name) : ICommand;

public sealed class RenameModifierGroupHandler(CatalogWorkspace workspace) : ICommandHandler<RenameModifierGroupCommand>
{
    public Task HandleAsync(RenameModifierGroupCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditModifierGroupAsync(
            command.ModifierGroupId, group => group.Rename(CatalogName.Create(command.Name)), cancellationToken);
}

public sealed record ChangeSelectionRuleCommand(Guid ModifierGroupId, int MinSelections, int MaxSelections) : ICommand;

public sealed class ChangeSelectionRuleHandler(CatalogWorkspace workspace) : ICommandHandler<ChangeSelectionRuleCommand>
{
    public Task HandleAsync(ChangeSelectionRuleCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditModifierGroupAsync(
            command.ModifierGroupId,
            group => group.ChangeSelectionRule(SelectionRule.Create(command.MinSelections, command.MaxSelections)),
            cancellationToken);
}

/// <summary>Ajoute une option disponible ; <paramref name="PriceDelta"/> est un supplément (0 = gratuit). Renvoie son id.</summary>
public sealed record AddModifierCommand(Guid ModifierGroupId, string? Name, decimal PriceDelta) : ICommand<Guid>;

public sealed class AddModifierHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<AddModifierCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddModifierCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var group = await workspace.LoadModifierGroupAsync(workspace.CurrentTenant, command.ModifierGroupId, cancellationToken);

        var id = group.AddModifier(CatalogName.Create(command.Name), Money.Of(command.PriceDelta, group.Currency), clock);

        await workspace.SaveAsync(cancellationToken);
        return id.Value;
    }
}

public sealed record RenameModifierCommand(Guid ModifierGroupId, Guid ModifierId, string? Name) : ICommand;

public sealed class RenameModifierHandler(CatalogWorkspace workspace) : ICommandHandler<RenameModifierCommand>
{
    public Task HandleAsync(RenameModifierCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditModifierGroupAsync(
            command.ModifierGroupId,
            group => group.RenameModifier(ModifierId.From(command.ModifierId), CatalogName.Create(command.Name)),
            cancellationToken);
}

public sealed record ChangeModifierPriceCommand(Guid ModifierGroupId, Guid ModifierId, decimal PriceDelta) : ICommand;

public sealed class ChangeModifierPriceHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<ChangeModifierPriceCommand>
{
    public Task HandleAsync(ChangeModifierPriceCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditModifierGroupAsync(
            command.ModifierGroupId,
            group => group.ChangeModifierPrice(
                ModifierId.From(command.ModifierId), Money.Of(command.PriceDelta, group.Currency), clock),
            cancellationToken);
}

/// <summary>Jamais bloqué par la règle min/max : rendre une option indisponible en plein service doit toujours marcher.</summary>
public sealed record SetModifierAvailabilityCommand(Guid ModifierGroupId, Guid ModifierId, bool IsAvailable) : ICommand;

public sealed class SetModifierAvailabilityHandler(CatalogWorkspace workspace, IClock clock)
    : ICommandHandler<SetModifierAvailabilityCommand>
{
    public Task HandleAsync(SetModifierAvailabilityCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditModifierGroupAsync(
            command.ModifierGroupId,
            group => group.SetModifierAvailability(ModifierId.From(command.ModifierId), command.IsAvailable, clock),
            cancellationToken);
}

public sealed record RemoveModifierCommand(Guid ModifierGroupId, Guid ModifierId) : ICommand;

public sealed class RemoveModifierHandler(CatalogWorkspace workspace, IClock clock) : ICommandHandler<RemoveModifierCommand>
{
    public Task HandleAsync(RemoveModifierCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditModifierGroupAsync(
            command.ModifierGroupId, group => group.RemoveModifier(ModifierId.From(command.ModifierId), clock), cancellationToken);
}
