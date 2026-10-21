namespace Inventory.Domain;

public readonly record struct PurchaseOrderId
{
    private PurchaseOrderId(Guid value) => Value = value;

    public Guid Value { get; }

    public static PurchaseOrderId New() => new(Guid.CreateVersion7());

    public static PurchaseOrderId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new PurchaseOrderId(value);
    }

    public override string ToString() => Value.ToString();
}

public enum PurchaseOrderStatus
{
    Draft = 1,
    Sent = 2,
    PartiallyReceived = 3,
    Received = 4,
    Cancelled = 5,
}

/// <summary>
/// Une ligne du bon : un article de stock (ingrédient d'un établissement), la quantité commandée et le prix convenu
/// d'une unité, dans l'unité de l'article ; ce qui a déjà été reçu.
/// </summary>
public sealed record PurchaseOrderLine(InventoryItemId ItemId, IngredientId IngredientId, UnitOfMeasure Unit, decimal Ordered, decimal? UnitPrice, decimal Received)
{
    public decimal Remaining => Math.Max(0m, Ordered - Received);
}

/// <summary>Une réception à porter au stock : l'article, la quantité reçue et ce qu'elle a coûté.</summary>
public sealed record PurchaseReceipt(InventoryItemId ItemId, Quantity Quantity, decimal? TotalCost);

/// <summary>
/// Bon de commande fournisseur d'un établissement : brouillon, envoyé, reçu en une ou plusieurs fois (rapproché ligne à
/// ligne), ou annulé. Chaque réception devient une entrée de stock chiffrée, faite par l'Application.
/// </summary>
public sealed class PurchaseOrder : AggregateRoot<PurchaseOrderId>, ITenantOwned
{
    public const int MaxLines = 100;
    public const int NotesMaxLength = 500;

    private List<PurchaseOrderLine> _lines = [];

    private PurchaseOrder(PurchaseOrderId id, TenantId tenantId, LocationId locationId, SupplierId supplierId, string number, DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        SupplierId = supplierId;
        Number = number;
        CreatedAtUtc = createdAtUtc;
        Status = PurchaseOrderStatus.Draft;
    }

    // Réservé à l'ORM.
    private PurchaseOrder()
    {
        Number = string.Empty;
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public SupplierId SupplierId { get; private set; }

    /// <summary>« BC-AAMMJJ-XXXX », à reporter sur le bon de livraison et dans le registre du stock.</summary>
    public string Number { get; private set; }

    public PurchaseOrderStatus Status { get; private set; }

    public string? Notes { get; private set; }

    public IReadOnlyList<PurchaseOrderLine> Lines => _lines.AsReadOnly();

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? SentAtUtc { get; private set; }

    public DateTimeOffset? ReceivedAtUtc { get; private set; }

    public decimal? EstimatedTotal => _lines.All(line => line.UnitPrice is not null) ? _lines.Sum(line => line.Ordered * line.UnitPrice!.Value) : null;

    public static PurchaseOrder Create(TenantId tenantId, LocationReference location, Supplier supplier, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(supplier);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(location.TenantId != tenantId || supplier.TenantId != tenantId, CommonErrors.TenantMismatch);
        DomainException.ThrowIf(!supplier.IsActive, PurchasingErrors.SupplierInactive);
        var id = PurchaseOrderId.New();
        var now = clock.UtcNow;
        return new PurchaseOrder(id, tenantId, location.LocationId, supplier.Id, $"BC-{now:yyMMdd}-{id.Value.ToString("N")[^4..].ToUpperInvariant()}", now);
    }

    /// <summary>Remplace les lignes et la note du brouillon.</summary>
    public void Compose(IReadOnlyList<(InventoryItem Item, Quantity Quantity, decimal? UnitPrice)> lines, string? notes)
    {
        ArgumentNullException.ThrowIfNull(lines);
        DomainException.ThrowIf(Status != PurchaseOrderStatus.Draft, PurchasingErrors.OrderNotDraft);
        DomainException.ThrowIf(lines.Count > MaxLines, PurchasingErrors.TooManyLines);
        DomainException.ThrowIf(lines.Select(line => line.Item.Id).Distinct().Count() != lines.Count, PurchasingErrors.LineDuplicate);
        var trimmed = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        DomainException.ThrowIf(trimmed?.Length > NotesMaxLength, PurchasingErrors.NotesTooLong);

        var composed = new List<PurchaseOrderLine>();
        foreach (var (item, quantity, price) in lines)
        {
            this.EnsureSameTenant(item.TenantId);
            DomainException.ThrowIf(item.LocationId != LocationId, PurchasingErrors.LineElsewhere);
            var ordered = quantity.ConvertTo(item.Unit).Amount;
            DomainException.ThrowIf(ordered <= 0m, InventoryErrors.MovementQuantityNotPositive);
            DomainException.ThrowIf(price is < 0m or > InventoryItem.MaxCost, InventoryErrors.CostInvalid);
            composed.Add(new PurchaseOrderLine(item.Id, item.IngredientId, item.Unit, ordered, price, 0m));
        }

        _lines = composed;
        Notes = trimmed;
    }

    public void Send(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != PurchaseOrderStatus.Draft, PurchasingErrors.OrderNotDraft);
        DomainException.ThrowIf(_lines.Count == 0, PurchasingErrors.OrderEmpty);
        Status = PurchaseOrderStatus.Sent;
        SentAtUtc = clock.UtcNow;
    }

    /// <summary>
    /// Livraison rapprochée du bon : les quantités reçues (dans l'unité de chaque article), au prix du bon sauf prix
    /// constaté différent. Plus que commandé est accepté (geste du fournisseur). Renvoie les entrées de stock à faire.
    /// </summary>
    public IReadOnlyList<PurchaseReceipt> Receive(IReadOnlyList<(InventoryItemId ItemId, decimal Quantity, decimal? UnitPrice)> received, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(received);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status is not (PurchaseOrderStatus.Sent or PurchaseOrderStatus.PartiallyReceived), PurchasingErrors.OrderNotReceivable);
        var useful = received.Where(item => item.Quantity > 0m).ToList();
        DomainException.ThrowIf(useful.Count == 0, PurchasingErrors.NothingReceived);
        DomainException.ThrowIf(useful.Select(item => item.ItemId).Distinct().Count() != useful.Count, PurchasingErrors.LineDuplicate);

        var receipts = new List<PurchaseReceipt>();
        foreach (var (itemId, quantity, price) in useful)
        {
            var index = _lines.FindIndex(line => line.ItemId == itemId);
            DomainException.ThrowIf(index < 0, PurchasingErrors.LineNotOnOrder);
            var line = _lines[index];
            var amount = Quantity.Create(quantity, line.Unit);
            DomainException.ThrowIf(price is < 0m or > InventoryItem.MaxCost, InventoryErrors.CostInvalid);
            var unitPrice = price ?? line.UnitPrice;
            _lines[index] = line with { Received = line.Received + amount.Amount, UnitPrice = unitPrice };
            receipts.Add(new PurchaseReceipt(itemId, amount, unitPrice is { } paid ? decimal.Round(paid * amount.Amount, 2) : null));
        }

        Status = _lines.All(line => line.Remaining == 0m) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
        if (Status == PurchaseOrderStatus.Received)
        {
            ReceivedAtUtc = clock.UtcNow;
        }

        return receipts;
    }

    /// <summary>Le reste ne viendra pas : le bon est clos tel quel.</summary>
    public void CloseAsReceived(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != PurchaseOrderStatus.PartiallyReceived, PurchasingErrors.OrderNotReceivable);
        Status = PurchaseOrderStatus.Received;
        ReceivedAtUtc = clock.UtcNow;
    }

    public void Cancel()
    {
        DomainException.ThrowIf(Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Sent), PurchasingErrors.OrderNotCancellable);
        Status = PurchaseOrderStatus.Cancelled;
    }
}
