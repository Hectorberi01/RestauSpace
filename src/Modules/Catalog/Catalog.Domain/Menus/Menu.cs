namespace Catalog.Domain;

/// <summary>
/// Composition du catalogue proposé par un établissement. Son contenu (nom, sections, produits) n'est modifiable qu'en
/// Draft : un menu publié doit d'abord être dépublié, pour que ce que voient les clients ne change jamais en silence.
/// </summary>
public sealed class Menu : AggregateRoot<MenuId>, ITenantOwned
{
    public const int MaxSections = 50;
    public const int MaxProductsPerSection = 200;
    public const int MaxSlots = 28;

    private readonly List<MenuSection> _sections = [];

    // Pas readonly : l'ORM le pose au chargement (jsonb).
    private List<MenuSlot> _slots = [];

    private Menu(MenuId id, TenantId tenantId, LocationId locationId, CatalogName name) : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        Name = name;
        Status = MenuStatus.Draft;
    }

    // Réservé à l'ORM.
    private Menu() => Name = null!;

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public CatalogName Name { get; private set; }

    public MenuStatus Status { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    /// <summary>Sections dans l'ordre d'affichage.</summary>
    public IReadOnlyCollection<MenuSection> Sections => _sections.AsReadOnly();

    /// <summary>Créneaux où le menu est proposé (midi, soir, happy hour) ; aucun = à toute heure.</summary>
    public IReadOnlyCollection<MenuSlot> Slots => _slots.AsReadOnly();

    /// <summary>
    /// Partie structurelle du « contenu valide » : au moins une section et aucune section vide. <see cref="Publish"/>
    /// vérifie aussi l'état actuel des produits listés.
    /// </summary>
    public bool CanBePublished => Status == MenuStatus.Draft && _sections.Count > 0 && _sections.All(s => !s.IsEmpty);

    public static Menu Create(TenantId tenantId, LocationReference location, CatalogName name)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(name);
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(location.LocationId);

        var menu = new Menu(MenuId.New(), tenantId, location.LocationId, name);
        menu.EnsureSameTenant(location.TenantId);
        return menu;
    }

    public void Rename(CatalogName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        EnsureEditable();
        Name = name;
    }

    public MenuSectionId AddSection(CatalogName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        EnsureEditable();
        DomainException.ThrowIf(_sections.Count >= MaxSections, MenuErrors.TooManySections);
        DomainException.ThrowIf(_sections.Any(s => s.Name.Matches(name)), MenuErrors.SectionNameDuplicate);

        var section = MenuSection.Create(name);
        _sections.Add(section);
        return section.Id;
    }

    public void RenameSection(MenuSectionId sectionId, CatalogName name)
    {
        ArgumentNullException.ThrowIfNull(name);
        EnsureEditable();
        var section = GetSection(sectionId);
        DomainException.ThrowIf(
            _sections.Any(s => s.Id != sectionId && s.Name.Matches(name)),
            MenuErrors.SectionNameDuplicate);

        section.Rename(name);
    }

    public void RemoveSection(MenuSectionId sectionId)
    {
        EnsureEditable();
        _sections.Remove(GetSection(sectionId));
    }

    /// <summary>Déplace une section à une position (base zéro) dans l'ordre d'affichage.</summary>
    public void MoveSection(MenuSectionId sectionId, int newPosition)
    {
        EnsureEditable();
        var section = GetSection(sectionId);
        DomainException.ThrowIf(newPosition < 0 || newPosition >= _sections.Count, MenuErrors.PositionOutOfRange);

        _sections.Remove(section);
        _sections.Insert(newPosition, section);
    }

    /// <summary>Ajoute un produit en fin de section. Un produit peut figurer dans plusieurs sections, une fois chacune.</summary>
    public void AddProductToSection(MenuSectionId sectionId, ProductReference product)
    {
        ArgumentNullException.ThrowIfNull(product);
        EnsureEditable();
        var section = GetSection(sectionId);
        Guard.NotEmpty(product.Id);
        this.EnsureSameTenant(product.TenantId);
        DomainException.ThrowIf(product.IsArchived, MenuErrors.ProductArchived);

        section.AddProduct(product.Id);
    }

    public void RemoveProductFromSection(MenuSectionId sectionId, ProductId productId)
    {
        EnsureEditable();
        GetSection(sectionId).RemoveProduct(productId);
    }

    /// <summary>Déplace un produit à une position (base zéro) dans sa section.</summary>
    public void MoveProductInSection(MenuSectionId sectionId, ProductId productId, int newPosition)
    {
        EnsureEditable();
        GetSection(sectionId).MoveProduct(productId, newPosition);
    }

    /// <summary>
    /// Draft -> Published. <paramref name="listedProducts"/> sont les références actuelles des produits que liste le
    /// menu (chargées par l'Application) : un produit archivé depuis son ajout bloque la publication.
    /// Les références de produits que le menu ne liste pas sont ignorées.
    /// </summary>
    public void Publish(IEnumerable<ProductReference> listedProducts, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(listedProducts);
        ArgumentNullException.ThrowIfNull(clock);
        var referencesById = new Dictionary<ProductId, ProductReference>();
        foreach (var reference in listedProducts)
        {
            ArgumentNullException.ThrowIfNull(reference, nameof(listedProducts));
            referencesById[reference.Id] = reference;
        }

        EnsureNotArchived();
        DomainException.ThrowIf(Status != MenuStatus.Draft, MenuErrors.NotDraft);
        DomainException.ThrowIf(_sections.Count == 0, MenuErrors.NoSection);
        DomainException.ThrowIf(_sections.Any(s => s.IsEmpty), MenuErrors.EmptySection);

        foreach (var productId in _sections.SelectMany(s => s.ProductIds).Distinct())
        {
            DomainException.ThrowIf(!referencesById.TryGetValue(productId, out var reference), MenuErrors.ProductReferenceMissing);
            this.EnsureSameTenant(reference!.TenantId);
            DomainException.ThrowIf(reference.IsArchived, MenuErrors.ProductArchived);
        }

        var now = clock.UtcNow;
        Status = MenuStatus.Published;
        PublishedAtUtc = now;
        Raise(new MenuPublished(TenantId, Id, LocationId, now));
    }

    /// <summary>
    /// Remplace les créneaux du menu. Permis même publié : le contenu ne change pas, seulement les heures où il est
    /// proposé. Les doublons sont fusionnés.
    /// </summary>
    public void SetSchedule(IEnumerable<MenuSlot> slots, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        var distinct = slots.Distinct().OrderBy(slot => slot.Day).ThenBy(slot => slot.Start).ToList();
        DomainException.ThrowIf(distinct.Count > MaxSlots, MenuErrors.ScheduleTooLong);
        if (distinct.SequenceEqual(_slots))
        {
            return;
        }

        _slots = distinct;
        Raise(new MenuScheduleChanged(TenantId, Id, LocationId, distinct.Count, clock.UtcNow));
    }

    /// <summary>Le menu est-il proposé à cette heure locale de l'établissement ? Toujours, sans créneau.</summary>
    public bool IsOfferedAt(DateTime localTime) => _slots.Count == 0 || _slots.Any(slot => slot.Covers(localTime));

    /// <summary>Published -> Draft.</summary>
    public void Unpublish(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        DomainException.ThrowIf(Status != MenuStatus.Published, MenuErrors.NotPublished);

        Status = MenuStatus.Draft;
        PublishedAtUtc = null;
        Raise(new MenuUnpublished(TenantId, Id, LocationId, clock.UtcNow));
    }

    /// <summary>Draft ou Published -> Archived (terminal).</summary>
    public void Archive(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();

        Status = MenuStatus.Archived;
        PublishedAtUtc = null;
        Raise(new MenuArchived(TenantId, Id, LocationId, clock.UtcNow));
    }

    private void EnsureNotArchived() =>
        DomainException.ThrowIf(Status == MenuStatus.Archived, MenuErrors.Archived);

    private void EnsureEditable()
    {
        EnsureNotArchived();
        DomainException.ThrowIf(Status == MenuStatus.Published, MenuErrors.PublishedIsReadOnly);
    }

    private MenuSection GetSection(MenuSectionId sectionId) =>
        _sections.FirstOrDefault(s => s.Id == sectionId)
        ?? throw new DomainException(MenuErrors.SectionNotFound);
}
