namespace Delivery.Domain;

/// <summary>Livreur salarié du restaurant, ou indépendant (zémidjan, coursier) payé à la course.</summary>
public enum CourierKind
{
    Staff = 1,
    Independent = 2,
}

public static class CourierErrors
{
    public static readonly DomainError NameRequired = new("delivery.courier.name_required", "The courier's name is required.");
    public static readonly DomainError NameTooLong = new("delivery.courier.name_too_long", "The courier's name is limited to 80 characters.");
    public static readonly DomainError KindInvalid = new("delivery.courier.kind_invalid", "The courier is either staff or independent.");
    public static readonly DomainError FeeInvalid = new("delivery.courier.fee_invalid", "The fee per delivery must be zero or more, in the restaurant's currency.");
    public static readonly DomainError Inactive = new("delivery.courier.inactive", "This courier is deactivated.");
}

/// <summary>
/// Fiche d'un livreur du restaurant : nom, téléphone, salarié ou indépendant, et tarif par course. Le tarif est figé sur
/// chaque livraison qui lui est confiée : le changer ne modifie pas les courses passées. Désactivé, il n'est plus
/// proposé mais ses courses restent. Le nom et le téléphone ne sortent jamais dans un événement.
/// </summary>
public sealed class Courier : AggregateRoot<CourierId>, ITenantOwned
{
    public const int NameMaxLength = Fulfillment.CourierNameMaxLength;

    private Courier(CourierId id, TenantId tenantId, DateTimeOffset now) : base(id)
    {
        TenantId = tenantId;
        IsActive = true;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    // Réservé à l'ORM.
    private Courier()
    {
    }

    public TenantId TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public PhoneNumber? Phone { get; private set; }

    public CourierKind Kind { get; private set; }

    /// <summary>Tarif par course ; null quand il n'y en a pas (livreur salarié, le plus souvent).</summary>
    public decimal? FeeAmount { get; private set; }

    public Currency? FeeCurrency { get; private set; }

    public Money? Fee => FeeAmount is { } amount && FeeCurrency is not null ? Money.Of(amount, FeeCurrency) : null;

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static Courier Create(TenantId tenantId, string? name, PhoneNumber? phone, CourierKind kind, Money? feePerDelivery, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        var courier = new Courier(CourierId.New(), tenantId, clock.UtcNow);
        courier.Apply(name, phone, kind, feePerDelivery);
        return courier;
    }

    public void Update(string? name, PhoneNumber? phone, CourierKind kind, Money? feePerDelivery, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Apply(name, phone, kind, feePerDelivery);
        UpdatedAtUtc = clock.UtcNow;
    }

    public void Deactivate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        IsActive = false;
        UpdatedAtUtc = clock.UtcNow;
    }

    public void Activate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        IsActive = true;
        UpdatedAtUtc = clock.UtcNow;
    }

    private void Apply(string? name, PhoneNumber? phone, CourierKind kind, Money? feePerDelivery)
    {
        var value = name?.Trim() ?? string.Empty;
        DomainException.ThrowIf(value.Length == 0, CourierErrors.NameRequired);
        DomainException.ThrowIf(value.Length > NameMaxLength, CourierErrors.NameTooLong);
        DomainException.ThrowIf(!Enum.IsDefined(kind), CourierErrors.KindInvalid);
        DomainException.ThrowIf(feePerDelivery is { IsNegative: true }, CourierErrors.FeeInvalid);
        Name = value;
        Phone = phone;
        Kind = kind;
        FeeAmount = feePerDelivery?.Amount;
        FeeCurrency = feePerDelivery?.Currency;
    }
}
