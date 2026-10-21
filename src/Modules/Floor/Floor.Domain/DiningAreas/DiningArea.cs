namespace Floor.Domain;

/// <summary>Salle/zone d'un établissement et ses tables. Les libellés de table sont uniques dans la zone.</summary>
public sealed class DiningArea : AggregateRoot<DiningAreaId>, ITenantOwned
{
    public const int NameMaxLength = 80;

    private readonly List<RestaurantTable> _tables = [];

    private DiningArea(DiningAreaId id, TenantId tenantId, LocationId locationId, string name)
        : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        Name = name;
    }

    // Réservé à l'ORM.
    private DiningArea()
    {
        Name = null!;
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public string Name { get; private set; }

    public IReadOnlyCollection<RestaurantTable> Tables => _tables.AsReadOnly();

    /// <summary>Nombre total de places des tables actuellement en service.</summary>
    public int SeatsInService => _tables.Where(t => t.IsInService).Sum(t => t.Capacity);

    /// <param name="tenantId">Tenant pour lequel la zone est créée ; doit posséder <paramref name="location"/>.</param>
    /// <param name="location">Établissement de la zone. Pas forcément opérationnel : le plan se prépare en amont.</param>
    public static DiningArea Create(TenantId tenantId, LocationReference location, string? name, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);

        var area = new DiningArea(DiningAreaId.New(), tenantId, location.LocationId, NormalizeName(name));
        area.EnsureSameTenant(location.TenantId);
        area.Raise(new DiningAreaCreated(tenantId, area.Id, area.LocationId, area.Name, clock.UtcNow));
        return area;
    }

    public void Rename(string? name, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var newName = NormalizeName(name);
        if (newName == Name)
        {
            return;
        }

        var previous = Name;
        Name = newName;
        Raise(new DiningAreaRenamed(TenantId, Id, previous, newName, clock.UtcNow));
    }

    public TableId AddTable(string? label, int capacity, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var tableLabel = TableLabel.Create(label);
        DomainException.ThrowIf(_tables.Exists(t => t.Label == tableLabel), DiningAreaErrors.TableLabelAlreadyUsed);

        var table = RestaurantTable.Create(tableLabel, capacity);
        _tables.Add(table);
        Raise(new TableAdded(TenantId, Id, LocationId, table.Id, tableLabel.Value, table.Capacity, clock.UtcNow));
        return table.Id;
    }

    /// <param name="hasOpenSession">Réponse de <see cref="ITableOccupancyChecker"/> pour cette table, lue par l'Application.</param>
    public void RemoveTable(TableId tableId, bool hasOpenSession, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var table = GetTable(tableId);
        DomainException.ThrowIf(hasOpenSession, DiningAreaErrors.TableHasOpenSession);

        _tables.Remove(table);
        Raise(new TableRemoved(TenantId, Id, LocationId, tableId, clock.UtcNow));
    }

    /// <summary>Nouveau libellé, unique dans la salle (sans tenir compte de la casse).</summary>
    public void RenameTable(TableId tableId, string? label, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var table = GetTable(tableId);
        var tableLabel = TableLabel.Create(label);
        if (tableLabel.Value == table.Label.Value)
        {
            return;
        }

        DomainException.ThrowIf(_tables.Exists(t => t.Id != tableId && t.Label == tableLabel), DiningAreaErrors.TableLabelAlreadyUsed);
        var previous = table.Label.Value;
        table.Rename(tableLabel);
        Raise(new TableRenamed(TenantId, Id, LocationId, tableId, previous, tableLabel.Value, clock.UtcNow));
    }

    /// <summary>Pose (ou déplace) la table sur le plan dessiné.</summary>
    public void PlaceTable(TableId tableId, TableLayout layout, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(clock);

        var table = GetTable(tableId);
        if (layout.Equals(table.Layout))
        {
            return;
        }

        table.Place(layout);
        Raise(new TablePlaced(TenantId, Id, tableId, layout.X, layout.Y, layout.Width, layout.Height, layout.Shape, layout.Rotation, clock.UtcNow));
    }

    /// <summary>
    /// Supprime la salle et ses tables ; refusé tant qu'une de ses tables est occupée.
    /// </summary>
    /// <param name="occupiedTables">Tables de la salle avec une session ouverte, lues par l'Application.</param>
    public void Delete(IReadOnlyCollection<TableId> occupiedTables, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(occupiedTables);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(_tables.Exists(table => occupiedTables.Contains(table.Id)), DiningAreaErrors.AreaHasOpenSession);

        Raise(new DiningAreaDeleted(TenantId, Id, LocationId, _tables.Select(table => table.Id).ToList(), clock.UtcNow));
    }

    public void ChangeTableCapacity(TableId tableId, int capacity, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var table = GetTable(tableId);
        var previous = table.Capacity;
        table.ChangeCapacity(capacity);
        if (previous == capacity)
        {
            return;
        }

        Raise(new TableCapacityChanged(TenantId, Id, LocationId, tableId, previous, capacity, clock.UtcNow));
    }

    public void SetTableOutOfService(TableId tableId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        GetTable(tableId).SetOutOfService();
        Raise(new TableSetOutOfService(TenantId, Id, LocationId, tableId, clock.UtcNow));
    }

    public void RestoreTable(TableId tableId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        GetTable(tableId).Restore();
        Raise(new TableRestored(TenantId, Id, LocationId, tableId, clock.UtcNow));
    }

    /// <summary>Donne un jeton de QR aux tables qui n'en ont pas encore ; renvoie le nombre de tables complétées.</summary>
    public int EnsureQrTokens(Func<string> newToken)
    {
        ArgumentNullException.ThrowIfNull(newToken);
        var missing = _tables.Where(table => table.QrToken is null).ToList();
        missing.ForEach(table => table.SetQrToken(newToken()));
        return missing.Count;
    }

    /// <summary>Nouveau jeton pour la table : l'ancien QR (perdu, copié) ne commande plus rien.</summary>
    public void RotateQrToken(TableId tableId, string token) => GetTable(tableId).SetQrToken(token);

    /// <summary>Instantané d'une table nécessaire pour y ouvrir une <see cref="TableSession"/>.</summary>
    public TableReference GetTableReference(TableId tableId)
    {
        var table = GetTable(tableId);
        return new TableReference(TenantId, LocationId, table.Id, table.IsInService);
    }

    private RestaurantTable GetTable(TableId tableId)
    {
        var table = _tables.Find(t => t.Id == tableId);
        DomainException.ThrowIf(table is null, DiningAreaErrors.TableNotFound);
        return table!;
    }

    private static string NormalizeName(string? name)
    {
        var normalized = DisplayText.Normalize(name);
        DomainException.ThrowIf(normalized.Length == 0, DiningAreaErrors.NameRequired);
        DomainException.ThrowIf(normalized.Length > NameMaxLength, DiningAreaErrors.NameTooLong);
        return normalized;
    }
}
