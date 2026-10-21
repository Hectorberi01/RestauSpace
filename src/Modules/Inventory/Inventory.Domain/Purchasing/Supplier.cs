namespace Inventory.Domain;

public readonly record struct SupplierId
{
    private SupplierId(Guid value) => Value = value;

    public Guid Value { get; }

    public static SupplierId New() => new(Guid.CreateVersion7());

    public static SupplierId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new SupplierId(value);
    }

    public override string ToString() => Value.ToString();
}

/// <summary>Fournisseur du restaurant : à qui l'on commande, et comment le joindre (contact professionnel).</summary>
public sealed class Supplier : AggregateRoot<SupplierId>, ITenantOwned
{
    public const int NameMaxLength = 100;
    public const int ContactMaxLength = 120;
    public const int NotesMaxLength = 500;

    private Supplier(SupplierId id, TenantId tenantId, DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        CreatedAtUtc = createdAtUtc;
        Name = string.Empty;
        IsActive = true;
    }

    // Réservé à l'ORM.
    private Supplier()
    {
        Name = string.Empty;
    }

    public TenantId TenantId { get; private set; }

    public string Name { get; private set; }

    public string? Phone { get; private set; }

    public string? Email { get; private set; }

    /// <summary>Délais, jours de livraison, conditions…</summary>
    public string? Notes { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static Supplier Create(TenantId tenantId, string? name, string? phone, string? email, string? notes, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        var supplier = new Supplier(SupplierId.New(), tenantId, clock.UtcNow);
        supplier.Change(name, phone, email, notes);
        return supplier;
    }

    public void Change(string? name, string? phone, string? email, string? notes)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        DomainException.ThrowIf(trimmed.Length is 0 or > NameMaxLength, PurchasingErrors.SupplierNameInvalid);
        Name = trimmed;
        Phone = Optional(phone, ContactMaxLength);
        Email = Optional(email, ContactMaxLength);
        Notes = Optional(notes, NotesMaxLength);
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    private static string? Optional(string? value, int max)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        DomainException.ThrowIf(trimmed?.Length > max, PurchasingErrors.SupplierFieldTooLong);
        return trimmed;
    }
}

public static class PurchasingErrors
{
    public static readonly DomainError SupplierNameInvalid = new("inventory.supplier.name_invalid", "A supplier name has 1 to 100 characters.");
    public static readonly DomainError SupplierFieldTooLong = new("inventory.supplier.field_too_long", "Phone, email or notes too long.");
    public static readonly DomainError SupplierInactive = new("inventory.supplier.inactive", "This supplier is no longer used.");
    public static readonly DomainError OrderNotDraft = new("inventory.purchase_order.not_draft", "Only a draft purchase order can be changed.");
    public static readonly DomainError OrderEmpty = new("inventory.purchase_order.empty", "A purchase order needs at least one line.");
    public static readonly DomainError OrderNotReceivable = new("inventory.purchase_order.not_receivable", "Only a sent purchase order can be received.");
    public static readonly DomainError OrderNotCancellable = new("inventory.purchase_order.not_cancellable", "A purchase order already (partly) received cannot be cancelled.");
    public static readonly DomainError LineDuplicate = new("inventory.purchase_order.line_duplicate", "Each stock item appears once on a purchase order.");
    public static readonly DomainError LineNotOnOrder = new("inventory.purchase_order.line_not_on_order", "This stock item is not on the purchase order.");
    public static readonly DomainError LineElsewhere = new("inventory.purchase_order.line_elsewhere", "A purchase order is for the stock of one location.");
    public static readonly DomainError TooManyLines = new("inventory.purchase_order.too_many_lines", "A purchase order has at most 100 lines.");
    public static readonly DomainError NothingReceived = new("inventory.purchase_order.nothing_received", "Enter at least one received quantity.");
    public static readonly DomainError NotesTooLong = new("inventory.purchase_order.notes_too_long", "Notes are 500 characters at most.");
}
