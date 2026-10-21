using Inventory.Application.Abstractions;
using Inventory.Domain;

namespace Inventory.Application.Commands;

// Recettes : ce qu'une unité vendue d'un produit (ou d'une de ses variantes) consomme en ingrédients. Une recette au
// niveau produit, et au plus une par variante ; la recette d'une variante l'emporte à la consommation.

/// <summary>Crée la recette (vide) d'un produit du catalogue, ou d'une de ses variantes. Renvoie son id.</summary>
public sealed record CreateRecipeCommand(Guid ProductId, Guid? VariantId = null) : ICommand<Guid>;

public sealed class CreateRecipeHandler(InventoryWorkspace workspace, IInventoryProducts products, IRecipeUniquenessChecker uniqueness)
    : ICommandHandler<CreateRecipeCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateRecipeCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var product = (command.ProductId == Guid.Empty ? null : await products.FindAsync(tenant, ProductId.From(command.ProductId), cancellationToken))
            ?? throw new NotFoundException(InventoryApplicationErrors.ProductNotFound);
        VariantId? variantId = null;
        if (command.VariantId is { } variant)
        {
            // La variante doit être une de CE produit : une variante d'un autre produit n'est pas trouvée.
            variantId = product.VariantIds.Where(candidate => candidate.Value == variant).Select(candidate => (VariantId?)candidate).FirstOrDefault()
                ?? throw new NotFoundException(InventoryApplicationErrors.VariantNotFound);
        }

        var recipe = Recipe.Create(
            tenant,
            product.Product,
            await uniqueness.IsUniqueAsync(tenant, product.Product.ProductId, variantId, cancellationToken),
            variantId);

        workspace.Recipes.Add(recipe);
        await workspace.SaveAsync(cancellationToken);
        return recipe.Id.Value;
    }
}

/// <summary>Ajoute un ingrédient actif ; quantité par unité vendue, dans l'unité de base de l'ingrédient si omise.</summary>
public sealed record AddRecipeIngredientCommand(Guid RecipeId, Guid IngredientId, QuantityInput? Quantity) : ICommand;

public sealed class AddRecipeIngredientHandler(InventoryWorkspace workspace, IClock clock) : ICommandHandler<AddRecipeIngredientCommand>
{
    public Task HandleAsync(AddRecipeIngredientCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditRecipeAsync(
            command.RecipeId,
            async recipe =>
            {
                var ingredient = await workspace.LoadIngredientAsync(recipe.TenantId, command.IngredientId, cancellationToken);
                recipe.AddIngredient(ingredient.ToReference(), QuantityInput.Require(command.Quantity, ingredient.BaseUnit), clock);
            },
            cancellationToken);
}

/// <summary>Nouvelle quantité d'un ingrédient de la recette ; dans l'unité actuelle de la ligne si omise.</summary>
public sealed record ChangeRecipeQuantityCommand(Guid RecipeId, Guid IngredientId, QuantityInput? Quantity) : ICommand;

public sealed class ChangeRecipeQuantityHandler(InventoryWorkspace workspace, IClock clock) : ICommandHandler<ChangeRecipeQuantityCommand>
{
    public Task HandleAsync(ChangeRecipeQuantityCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditRecipeAsync(
            command.RecipeId,
            recipe =>
            {
                var ingredientId = RecipeLines.IdOf(command.IngredientId);
                var current = recipe.Ingredients.FirstOrDefault(component => component.IngredientId == ingredientId)
                    ?? throw new DomainException(InventoryErrors.RecipeIngredientNotFound);
                recipe.ChangeQuantity(ingredientId, QuantityInput.Require(command.Quantity, current.Quantity.Unit), clock);
                return Task.CompletedTask;
            },
            cancellationToken);
}

public sealed record RemoveRecipeIngredientCommand(Guid RecipeId, Guid IngredientId) : ICommand;

public sealed class RemoveRecipeIngredientHandler(InventoryWorkspace workspace, IClock clock) : ICommandHandler<RemoveRecipeIngredientCommand>
{
    public Task HandleAsync(RemoveRecipeIngredientCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditRecipeAsync(
            command.RecipeId,
            recipe =>
            {
                recipe.RemoveIngredient(RecipeLines.IdOf(command.IngredientId), clock);
                return Task.CompletedTask;
            },
            cancellationToken);
}

internal static class RecipeLines
{
    /// <summary>Un id vide ne désigne aucune ligne : même réponse qu'un ingrédient absent de la recette.</summary>
    public static IngredientId IdOf(Guid ingredientId) =>
        ingredientId == Guid.Empty
            ? throw new DomainException(InventoryErrors.RecipeIngredientNotFound)
            : IngredientId.From(ingredientId);
}
