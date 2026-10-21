using Inventory.Application.Abstractions;
using Inventory.Domain;

namespace Inventory.Application.Commands;

// Le référentiel des ingrédients du restaurant : un nom unique (sans tenir compte de la casse) et une unité de base
// définitive. Désactiver un ingrédient l'écarte des nouveaux articles de stock et des nouvelles recettes.

/// <summary>Crée un ingrédient actif. Renvoie son id.</summary>
public sealed record CreateIngredientCommand(string? Name, UnitOfMeasure BaseUnit) : ICommand<Guid>;

public sealed class CreateIngredientHandler(InventoryWorkspace workspace, IIngredientNameUniquenessChecker uniqueness)
    : ICommandHandler<CreateIngredientCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateIngredientCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var name = command.Name ?? string.Empty;

        var ingredient = Ingredient.Create(
            tenant, name, command.BaseUnit, await uniqueness.IsUniqueAsync(tenant, name.Trim(), null, cancellationToken));

        workspace.Ingredients.Add(ingredient);
        await workspace.SaveAsync(cancellationToken);
        return ingredient.Id.Value;
    }
}

public sealed record RenameIngredientCommand(Guid IngredientId, string? Name) : ICommand;

public sealed class RenameIngredientHandler(InventoryWorkspace workspace, IIngredientNameUniquenessChecker uniqueness)
    : ICommandHandler<RenameIngredientCommand>
{
    public async Task HandleAsync(RenameIngredientCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var ingredient = await workspace.LoadIngredientAsync(tenant, command.IngredientId, cancellationToken);
        var name = command.Name ?? string.Empty;

        ingredient.Rename(name, await uniqueness.IsUniqueAsync(tenant, name.Trim(), ingredient.Id, cancellationToken));

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Définitif : un ingrédient désactivé reste dans l'historique, les articles et recettes existants.</summary>
public sealed record DeactivateIngredientCommand(Guid IngredientId) : ICommand;

public sealed class DeactivateIngredientHandler(InventoryWorkspace workspace) : ICommandHandler<DeactivateIngredientCommand>
{
    public async Task HandleAsync(DeactivateIngredientCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var ingredient = await workspace.LoadIngredientAsync(workspace.CurrentTenant, command.IngredientId, cancellationToken);
        ingredient.Deactivate();
        await workspace.SaveAsync(cancellationToken);
    }
}
