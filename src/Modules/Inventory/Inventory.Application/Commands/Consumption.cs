using Inventory.Application.Abstractions;
using Inventory.Domain;

namespace Inventory.Application.Commands;

/// <param name="AlreadyConsumed">La commande avait déjà été consommée : rien n'a bougé (événement rejoué).</param>
/// <param name="MovementCount">Mouvements de vente écrits (un par ingrédient consommé).</param>
/// <param name="ProductsWithoutRecipe">Produits vendus sans recette : rien n'a été retiré du stock pour eux.</param>
/// <param name="IngredientsWithoutStockItem">Ingrédients des recettes sans article de stock dans l'établissement de la commande.</param>
public sealed record OrderConsumptionDto(
    Guid OrderId,
    bool AlreadyConsumed,
    int MovementCount,
    IReadOnlyList<Guid> ProductsWithoutRecipe,
    IReadOnlyList<Guid> IngredientsWithoutStockItem);

/// <summary>
/// Retire du stock de l'établissement de la commande ce que ses lignes ont utilisé, d'après les recettes (celle de la
/// variante si elle existe, sinon celle du produit). Une fois par commande : rejouer ne fait rien (idempotent). La
/// préparation a commencé, la vente a donc eu lieu : la consommation n'est jamais refusée, même si elle fait passer le
/// stock sous zéro (événement StockWentNegative), et elle a lieu même pour un restaurant suspendu. Les produits sans
/// recette et les ingrédients sans article de stock sont seulement signalés.
/// </summary>
public sealed record ConsumeStockForOrderCommand(Guid OrderId) : ICommand<OrderConsumptionDto>;

public sealed class ConsumeStockForOrderHandler(
    InventoryWorkspace workspace, IInventoryOrders orders, IOrderConsumptionLog log, IClock clock)
    : ICommandHandler<ConsumeStockForOrderCommand, OrderConsumptionDto>
{
    public async Task<OrderConsumptionDto> HandleAsync(ConsumeStockForOrderCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var order = (command.OrderId == Guid.Empty ? null : await orders.FindAsync(tenant, OrderId.From(command.OrderId), cancellationToken))
            ?? throw new NotFoundException(InventoryApplicationErrors.OrderNotFound);
        if (await log.HasConsumedAsync(tenant, order.OrderId, cancellationToken))
        {
            return new OrderConsumptionDto(order.OrderId.Value, AlreadyConsumed: true, 0, [], []);
        }

        DomainException.ThrowIf(!order.HasStartedPreparation, InventoryApplicationErrors.OrderNotInPreparation);

        var recipes = await workspace.Recipes.ListForProductsAsync(
            tenant, order.Lines.Select(line => line.ProductId).Distinct(), cancellationToken);
        var needs = new Dictionary<IngredientId, List<Quantity>>();
        var withoutRecipe = new List<Guid>();
        foreach (var line in order.Lines.Where(line => line.Quantity > 0))
        {
            // La recette de la variante d'abord, sinon celle du produit.
            var recipe = recipes.FirstOrDefault(candidate => candidate.ProductId == line.ProductId && candidate.VariantId == line.VariantId)
                ?? (line.VariantId is null
                    ? null
                    : recipes.FirstOrDefault(candidate => candidate.ProductId == line.ProductId && candidate.VariantId is null));
            if (recipe is null)
            {
                withoutRecipe.Add(line.ProductId.Value);
                continue;
            }

            if (recipe.Ingredients.Count == 0)
            {
                continue;
            }

            foreach (var component in recipe.ComputeConsumption(line.Quantity))
            {
                if (!needs.TryGetValue(component.IngredientId, out var quantities))
                {
                    quantities = [];
                    needs[component.IngredientId] = quantities;
                }

                quantities.Add(component.Quantity);
            }
        }

        var items = (await workspace.Items.FindAtLocationAsync(tenant, order.LocationId, needs.Keys, cancellationToken))
            .ToDictionary(item => item.IngredientId);
        var withoutItem = new List<Guid>();
        var movements = 0;
        var reference = order.OrderId.Value.ToString();
        // Ordre stable : deux consommations concurrentes verrouillent les articles dans le même ordre.
        foreach (var (ingredientId, quantities) in needs.OrderBy(need => need.Key.Value))
        {
            if (!items.TryGetValue(ingredientId, out var item))
            {
                withoutItem.Add(ingredientId.Value);
                continue;
            }

            // Une seule sortie par ingrédient : les lignes qui l'utilisent sont additionnées dans l'unité de l'article.
            var total = quantities.Sum(quantity => quantity.ConvertTo(item.Unit).Amount);
            item.ConsumeForSale(Quantity.Create(total, item.Unit), reference, clock);
            movements++;
        }

        log.Record(tenant, order.OrderId, order.LocationId, clock.UtcNow);
        await workspace.SaveAsync(cancellationToken);
        return new OrderConsumptionDto(order.OrderId.Value, AlreadyConsumed: false, movements, withoutRecipe.Distinct().ToList(), withoutItem);
    }
}
