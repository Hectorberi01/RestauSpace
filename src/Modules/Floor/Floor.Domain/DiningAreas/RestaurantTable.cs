namespace Floor.Domain;

/// <summary>Table d'une salle. Seule sa <see cref="DiningArea"/> peut la créer ou la modifier.</summary>
public sealed class RestaurantTable : Entity<TableId>
{
    private RestaurantTable(TableId id, TableLabel label, int capacity)
        : base(id)
    {
        Label = label;
        Capacity = capacity;
        Status = TableStatus.Available;
    }

    // Réservé à l'ORM.
    private RestaurantTable()
    {
        Label = null!;
    }

    public TableLabel Label { get; private set; }

    /// <summary>Nombre de places.</summary>
    public int Capacity { get; private set; }

    public TableStatus Status { get; private set; }

    public bool IsInService => Status == TableStatus.Available;

    /// <summary>
    /// Jeton du QR code collé sur la table (aléatoire, sans le nom de la table) : il ouvre la commande à cette table sur
    /// le site du restaurant. Null tant qu'aucun QR n'a été préparé ; un nouveau jeton rend l'ancien QR inutilisable.
    /// </summary>
    public string? QrToken { get; private set; }

    /// <summary>Place sur le plan dessiné ; null tant que la table n'y a pas été posée.</summary>
    public TableLayout? Layout { get; private set; }

    internal static RestaurantTable Create(TableLabel label, int capacity)
    {
        EnsureValidCapacity(capacity);
        return new RestaurantTable(TableId.New(), label, capacity);
    }

    internal void ChangeCapacity(int capacity)
    {
        EnsureValidCapacity(capacity);
        Capacity = capacity;
    }

    internal void Rename(TableLabel label) => Label = label;

    internal void Place(TableLayout layout) => Layout = layout;

    internal void SetQrToken(string token)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(token) || token.Length > TableQrTokens.MaxLength, DiningAreaErrors.QrTokenInvalid);
        QrToken = token;
    }

    internal void SetOutOfService()
    {
        DomainException.ThrowIf(Status == TableStatus.OutOfService, DiningAreaErrors.TableAlreadyOutOfService);
        Status = TableStatus.OutOfService;
    }

    internal void Restore()
    {
        DomainException.ThrowIf(Status != TableStatus.OutOfService, DiningAreaErrors.TableNotOutOfService);
        Status = TableStatus.Available;
    }

    private static void EnsureValidCapacity(int capacity) =>
        DomainException.ThrowIf(capacity <= 0, DiningAreaErrors.TableCapacityNotPositive);
}
