namespace Kitchen.Domain;

/// <summary>
/// Indique quel poste prépare quel produit dans un établissement (un routage par établissement). Un produit sans
/// route propre va au poste par défaut : toute ligne de commande est routable, y compris une commande de boissons seules.
/// Les routes gardent l'id du poste quand celui-ci est désactivé plus tard : rien n'est re-routé en silence, et
/// <see cref="KitchenTicket.CreateFromOrder"/> refuse le poste inactif tant que le routage n'est pas corrigé.
/// </summary>
public sealed class StationRouting : AggregateRoot<StationRoutingId>, ITenantOwned
{
    public const int MaxRoutes = 2000;

    // Pas readonly : l'ORM le pose au chargement (stocké en une seule valeur jsonb). Jamais réassigné par le domaine.
    private List<StationRoute> _routes = [];

    // Pas readonly : l'ORM le pose au chargement (jsonb). Seuls les produits qui ne partent pas directement y figurent.
    private List<ProductCourse> _courses = [];

    private StationRouting(StationRoutingId id, TenantId tenantId, LocationId locationId, StationId defaultStationId) : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        DefaultStationId = defaultStationId;
    }

    // Réservé à l'ORM.
    private StationRouting()
    {
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    /// <summary>Poste de tout produit qui n'a pas de route propre.</summary>
    public StationId DefaultStationId { get; private set; }

    public IReadOnlyCollection<StationRoute> Routes => _routes.AsReadOnly();

    public IReadOnlyCollection<ProductCourse> Courses => _courses.AsReadOnly();

    /// <param name="isOnlyRoutingOfLocation">Réponse de <see cref="IStationRoutingUniquenessChecker"/>.</param>
    public static StationRouting Create(
        TenantId tenantId,
        LocationReference location,
        KitchenStationReference defaultStation,
        bool isOnlyRoutingOfLocation,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(defaultStation);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(location.LocationId);
        Guard.NotEmpty(defaultStation.Id);

        var routing = new StationRouting(StationRoutingId.New(), tenantId, location.LocationId, defaultStation.Id);
        routing.EnsureSameTenant(location.TenantId);
        DomainException.ThrowIf(!isOnlyRoutingOfLocation, StationRoutingErrors.LocationAlreadyHasRouting);
        routing.EnsureUsable(defaultStation);
        routing.Raise(new StationRoutingCreated(tenantId, routing.Id, routing.LocationId, defaultStation.Id, clock.UtcNow));
        return routing;
    }

    public void ChangeDefaultStation(KitchenStationReference station, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(station.Id);
        this.EnsureSameTenant(station.TenantId);

        if (station.Id == DefaultStationId)
        {
            return;
        }

        EnsureUsable(station);

        var previous = DefaultStationId;
        DefaultStationId = station.Id;
        Raise(new StationRoutingDefaultStationChanged(TenantId, Id, LocationId, previous, station.Id, clock.UtcNow));
    }

    /// <summary>Ajoute la route du produit ou remplace son poste.</summary>
    public void SetRoute(ProductId productId, KitchenStationReference station, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(productId);
        Guard.NotEmpty(station.Id);
        this.EnsureSameTenant(station.TenantId);

        var index = _routes.FindIndex(r => r.ProductId == productId);
        if (index >= 0 && _routes[index].StationId == station.Id)
        {
            return;
        }

        EnsureUsable(station);

        StationId? previous = null;
        if (index >= 0)
        {
            previous = _routes[index].StationId;
            _routes[index] = new StationRoute(productId, station.Id);
        }
        else
        {
            DomainException.ThrowIf(_routes.Count >= MaxRoutes, StationRoutingErrors.RouteLimitReached);
            _routes.Add(new StationRoute(productId, station.Id));
        }

        Raise(new StationRouteSet(TenantId, Id, LocationId, productId, station.Id, previous, clock.UtcNow));
    }

    /// <summary>Le produit revient au poste par défaut.</summary>
    public void RemoveRoute(ProductId productId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var route = _routes.FirstOrDefault(r => r.ProductId == productId)
            ?? throw new DomainException(StationRoutingErrors.RouteNotFound);

        _routes.Remove(route);
        Raise(new StationRouteRemoved(TenantId, Id, LocationId, productId, route.StationId, clock.UtcNow));
    }

    /// <summary>Le service du produit (entrée, plat, dessert) ; <see cref="KitchenCourse.Direct"/> l'efface.</summary>
    public void SetCourse(ProductId productId, KitchenCourse course, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(productId);
        DomainException.ThrowIf(!Enum.IsDefined(course), KitchenTicketErrors.CourseUnknown);
        if (CourseFor(productId) == course)
        {
            return;
        }

        var others = _courses.Where(c => c.ProductId != productId).ToList();
        if (course != KitchenCourse.Direct)
        {
            DomainException.ThrowIf(others.Count >= MaxRoutes, StationRoutingErrors.RouteLimitReached);
            others.Add(new ProductCourse(productId, course));
        }

        _courses = others;
        Raise(new ProductCourseSet(TenantId, Id, LocationId, productId, course, clock.UtcNow));
    }

    public KitchenCourse CourseFor(ProductId productId) =>
        _courses.FirstOrDefault(c => c.ProductId == productId)?.Course ?? KitchenCourse.Direct;

    /// <summary>Poste qui prépare le produit : sa route s'il en a une, le poste par défaut sinon.</summary>
    public StationId StationFor(ProductId productId) =>
        _routes.FirstOrDefault(r => r.ProductId == productId)?.StationId ?? DefaultStationId;

    /// <summary>Vrai si le poste est celui par défaut ou la cible d'une route : le désactiver bloquerait des tickets.</summary>
    public bool Uses(StationId stationId) =>
        DefaultStationId == stationId || _routes.Any(r => r.StationId == stationId);

    private void EnsureUsable(KitchenStationReference station)
    {
        this.EnsureSameTenant(station.TenantId);
        DomainException.ThrowIf(station.LocationId != LocationId, StationRoutingErrors.StationOtherLocation);
        DomainException.ThrowIf(!station.IsActive, StationRoutingErrors.StationInactive);
    }
}
