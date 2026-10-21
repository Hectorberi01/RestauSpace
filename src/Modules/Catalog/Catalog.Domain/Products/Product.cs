namespace Catalog.Domain;

/// <summary>
/// Produit vendable et configurable. Les commandes copient ce dont elles ont besoin (nom, prix) au moment de la vente
/// et ne dépendent plus ensuite du produit vivant.
/// </summary>
public sealed class Product : AggregateRoot<ProductId>, ITenantOwned
{
    public const int MaxDescriptionLength = 2000;
    public const int MaxVariants = 50;
    public const int MaxModifierGroups = 20;

    private readonly List<ProductVariant> _variants = [];
    // Pas readonly : l'ORM le pose au chargement (stocké en une seule valeur jsonb). Jamais réassigné par le domaine.
    private List<ModifierGroupId> _modifierGroupIds = [];
    // Pas readonly : l'ORM le pose au chargement (stocké en une seule valeur jsonb). Jamais réassigné par le domaine.
    private List<string> _allergens = [];
    // Pas readonly : l'ORM le pose au chargement (stocké en une seule valeur jsonb). Jamais réassigné par le domaine.
    private List<OnlineChannel> _hiddenChannels = [];

    // Pas readonly : l'ORM le pose au chargement (jsonb).
    private List<PriceOverride> _priceOverrides = [];

    // Pas readonly : l'ORM le pose au chargement (jsonb). Vide pour un produit ordinaire.
    private List<FormulaStep> _formulaSteps = [];

    public const int MaxFormulaSteps = 10;

    // Pas readonly : l'ORM le pose au chargement (jsonb). Vide : vendu dans tous les établissements du restaurant.
    private List<LocationId> _locationIds = [];

    // Pas readonly : l'ORM le pose au chargement (jsonb). Établissements où le produit est en rupture.
    private List<LocationId> _unavailableAt = [];

    public const int MaxLocations = 200;

    private Product(
        ProductId id,
        TenantId tenantId,
        CatalogName name,
        string? description,
        Money basePrice,
        TaxCategoryId taxCategoryId) : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Description = description;
        BasePrice = basePrice;
        TaxCategoryId = taxCategoryId;
        Status = ProductStatus.Draft;
    }

    // Réservé à l'ORM.
    private Product()
    {
        Name = null!;
        BasePrice = null!;
    }

    public TenantId TenantId { get; private set; }

    public CatalogName Name { get; private set; }

    public string? Description { get; private set; }

    public ProductStatus Status { get; private set; }

    /// <summary>
    /// Stocké exactement tel que saisi. Son caractère TTC ou HT n'est pas décidé ici : le module Orders l'interprète
    /// (ainsi que les deltas de variante / modificateur) selon le mode de taxation des prix du tenant — TTC par défaut.
    /// </summary>
    public Money BasePrice { get; private set; }

    public TaxCategoryId TaxCategoryId { get; private set; }

    public IReadOnlyCollection<ProductVariant> Variants => _variants.AsReadOnly();

    public IReadOnlyCollection<ModifierGroupId> ModifierGroupIds => _modifierGroupIds.AsReadOnly();

    /// <summary>Codes du référentiel <see cref="AllergenCodes"/>, dans son ordre.</summary>
    public IReadOnlyCollection<string> Allergens => _allergens.AsReadOnly();

    /// <summary>Canaux de vente en ligne où le produit n'apparaît pas (aucun = visible partout).</summary>
    public IReadOnlyCollection<OnlineChannel> HiddenChannels => _hiddenChannels.AsReadOnly();

    /// <summary>Prix de base propres à un établissement, à un canal, ou aux deux.</summary>
    public IReadOnlyCollection<PriceOverride> PriceOverrides => _priceOverrides.AsReadOnly();

    public const int MaxPriceOverrides = 100;

    /// <summary>
    /// Étapes d'une formule (menu composé) : le client choisit un ou plusieurs produits à chacune. Vide pour un produit
    /// ordinaire. Le prix de la formule est son prix de base, plus les suppléments des produits choisis.
    /// </summary>
    public IReadOnlyList<FormulaStep> FormulaSteps => _formulaSteps.AsReadOnly();

    public bool IsFormula => _formulaSteps.Count > 0;

    public Currency Currency => BasePrice.Currency;

    /// <summary>Seul un produit actif peut être mis dans une commande.</summary>
    public bool IsSellable => Status == ProductStatus.Active;

    public static Product Create(
        TenantId tenantId,
        CatalogName name,
        string? description,
        Money basePrice,
        TaxCategoryReference taxCategory,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(basePrice);
        ArgumentNullException.ThrowIfNull(taxCategory);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(taxCategory.Id);
        DomainException.ThrowIf(basePrice.IsNegative, ProductErrors.NegativePrice);

        var product = new Product(ProductId.New(), tenantId, name, NormalizeDescription(description), basePrice, taxCategory.Id);
        product.EnsureSameTenant(taxCategory.TenantId);
        DomainException.ThrowIf(taxCategory.IsArchived, ProductErrors.TaxCategoryArchived);
        product.Raise(new ProductCreated(tenantId, product.Id, name.Value, basePrice, clock.UtcNow));
        return product;
    }

    public void Rename(CatalogName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        EnsureNotArchived();
        Name = name;
    }

    public void ChangeDescription(string? description)
    {
        EnsureNotArchived();
        Description = NormalizeDescription(description);
    }

    /// <summary>Pour les ventes futures uniquement : les lignes de commande gardent la taxe qu'elles ont figée.</summary>
    public void ChangeTaxCategory(TaxCategoryReference taxCategory)
    {
        ArgumentNullException.ThrowIfNull(taxCategory);
        EnsureNotArchived();
        Guard.NotEmpty(taxCategory.Id);
        this.EnsureSameTenant(taxCategory.TenantId);

        if (taxCategory.Id == TaxCategoryId)
        {
            return;
        }

        DomainException.ThrowIf(taxCategory.IsArchived, ProductErrors.TaxCategoryArchived);
        TaxCategoryId = taxCategory.Id;
    }

    public void ChangeBasePrice(Money newBasePrice, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newBasePrice);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        DomainException.ThrowIf(newBasePrice.Currency != Currency, ProductErrors.CurrencyMismatch);
        DomainException.ThrowIf(newBasePrice.IsNegative, ProductErrors.NegativePrice);

        if (newBasePrice == BasePrice)
        {
            return;
        }

        // Un prix de base plus bas ne doit pas faire passer une variante existante (à delta négatif) sous zéro.
        foreach (var variant in _variants)
        {
            EnsureVariantPriceIsValid(newBasePrice, variant.PriceDelta);
        }

        var previous = BasePrice;
        BasePrice = newBasePrice;
        Raise(new ProductPriceChanged(TenantId, Id, previous, newBasePrice, clock.UtcNow));
    }

    /// <summary>Draft -> Active (première mise en ligne) ou Unavailable -> Active (retour en stock).</summary>
    public void Activate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        DomainException.ThrowIf(Status == ProductStatus.Active, ProductErrors.AlreadyActive);

        Status = ProductStatus.Active;
        Raise(new ProductActivated(TenantId, Id, clock.UtcNow));
    }

    /// <summary>Active -> Unavailable (temporairement invendable, par ex. en rupture de stock).</summary>
    public void MarkUnavailable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        DomainException.ThrowIf(Status != ProductStatus.Active, ProductErrors.NotActive);

        Status = ProductStatus.Unavailable;
        Raise(new ProductUnavailable(TenantId, Id, clock.UtcNow));
    }

    /// <summary>Tout état non archivé -> Archived. Terminal : le produit reste dans l'historique mais ne se vend plus.</summary>
    public void Archive(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();

        Status = ProductStatus.Archived;
        Raise(new ProductArchived(TenantId, Id, clock.UtcNow));
    }

    public VariantId AddVariant(CatalogName name, Money priceDelta, Sku? sku = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(priceDelta);
        EnsureNotArchived();
        DomainException.ThrowIf(IsFormula, FormulaErrors.WithVariants);
        DomainException.ThrowIf(_variants.Count >= MaxVariants, ProductErrors.TooManyVariants);
        DomainException.ThrowIf(_variants.Any(v => v.Name.Matches(name)), ProductErrors.VariantNameDuplicate);
        DomainException.ThrowIf(sku is not null && _variants.Any(v => v.Sku == sku), ProductErrors.VariantSkuDuplicate);
        EnsureVariantFitsEveryPrice(priceDelta);

        var variant = ProductVariant.Create(name, priceDelta, sku);
        _variants.Add(variant);
        return variant.Id;
    }

    public void RemoveVariant(VariantId variantId)
    {
        EnsureNotArchived();
        _variants.Remove(GetVariant(variantId));
    }

    public void ChangeVariantPriceDelta(VariantId variantId, Money priceDelta, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(priceDelta);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        var variant = GetVariant(variantId);
        EnsureVariantFitsEveryPrice(priceDelta);

        if (priceDelta == variant.PriceDelta)
        {
            return;
        }

        var previous = variant.PriceDelta;
        variant.ChangePriceDelta(priceDelta);
        Raise(new ProductVariantPriceChanged(TenantId, Id, variantId, previous, priceDelta, BasePrice + priceDelta, clock.UtcNow));
    }

    public void SetVariantAvailability(VariantId variantId, bool isAvailable, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        var variant = GetVariant(variantId);

        if (variant.IsAvailable == isAvailable)
        {
            return;
        }

        variant.SetAvailability(isAvailable);
        Raise(new ProductVariantAvailabilityChanged(TenantId, Id, variantId, isAvailable, clock.UtcNow));
    }

    public void AttachModifierGroup(ModifierGroupReference group)
    {
        ArgumentNullException.ThrowIfNull(group);
        EnsureNotArchived();
        Guard.NotEmpty(group.Id);
        this.EnsureSameTenant(group.TenantId);
        DomainException.ThrowIf(group.Currency != Currency, ProductErrors.ModifierGroupCurrencyMismatch);
        DomainException.ThrowIf(_modifierGroupIds.Contains(group.Id), ProductErrors.ModifierGroupAlreadyAttached);
        DomainException.ThrowIf(_modifierGroupIds.Count >= MaxModifierGroups, ProductErrors.TooManyModifierGroups);

        _modifierGroupIds.Add(group.Id);
    }

    public void DetachModifierGroup(ModifierGroupId modifierGroupId)
    {
        EnsureNotArchived();
        DomainException.ThrowIf(!_modifierGroupIds.Remove(modifierGroupId), ProductErrors.ModifierGroupNotAttached);
    }

    /// <summary>Remplace toute la liste des allergènes (codes du référentiel) ; les doublons sont fusionnés.</summary>
    public void SetAllergens(IEnumerable<string?> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        EnsureNotArchived();

        var normalized = AllergenCodes.Normalize(codes);
        _allergens.Clear();
        _allergens.AddRange(normalized);
    }

    /// <summary>Remplace la liste des canaux en ligne où le produit est masqué ; les doublons sont fusionnés.</summary>
    public void SetHiddenChannels(IEnumerable<OnlineChannel> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        EnsureNotArchived();

        var distinct = channels.Distinct().ToList();
        DomainException.ThrowIf(distinct.Any(channel => !Enum.IsDefined(channel)), ProductErrors.ChannelInvalid);
        _hiddenChannels.Clear();
        _hiddenChannels.AddRange(distinct.Order());
    }

    public bool IsVisibleOn(OnlineChannel channel) => !_hiddenChannels.Contains(channel);

    /// <summary>Établissements où le produit est vendu ; vide : tous les établissements du restaurant.</summary>
    public IReadOnlyCollection<LocationId> LocationIds => _locationIds.AsReadOnly();

    /// <summary>Établissements où le produit est en rupture (le reste du restaurant continue de le vendre).</summary>
    public IReadOnlyCollection<LocationId> UnavailableAt => _unavailableAt.AsReadOnly();

    public bool IsSoldAt(LocationId locationId) => _locationIds.Count == 0 || _locationIds.Contains(locationId);

    /// <summary>Vendable dans cet établissement : actif, vendu ici et pas en rupture ici. Sans établissement : <see cref="IsSellable"/>.</summary>
    public bool IsSellableAt(LocationId? locationId) =>
        IsSellable && (locationId is not { } id || (IsSoldAt(id) && !_unavailableAt.Contains(id)));

    /// <summary>
    /// Les établissements (de ce restaurant, vérifiés par l'Application) où le produit est vendu ; aucun : partout. Une
    /// rupture posée dans un établissement retiré est oubliée.
    /// </summary>
    public void SetLocations(IReadOnlyCollection<LocationReference> locations, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        foreach (var location in locations)
        {
            this.EnsureSameTenant(location.TenantId);
        }

        var ids = locations.Select(location => location.LocationId).Distinct().OrderBy(id => id.Value).ToList();
        DomainException.ThrowIf(ids.Count > MaxLocations, ProductErrors.TooManyLocations);
        if (ids.SequenceEqual(_locationIds.OrderBy(id => id.Value)))
        {
            return;
        }

        _locationIds = ids;
        _unavailableAt = _unavailableAt.Where(IsSoldAt).ToList();
        Raise(new ProductLocationsChanged(TenantId, Id, ids.Select(id => id.Value).ToList(), clock.UtcNow));
    }

    /// <summary>Rupture (ou retour) dans un seul établissement ; le statut du produit pour le restaurant ne change pas.</summary>
    public void SetAvailabilityAt(LocationReference location, bool isAvailable, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        this.EnsureSameTenant(location.TenantId);
        DomainException.ThrowIf(!IsSoldAt(location.LocationId), ProductErrors.NotSoldHere);
        if (isAvailable != _unavailableAt.Contains(location.LocationId))
        {
            return;
        }

        _unavailableAt = isAvailable
            ? _unavailableAt.Where(id => id != location.LocationId).ToList()
            : [.. _unavailableAt, location.LocationId];
        Raise(new ProductAvailabilityAtLocationChanged(TenantId, Id, location.LocationId, isAvailable, clock.UtcNow));
    }

    /// <summary>
    /// Pose le prix de base du produit pour un établissement (de ce restaurant, vérifié par l'Application), un canal, ou
    /// les deux. Les variantes gardent leur écart avec ce prix.
    /// </summary>
    public void SetPriceOverride(LocationReference? location, PriceChannel? channel, Money basePrice, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(basePrice);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        DomainException.ThrowIf(location is null && channel is null, ProductErrors.PriceOverrideTargetRequired);
        DomainException.ThrowIf(channel is { } known && !Enum.IsDefined(known), ProductErrors.PriceChannelInvalid);
        if (location is not null)
        {
            this.EnsureSameTenant(location.TenantId);
        }

        DomainException.ThrowIf(basePrice.Currency != Currency, ProductErrors.CurrencyMismatch);
        DomainException.ThrowIf(basePrice.IsNegative, ProductErrors.NegativePrice);
        foreach (var variant in _variants)
        {
            EnsureVariantPriceIsValid(basePrice, variant.PriceDelta);
        }

        var locationId = location?.LocationId;
        var existing = _priceOverrides.FirstOrDefault(item => item.Targets(locationId, channel));
        if (existing?.BasePrice == basePrice)
        {
            return;
        }

        DomainException.ThrowIf(existing is null && _priceOverrides.Count >= MaxPriceOverrides, ProductErrors.PriceOverrideLimitReached);
        _priceOverrides = [.. _priceOverrides.Where(item => item != existing), new PriceOverride(locationId, channel, basePrice)];
        Raise(new ProductPriceOverrideChanged(TenantId, Id, locationId, channel, basePrice, clock.UtcNow));
    }

    /// <summary>Retire un prix propre : le produit reprend le prix suivant (moins précis, ou de base).</summary>
    public void RemovePriceOverride(LocationId? locationId, PriceChannel? channel, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        var existing = _priceOverrides.FirstOrDefault(item => item.Targets(locationId, channel))
            ?? throw new DomainException(ProductErrors.PriceOverrideNotFound);
        _priceOverrides = _priceOverrides.Where(item => item != existing).ToList();
        Raise(new ProductPriceOverrideChanged(TenantId, Id, locationId, channel, null, clock.UtcNow));
    }

    /// <summary>
    /// Le prix de base qui s'applique : établissement et canal, puis établissement seul, puis canal seul, puis le prix
    /// de base du produit.
    /// </summary>
    public Money BasePriceFor(LocationId? locationId, PriceChannel? channel) =>
        _priceOverrides
            .Where(item => item.AppliesTo(locationId, channel))
            .OrderByDescending(item => item.Rank)
            .FirstOrDefault()?.BasePrice ?? BasePrice;

    /// <summary>Vrai quand le produit est actif et que la variante donnée existe et est disponible.</summary>
    public bool CanBeSold(VariantId variantId) =>
        IsSellable && _variants.Any(v => v.Id == variantId && v.IsAvailable);

    /// <summary>Prix de la variante donnée : prix de base + delta de variante. Un produit archivé n'a plus de prix.</summary>
    public Money PriceOf(VariantId variantId)
    {
        EnsureNotArchived();
        return BasePrice + GetVariant(variantId).PriceDelta;
    }

    /// <summary>
    /// Tout ce qu'une ligne de commande fige pour ce produit, ou rejette la vente. Un produit à variantes se vend via
    /// l'une d'elles ; un produit sans variante se vend à son prix de base (<paramref name="variantId"/> null).
    /// </summary>
    /// <param name="locationId">Établissement de la vente, pour son prix propre ; null = prix commun.</param>
    /// <param name="channel">Canal de la vente, pour son prix propre ; null = prix commun.</param>
    public SellableItem DescribeForSale(VariantId? variantId, LocationId? locationId = null, PriceChannel? channel = null)
    {
        EnsureNotArchived();
        DomainException.ThrowIf(!IsSellable, ProductErrors.NotSellable);
        if (locationId is { } here)
        {
            DomainException.ThrowIf(!IsSoldAt(here), ProductErrors.NotSoldHere);
            DomainException.ThrowIf(_unavailableAt.Contains(here), ProductErrors.UnavailableHere);
        }

        var basePrice = BasePriceFor(locationId, channel);

        if (variantId is null)
        {
            DomainException.ThrowIf(_variants.Count > 0, ProductErrors.VariantRequired);
            return new SellableItem(TenantId, Id, Name.Value, null, null, basePrice, TaxCategoryId);
        }

        var variant = GetVariant(variantId.Value);
        DomainException.ThrowIf(!variant.IsAvailable, ProductErrors.VariantUnavailable);
        return new SellableItem(TenantId, Id, Name.Value, variant.Id, variant.Name.Value, basePrice + variant.PriceDelta, TaxCategoryId);
    }

    /// <summary>
    /// Ce produit (et sa variante s'il en a) proposé dans une formule, avec un supplément. Refusé pour une formule ou un
    /// produit archivé ; un produit à variantes est proposé par l'une d'elles.
    /// </summary>
    public FormulaChoice OfferInFormula(VariantId? variantId, Money supplement)
    {
        ArgumentNullException.ThrowIfNull(supplement);
        DomainException.ThrowIf(Status == ProductStatus.Archived, FormulaErrors.ChoiceArchived);
        DomainException.ThrowIf(IsFormula, FormulaErrors.NestedFormula);
        DomainException.ThrowIf(supplement.IsNegative, FormulaErrors.SupplementInvalid);
        if (variantId is { } variant)
        {
            GetVariant(variant);
        }
        else
        {
            DomainException.ThrowIf(_variants.Count > 0, ProductErrors.VariantRequired);
        }

        return new FormulaChoice(TenantId, Id, variantId, supplement);
    }

    /// <summary>
    /// Fait de ce produit une formule (ou change ses étapes) ; une liste vide en refait un produit ordinaire. Les
    /// commandes passées gardent les produits qu'elles ont figés.
    /// </summary>
    public void SetFormulaSteps(IReadOnlyList<FormulaStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        EnsureNotArchived();
        if (steps.Count == 0)
        {
            _formulaSteps = [];
            return;
        }

        DomainException.ThrowIf(_variants.Count > 0, FormulaErrors.WithVariants);
        DomainException.ThrowIf(steps.Count > MaxFormulaSteps, FormulaErrors.TooManySteps);
        DomainException.ThrowIf(steps.Select(step => step.Name.Value.ToUpperInvariant()).Distinct().Count() != steps.Count, FormulaErrors.StepNameDuplicate);
        foreach (var choice in steps.SelectMany(step => step.Choices))
        {
            this.EnsureSameTenant(choice.TenantId);
            DomainException.ThrowIf(choice.ProductId == Id, FormulaErrors.NestedFormula);
            DomainException.ThrowIf(choice.Supplement.Currency != Currency, FormulaErrors.SupplementInvalid);
        }

        _formulaSteps = [.. steps];
    }

    /// <summary>
    /// Vérifie la composition choisie pour une vente : chaque produit proposé à son étape, et le nombre choisi à chaque
    /// étape dans ses bornes. Renvoie, dans l'ordre donné, l'étape et la proposition de chaque produit choisi.
    /// </summary>
    public IReadOnlyList<(FormulaStep Step, FormulaChoice Choice)> ChooseInFormula(IReadOnlyList<(Guid StepId, ProductId ProductId, VariantId? VariantId)> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        EnsureNotArchived();
        DomainException.ThrowIf(!IsFormula && components.Count > 0, FormulaErrors.NotAFormula);
        var chosen = components
            .Select(component =>
            {
                var step = _formulaSteps.FirstOrDefault(item => item.Id == component.StepId) ?? throw new DomainException(FormulaErrors.StepNotFound);
                return (step, step.Choice(component.ProductId, component.VariantId));
            })
            .ToList();
        foreach (var step in _formulaSteps)
        {
            var count = chosen.Count(item => item.step.Id == step.Id);
            DomainException.ThrowIf(count < step.MinChoices || count > step.MaxChoices, FormulaErrors.SelectionCountInvalid);
        }

        return chosen;
    }

    public ProductReference ToReference() => new(Id, TenantId, Status == ProductStatus.Archived);

    private static string? NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        var trimmed = description.Trim();
        DomainException.ThrowIf(trimmed.Length > MaxDescriptionLength, ProductErrors.DescriptionTooLong);
        return trimmed;
    }

    private static void EnsureVariantPriceIsValid(Money basePrice, Money priceDelta)
    {
        DomainException.ThrowIf(priceDelta.Currency != basePrice.Currency, ProductErrors.VariantCurrencyMismatch);
        DomainException.ThrowIf((basePrice + priceDelta).IsNegative, ProductErrors.VariantPriceNegative);
    }

    private void EnsureVariantFitsEveryPrice(Money priceDelta)
    {
        EnsureVariantPriceIsValid(BasePrice, priceDelta);
        foreach (var item in _priceOverrides)
        {
            EnsureVariantPriceIsValid(item.BasePrice, priceDelta);
        }
    }

    private void EnsureNotArchived() =>
        DomainException.ThrowIf(Status == ProductStatus.Archived, ProductErrors.Archived);

    private ProductVariant GetVariant(VariantId variantId) =>
        _variants.FirstOrDefault(v => v.Id == variantId)
        ?? throw new DomainException(ProductErrors.VariantNotFound);
}
