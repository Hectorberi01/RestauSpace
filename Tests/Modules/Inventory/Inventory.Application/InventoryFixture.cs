using Inventory.Application.Abstractions;
using Inventory.Application.Commands;
using Inventory.Application.Queries;
using Inventory.Domain;

namespace Inventory.Application;

/// <summary>
/// Ports en mémoire du module Inventory. Comme les vrais, ils ne répondent jamais que dans le tenant qui leur est donné.
/// Le tenant de « la requête » est <see cref="CurrentTenantId"/>.
/// </summary>
public sealed class InventoryFixture
    : ITenantContext, IInventoryUnitOfWork, IInventoryTenants, IInventoryLocations, IInventoryProducts, IInventoryOrders,
      IIngredientNameUniquenessChecker, IInventoryItemUniquenessChecker, IRecipeUniquenessChecker, IOrderConsumptionLog
{
    public InventoryFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        Writable[TenantA] = true;
        Writable[TenantB] = true;
        CurrentTenantId = TenantA;
        Location = GivenLocation();
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    /// <summary>Un établissement du tenant A.</summary>
    public Guid Location { get; }

    public Dictionary<TenantId, bool> Writable { get; } = [];

    public List<LocationReference> Locations { get; } = [];

    /// <summary>Faux Catalog : produits (et leurs variantes) par tenant.</summary>
    public Dictionary<ProductId, (TenantId Tenant, List<VariantId> Variants)> Products { get; } = [];

    /// <summary>Faux module Orders.</summary>
    public Dictionary<OrderId, (TenantId Tenant, InventoryOrder Order)> Orders { get; } = [];

    public List<Ingredient> Ingredients { get; } = [];

    public List<InventoryItem> Items { get; } = [];

    /// <summary>Le registre : chaque sauvegarde y verse les nouveaux mouvements des articles (comme l'Infrastructure).</summary>
    public List<StockMovement> Ledger { get; } = [];

    public List<Recipe> Recipes { get; } = [];

    public List<Supplier> Suppliers { get; } = [];

    public List<PurchaseOrder> PurchaseOrders { get; } = [];

    public HashSet<(TenantId, OrderId)> Consumed { get; } = [];

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public InventoryWorkspace Workspace =>
        new(this, this, this, new IngredientStore(this), new ItemStore(this), new LedgerStore(this), new RecipeStore(this), this);

    public CreateIngredientHandler CreateIngredient => new(Workspace, this);

    public RenameIngredientHandler RenameIngredient => new(Workspace, this);

    public DeactivateIngredientHandler DeactivateIngredient => new(Workspace);

    public CreateInventoryItemHandler CreateItem => new(Workspace, this);

    public ReceiveStockHandler Receive => new(Workspace, Clock);

    public ConsumeStockHandler Consume => new(Workspace, Clock);

    public RecordWasteHandler Waste => new(Workspace, Clock);

    public CountStockHandler Count => new(Workspace, Clock);

    public TransferStockHandler Transfer => new(Workspace, Clock);

    public ChangeReorderThresholdHandler ChangeThreshold => new(Workspace, Clock);

    public ChangeNegativeStockPolicyHandler ChangePolicy => new(Workspace);

    public CreateRecipeHandler CreateRecipe => new(Workspace, this, this);

    public AddRecipeIngredientHandler AddToRecipe => new(Workspace, Clock);

    public ChangeRecipeQuantityHandler ChangeRecipeQuantity => new(Workspace, Clock);

    public RemoveRecipeIngredientHandler RemoveFromRecipe => new(Workspace, Clock);

    public ConsumeStockForOrderHandler ConsumeOrder => new(Workspace, this, this, Clock);

    public SetUnitCostHandler SetUnitCost => new(Workspace);

    public SaveSupplierHandler SaveSupplier => new(Workspace, new SupplierStore(this), Clock);

    public CreatePurchaseOrderHandler CreatePurchaseOrder => new(Workspace, new SupplierStore(this), new PurchaseOrderStore(this), Clock);

    public SendPurchaseOrderHandler SendPurchaseOrder => new(Workspace, new PurchaseOrderStore(this), Clock);

    public ReceivePurchaseOrderHandler ReceivePurchaseOrder => new(Workspace, new PurchaseOrderStore(this), Clock);

    public GetPurchaseOrderHandler GetPurchaseOrder => new(Workspace, new SupplierStore(this), new PurchaseOrderStore(this));

    public GetReorderSuggestionsHandler ReorderSuggestions => new(Workspace);

    public GetRecipeCostsHandler RecipeCosts => new(Workspace);

    public ListIngredientsHandler ListIngredients => new(Workspace);

    public ListInventoryItemsHandler ListItems => new(Workspace);

    public GetInventoryItemByIdHandler GetItem => new(Workspace);

    public ListStockMovementsHandler ListMovements => new(Workspace);

    public GetRecipeByIdHandler GetRecipe => new(Workspace);

    public ListRecipesHandler ListRecipes => new(Workspace);

    public Guid GivenLocation(TenantId? tenantId = null)
    {
        var location = new LocationReference(tenantId ?? TenantA, LocationId.New(), IsOperational: true);
        Locations.Add(location);
        return location.LocationId.Value;
    }

    /// <summary>Un produit du catalogue du tenant (A par défaut) avec <paramref name="variants"/> variantes.</summary>
    public (Guid Product, IReadOnlyList<Guid> Variants) GivenProduct(int variants = 0, TenantId? tenantId = null)
    {
        var id = ProductId.New();
        var variantIds = Enumerable.Range(0, variants).Select(_ => VariantId.New()).ToList();
        Products[id] = (tenantId ?? TenantA, variantIds);
        return (id.Value, variantIds.Select(variant => variant.Value).ToList());
    }

    /// <summary>Une commande du tenant A (en préparation par défaut) à l'établissement principal.</summary>
    public Guid GivenOrder(IReadOnlyList<(Guid Product, Guid? Variant, int Quantity)> lines, bool started = true, Guid? locationId = null, TenantId? tenantId = null)
    {
        var id = OrderId.New();
        Orders[id] = (
            tenantId ?? TenantA,
            new InventoryOrder(
                id,
                LocationId.From(locationId ?? Location),
                started,
                lines.Select(line => new InventoryOrderLine(
                    ProductId.From(line.Product), line.Variant is { } variant ? VariantId.From(variant) : null, line.Quantity)).ToList()));
        return id.Value;
    }

    public InventoryItem ItemById(Guid id) => Items.Single(item => item.Id.Value == id);

    public Recipe RecipeById(Guid id) => Recipes.Single(recipe => recipe.Id.Value == id);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        // Comme l'Infrastructure : les mouvements ajoutés sont insérés dans le registre, puis ne sont plus en mémoire.
        foreach (var item in Items)
        {
            Ledger.AddRange(item.Movements.Where(movement => !Ledger.Contains(movement)).ToList());
        }

        return Task.CompletedTask;
    }

    Task<bool> IInventoryTenants.AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Writable.TryGetValue(tenantId, out var writable) && writable);

    Task<LocationReference?> IInventoryLocations.FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult<LocationReference?>(Locations.SingleOrDefault(location => location.TenantId == tenantId && location.LocationId == locationId));

    Task<InventoryProduct?> IInventoryProducts.FindAsync(TenantId tenantId, ProductId productId, CancellationToken cancellationToken) =>
        Task.FromResult<InventoryProduct?>(Products.TryGetValue(productId, out var product) && product.Tenant == tenantId
            ? new InventoryProduct(new ProductReference(tenantId, productId), product.Variants)
            : null);

    Task<InventoryOrder?> IInventoryOrders.FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult<InventoryOrder?>(Orders.TryGetValue(orderId, out var order) && order.Tenant == tenantId ? order.Order : null);

    Task<bool> IIngredientNameUniquenessChecker.IsUniqueAsync(
        TenantId tenantId, string name, IngredientId? excludedIngredientId, CancellationToken cancellationToken) =>
        Task.FromResult(!Ingredients.Any(ingredient => ingredient.TenantId == tenantId
            && ingredient.Id != excludedIngredientId
            && string.Equals(ingredient.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)));

    Task<bool> IInventoryItemUniquenessChecker.IsUniqueAsync(
        TenantId tenantId, LocationId locationId, IngredientId ingredientId, CancellationToken cancellationToken) =>
        Task.FromResult(!Items.Any(item => item.TenantId == tenantId && item.LocationId == locationId && item.IngredientId == ingredientId));

    Task<bool> IRecipeUniquenessChecker.IsUniqueAsync(
        TenantId tenantId, ProductId productId, VariantId? variantId, CancellationToken cancellationToken) =>
        Task.FromResult(!Recipes.Any(recipe => recipe.TenantId == tenantId && recipe.ProductId == productId && recipe.VariantId == variantId));

    Task<bool> IOrderConsumptionLog.HasConsumedAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult(Consumed.Contains((tenantId, orderId)));

    void IOrderConsumptionLog.Record(TenantId tenantId, OrderId orderId, LocationId locationId, DateTimeOffset consumedAtUtc) =>
        Consumed.Add((tenantId, orderId));

    private sealed class IngredientStore(InventoryFixture fixture) : IIngredientRepository
    {
        public Task<Ingredient?> FindAsync(TenantId tenantId, IngredientId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Ingredient?>(fixture.Ingredients.SingleOrDefault(ingredient => ingredient.TenantId == tenantId && ingredient.Id == id));

        public Task<IReadOnlyList<Ingredient>> FindManyAsync(TenantId tenantId, IEnumerable<IngredientId> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Ingredient>>(
                fixture.Ingredients.Where(ingredient => ingredient.TenantId == tenantId && ids.Contains(ingredient.Id)).ToList());

        public Task<IReadOnlyList<Ingredient>> ListAsync(TenantId tenantId, bool includeInactive, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Ingredient>>(fixture.Ingredients
                .Where(ingredient => ingredient.TenantId == tenantId && (includeInactive || ingredient.IsActive))
                .OrderBy(ingredient => ingredient.Name, StringComparer.Ordinal)
                .ToList());

        public void Add(Ingredient ingredient) => fixture.Ingredients.Add(ingredient);
    }

    private sealed class ItemStore(InventoryFixture fixture) : IInventoryItemRepository
    {
        public Task<InventoryItem?> FindAsync(TenantId tenantId, InventoryItemId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<InventoryItem?>(fixture.Items.SingleOrDefault(item => item.TenantId == tenantId && item.Id == id));

        public Task<IReadOnlyList<InventoryItem>> FindAtLocationAsync(
            TenantId tenantId, LocationId locationId, IEnumerable<IngredientId> ingredientIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InventoryItem>>(fixture.Items
                .Where(item => item.TenantId == tenantId && item.LocationId == locationId && ingredientIds.Contains(item.IngredientId))
                .ToList());

        public Task<IReadOnlyList<InventoryItem>> ListAsync(
            TenantId tenantId, LocationId? locationId, bool lowStockOnly, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InventoryItem>>(fixture.Items
                .Where(item => item.TenantId == tenantId && (locationId == null || item.LocationId == locationId) && (!lowStockOnly || item.IsLowStock))
                .ToList());

        public void Add(InventoryItem item) => fixture.Items.Add(item);
    }

    private sealed class SupplierStore(InventoryFixture fixture) : ISupplierRepository
    {
        public Task<Supplier?> FindAsync(TenantId tenantId, SupplierId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Supplier?>(fixture.Suppliers.SingleOrDefault(supplier => supplier.TenantId == tenantId && supplier.Id == id));

        public Task<IReadOnlyList<Supplier>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Supplier>>(fixture.Suppliers.Where(supplier => supplier.TenantId == tenantId).ToList());

        public void Add(Supplier supplier) => fixture.Suppliers.Add(supplier);
    }

    private sealed class PurchaseOrderStore(InventoryFixture fixture) : IPurchaseOrderRepository
    {
        public Task<PurchaseOrder?> FindAsync(TenantId tenantId, PurchaseOrderId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PurchaseOrder?>(fixture.PurchaseOrders.SingleOrDefault(order => order.TenantId == tenantId && order.Id == id));

        public Task<IReadOnlyList<PurchaseOrder>> ListAsync(
            TenantId tenantId, LocationId? locationId, PurchaseOrderStatus? status, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PurchaseOrder>>(fixture.PurchaseOrders
                .Where(order => order.TenantId == tenantId && (locationId == null || order.LocationId == locationId) && (status == null || order.Status == status))
                .Take(take)
                .ToList());

        public void Add(PurchaseOrder order) => fixture.PurchaseOrders.Add(order);
    }

    private sealed class LedgerStore(InventoryFixture fixture) : IStockMovementLedger
    {
        public Task<IReadOnlyList<StockMovement>> ListAsync(
            TenantId tenantId, InventoryItemId itemId, int skip, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StockMovement>>(fixture.Ledger
                .Where(movement => movement.TenantId == tenantId && movement.InventoryItemId == itemId)
                .Reverse()
                .Skip(skip)
                .Take(take)
                .ToList());
    }

    private sealed class RecipeStore(InventoryFixture fixture) : IRecipeRepository
    {
        public Task<Recipe?> FindAsync(TenantId tenantId, RecipeId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Recipe?>(fixture.Recipes.SingleOrDefault(recipe => recipe.TenantId == tenantId && recipe.Id == id));

        public Task<IReadOnlyList<Recipe>> ListForProductsAsync(
            TenantId tenantId, IEnumerable<ProductId> productIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Recipe>>(
                fixture.Recipes.Where(recipe => recipe.TenantId == tenantId && productIds.Contains(recipe.ProductId)).ToList());

        public Task<IReadOnlyList<Recipe>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Recipe>>(fixture.Recipes.Where(recipe => recipe.TenantId == tenantId).ToList());

        public void Add(Recipe recipe) => fixture.Recipes.Add(recipe);
    }
}
