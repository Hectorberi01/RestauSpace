using Catalog.Application.Abstractions;
using Catalog.Application.Commands;
using Catalog.Application.Queries;
using Catalog.Domain;

namespace Catalog.Application;

/// <summary>
/// Ports en mémoire du module Catalog. Comme les vrais repositories, ils ne répondent jamais que dans le tenant qui leur
/// est donné. Le tenant de « la requête » est <see cref="CurrentTenantId"/>.
/// </summary>
public sealed class CatalogFixture : ITenantContext, ICatalogUnitOfWork
{
    public CatalogFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        Tenants[TenantA] = new CatalogTenant(Currency.XOF, AcceptsBusinessWrites: true);
        Tenants[TenantB] = new CatalogTenant(Currency.EUR, AcceptsBusinessWrites: true);
        CurrentTenantId = TenantA;
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    public Dictionary<TenantId, CatalogTenant> Tenants { get; } = [];

    /// <summary>Établissements connus, par tenant.</summary>
    public List<LocationReference> Locations { get; } = [];

    public InMemoryProducts Products { get; } = new();

    public InMemoryModifierGroups ModifierGroups { get; } = new();

    public InMemoryMenus Menus { get; } = new();

    public InMemoryTaxCategories TaxCategories { get; } = new();

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public CatalogWorkspace Workspace => new(
        this, new TenantsPort(Tenants), Products, ModifierGroups, Menus, TaxCategories, this);

    public ICatalogLocations LocationsPort => new LocationsPortImpl(Locations);

    // --- Handlers.

    public CreateTaxCategoryHandler CreateTaxCategory => new(Workspace, TaxCategories, Clock);

    public ArchiveTaxCategoryHandler ArchiveTaxCategory => new(Workspace, Clock);

    public ChangeTaxRateHandler ChangeTaxRate => new(Workspace, Clock);

    public CreateModifierGroupHandler CreateModifierGroup => new(Workspace);

    public AddModifierHandler AddModifier => new(Workspace, Clock);

    public ChangeModifierPriceHandler ChangeModifierPrice => new(Workspace, Clock);

    public SetModifierAvailabilityHandler SetModifierAvailability => new(Workspace, Clock);

    public CreateProductHandler CreateProduct => new(Workspace, Clock);

    public ChangeProductBasePriceHandler ChangeBasePrice => new(Workspace, Clock);

    public ChangeProductTaxCategoryHandler ChangeProductTaxCategory => new(Workspace);

    public ActivateProductHandler ActivateProduct => new(Workspace, Clock);

    public ArchiveProductHandler ArchiveProduct => new(Workspace, Clock);

    public AddProductVariantHandler AddVariant => new(Workspace);

    public AttachModifierGroupHandler AttachModifierGroup => new(Workspace);

    public MarkProductUnavailableHandler MarkUnavailable => new(Workspace, Clock);

    public SetProductAllergensHandler SetAllergens => new(Workspace);

    public SetProductHiddenChannelsHandler SetHiddenChannels => new(Workspace);

    public SetProductPriceOverrideHandler SetPriceOverride => new(Workspace, LocationsPort, Clock);

    public RemoveProductPriceOverrideHandler RemovePriceOverride => new(Workspace, Clock);

    public SetProductLocationsHandler SetLocations => new(Workspace, LocationsPort, Clock);

    public SetProductAvailabilityAtLocationHandler SetAvailabilityAt => new(Workspace, LocationsPort, Clock);

    public GetOrderableCatalogHandler OrderableCatalog => new(Workspace, LocationsPort, Clock);

    public SetMenuScheduleHandler SetMenuSchedule => new(Workspace, Clock);

    public CreateMenuHandler CreateMenu => new(Workspace, LocationsPort);

    public AddMenuSectionHandler AddSection => new(Workspace);

    public AddProductToMenuSectionHandler AddProductToSection => new(Workspace);

    public MoveMenuSectionHandler MoveSection => new(Workspace);

    public PublishMenuHandler PublishMenu => new(Workspace, Clock);

    public RenameMenuHandler RenameMenu => new(Workspace);

    public GetProductByIdHandler GetProduct => new(Workspace);

    public ListProductsHandler ListProducts => new(Workspace);

    public GetMenuByIdHandler GetMenu => new(Workspace);

    public ListMenusHandler ListMenus => new(Workspace);

    public ListTaxCategoriesHandler ListTaxCategories => new(Workspace);

    public GetModifierGroupByIdHandler GetModifierGroup => new(Workspace);

    public HasPricedCatalogHandler HasPricedCatalog => new(new PricedLookup(Products, ModifierGroups));

    // --- Données de départ (créées dans le tenant donné, A par défaut ; le tenant courant est restauré).

    public Task<Guid> GivenTaxCategoryAsync(TenantId? tenantId = null, string code = "TVA_18", decimal rate = 0.18m) =>
        As(tenantId, () => CreateTaxCategory.HandleAsync(new CreateTaxCategoryCommand(code, "TVA 18 %", rate)));

    public async Task<Guid> GivenProductAsync(TenantId? tenantId = null, string name = "Poulet DG", decimal basePrice = 3500m)
    {
        var taxCategory = await GivenTaxCategoryAsync(tenantId, code: $"T{Guid.NewGuid():N}"[..10].ToUpperInvariant());
        return await As(tenantId, () => CreateProduct.HandleAsync(new CreateProductCommand(name, null, basePrice, taxCategory)));
    }

    public Task<Guid> GivenModifierGroupAsync(TenantId? tenantId = null, int min = 0, int max = 1) =>
        As(tenantId, () => CreateModifierGroup.HandleAsync(new CreateModifierGroupCommand("Accompagnement", min, max)));

    public Guid GivenLocation(TenantId? tenantId = null)
    {
        var location = new LocationReference(tenantId ?? TenantA, LocationId.New(), IsOperational: true);
        Locations.Add(location);
        return location.LocationId.Value;
    }

    public async Task<Guid> GivenMenuAsync(TenantId? tenantId = null) =>
        await As(tenantId, () => CreateMenu.HandleAsync(new CreateMenuCommand(GivenLocation(tenantId), "Carte du soir")));

    private async Task<T> As<T>(TenantId? tenantId, Func<Task<T>> action)
    {
        var previous = CurrentTenantId;
        CurrentTenantId = tenantId ?? TenantA;
        try
        {
            return await action();
        }
        finally
        {
            CurrentTenantId = previous;
            SaveCount = 0;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    // --- Ports en mémoire.

    public sealed class InMemoryProducts : IProductRepository
    {
        public List<Product> Items { get; } = [];

        public Product Stored(Guid id) => Items.Single(product => product.Id.Value == id);

        public Task<Product?> FindAsync(TenantId tenantId, ProductId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Product?>(Items.SingleOrDefault(product => product.TenantId == tenantId && product.Id == id));

        public Task<IReadOnlyList<Product>> FindManyAsync(
            TenantId tenantId, IEnumerable<ProductId> ids, CancellationToken cancellationToken = default)
        {
            var wanted = ids.ToHashSet();
            return Task.FromResult<IReadOnlyList<Product>>(
                Items.Where(product => product.TenantId == tenantId && wanted.Contains(product.Id)).ToList());
        }

        public Task<ProductPage> ListAsync(
            TenantId tenantId, ProductStatus? status, int skip, int take, CancellationToken cancellationToken = default)
        {
            var matching = Items
                .Where(product => product.TenantId == tenantId)
                .Where(product => status is null ? product.Status != ProductStatus.Archived : product.Status == status)
                .OrderBy(product => product.Name.Value, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult(new ProductPage(matching.Skip(skip).Take(take).ToList(), matching.Count));
        }

        public void Add(Product product) => Items.Add(product);
    }

    public sealed class InMemoryModifierGroups : IModifierGroupRepository
    {
        public List<ModifierGroup> Items { get; } = [];

        public ModifierGroup Stored(Guid id) => Items.Single(group => group.Id.Value == id);

        public Task<ModifierGroup?> FindAsync(TenantId tenantId, ModifierGroupId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<ModifierGroup?>(Items.SingleOrDefault(group => group.TenantId == tenantId && group.Id == id));

        public Task<IReadOnlyList<ModifierGroup>> FindManyAsync(
            TenantId tenantId, IEnumerable<ModifierGroupId> ids, CancellationToken cancellationToken = default)
        {
            var wanted = ids.ToHashSet();
            return Task.FromResult<IReadOnlyList<ModifierGroup>>(
                Items.Where(group => group.TenantId == tenantId && wanted.Contains(group.Id)).ToList());
        }

        public Task<IReadOnlyList<ModifierGroup>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ModifierGroup>>(
                Items.Where(group => group.TenantId == tenantId).OrderBy(group => group.Name.Value, StringComparer.Ordinal).ToList());

        public void Add(ModifierGroup group) => Items.Add(group);
    }

    public sealed class InMemoryMenus : IMenuRepository
    {
        public List<Menu> Items { get; } = [];

        public Menu Stored(Guid id) => Items.Single(menu => menu.Id.Value == id);

        public Task<Menu?> FindAsync(TenantId tenantId, MenuId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Menu?>(Items.SingleOrDefault(menu => menu.TenantId == tenantId && menu.Id == id));

        public Task<IReadOnlyList<Menu>> ListAsync(TenantId tenantId, LocationId? locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Menu>>(Items
                .Where(menu => menu.TenantId == tenantId && (locationId == null || menu.LocationId == locationId))
                .OrderBy(menu => menu.Name.Value, StringComparer.Ordinal)
                .ToList());

        public void Add(Menu menu) => Items.Add(menu);
    }

    public sealed class InMemoryTaxCategories : ITaxCategoryRepository, ITaxCategoryCodeUniquenessChecker
    {
        public List<TaxCategory> Items { get; } = [];

        public TaxCategory Stored(Guid id) => Items.Single(category => category.Id.Value == id);

        public Task<TaxCategory?> FindAsync(TenantId tenantId, TaxCategoryId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<TaxCategory?>(Items.SingleOrDefault(category => category.TenantId == tenantId && category.Id == id));

        public Task<IReadOnlyList<TaxCategory>> ListAsync(TenantId tenantId, bool includeArchived, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TaxCategory>>(Items
                .Where(category => category.TenantId == tenantId && (includeArchived || !category.IsArchived))
                .OrderBy(category => category.Code.Value, StringComparer.Ordinal)
                .ToList());

        public void Add(TaxCategory category) => Items.Add(category);

        public Task<bool> IsUniqueAsync(TenantId tenantId, TaxCategoryCode code, CancellationToken cancellationToken = default) =>
            Task.FromResult(!Items.Any(category => category.TenantId == tenantId && category.Code == code));
    }

    private sealed class TenantsPort(Dictionary<TenantId, CatalogTenant> tenants) : ICatalogTenants
    {
        public Task<CatalogTenant?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<CatalogTenant?>(tenants.TryGetValue(tenantId, out var tenant) ? tenant : null);
    }

    private sealed class LocationsPortImpl(List<LocationReference> locations) : ICatalogLocations
    {
        public Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<LocationReference?>(
                locations.SingleOrDefault(location => location.TenantId == tenantId && location.LocationId == locationId));
    }

    private sealed class PricedLookup(InMemoryProducts products, InMemoryModifierGroups groups) : IPricedCatalogLookup
    {
        public Task<bool> HasPricedCatalogAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                products.Items.Any(product => product.TenantId == tenantId) || groups.Items.Any(group => group.TenantId == tenantId));
    }
}
