namespace Delivery.Domain;

/// <summary>
/// Réglages de retrait et de livraison d'UN établissement : services proposés, délais annoncés et zones de livraison.
/// La livraison ne peut être activée qu'avec au moins une zone active ; tous les montants sont dans la devise des
/// réglages (celle du restaurant).
/// </summary>
public sealed class DeliverySettings : AggregateRoot<DeliverySettingsId>, ITenantOwned
{
    public const int MaxZones = 50;
    public const int MaxLeadMinutes = 240;
    public const int DefaultPickupLeadMinutes = 20;
    public const int DefaultDeliveryLeadMinutes = 45;

    private readonly List<DeliveryZone> _zones = [];

    private DeliverySettings(DeliverySettingsId id, TenantId tenantId, LocationId locationId, Currency currency, DateTimeOffset now) : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        Currency = currency;
        PickupEnabled = true;
        PickupLeadMinutes = DefaultPickupLeadMinutes;
        DeliveryEnabled = false;
        DeliveryLeadMinutes = DefaultDeliveryLeadMinutes;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    // Réservé à l'ORM.
    private DeliverySettings()
    {
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public Currency Currency { get; private set; } = null!;

    public bool PickupEnabled { get; private set; }

    /// <summary>Délai annoncé entre la commande et le retrait.</summary>
    public int PickupLeadMinutes { get; private set; }

    public bool DeliveryEnabled { get; private set; }

    /// <summary>Délai de préparation avant le départ du livreur (le trajet est le délai de la zone).</summary>
    public int DeliveryLeadMinutes { get; private set; }

    public IReadOnlyList<DeliveryZone> Zones => _zones.AsReadOnly();

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Réglages par défaut : retrait proposé (20 min), livraison désactivée tant qu'aucune zone n'existe.</summary>
    public static DeliverySettings Create(TenantId tenantId, LocationReference location, Currency currency, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(location.TenantId != tenantId, CommonErrors.TenantMismatch);

        var settings = new DeliverySettings(DeliverySettingsId.New(), tenantId, location.LocationId, currency, clock.UtcNow);
        settings.Raise(new DeliverySettingsCreated(tenantId, settings.Id, settings.LocationId, settings.CreatedAtUtc));
        return settings;
    }

    public void ConfigurePickup(bool enabled, int leadMinutes, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureLeadTime(leadMinutes);
        PickupEnabled = enabled;
        PickupLeadMinutes = leadMinutes;
        ServicesChanged(clock);
    }

    public void ConfigureDelivery(bool enabled, int leadMinutes, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureLeadTime(leadMinutes);
        DomainException.ThrowIf(enabled && !_zones.Any(zone => zone.IsActive), DeliverySettingsErrors.DeliveryNeedsZone);
        DeliveryEnabled = enabled;
        DeliveryLeadMinutes = leadMinutes;
        ServicesChanged(clock);
    }

    public DeliveryZoneId AddZone(string? name, Money fee, Money minimumOrder, int estimatedMinutes, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(_zones.Count >= MaxZones, DeliverySettingsErrors.TooManyZones);
        var zoneName = ValidZone(null, name, fee, minimumOrder, estimatedMinutes);

        var zone = new DeliveryZone(DeliveryZoneId.New(), zoneName, fee, minimumOrder, estimatedMinutes);
        _zones.Add(zone);
        ZonesChanged(clock);
        return zone.Id;
    }

    public void UpdateZone(DeliveryZoneId zoneId, string? name, Money fee, Money minimumOrder, int estimatedMinutes, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var zone = GetZone(zoneId);
        var zoneName = ValidZone(zone.Id, name, fee, minimumOrder, estimatedMinutes);
        zone.Change(zoneName, fee, minimumOrder, estimatedMinutes);
        ZonesChanged(clock);
    }

    public void SetZoneActive(DeliveryZoneId zoneId, bool active, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var zone = GetZone(zoneId);
        if (zone.IsActive == active)
        {
            return;
        }

        DomainException.ThrowIf(!active && IsLastActiveZoneOfDelivery(zone), DeliverySettingsErrors.LastActiveZone);
        zone.SetActive(active);
        ZonesChanged(clock);
    }

    public void RemoveZone(DeliveryZoneId zoneId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var zone = GetZone(zoneId);
        DomainException.ThrowIf(IsLastActiveZoneOfDelivery(zone), DeliverySettingsErrors.LastActiveZone);
        _zones.Remove(zone);
        ZonesChanged(clock);
    }

    /// <summary>La zone si elle existe et est active ; sinon null.</summary>
    public DeliveryZone? ActiveZone(DeliveryZoneId zoneId) => _zones.SingleOrDefault(zone => zone.Id == zoneId && zone.IsActive);

    private bool IsLastActiveZoneOfDelivery(DeliveryZone zone) =>
        DeliveryEnabled && zone.IsActive && _zones.Count(candidate => candidate.IsActive) == 1;

    private DeliveryZone GetZone(DeliveryZoneId zoneId) =>
        _zones.SingleOrDefault(zone => zone.Id == zoneId) ?? throw new DomainException(DeliverySettingsErrors.ZoneNotFound);

    private string ValidZone(DeliveryZoneId? self, string? name, Money fee, Money minimumOrder, int estimatedMinutes)
    {
        ArgumentNullException.ThrowIfNull(fee);
        ArgumentNullException.ThrowIfNull(minimumOrder);
        var zoneName = name?.Trim() ?? string.Empty;
        DomainException.ThrowIf(zoneName.Length == 0, DeliverySettingsErrors.ZoneNameRequired);
        DomainException.ThrowIf(zoneName.Length > DeliveryZone.NameMaxLength, DeliverySettingsErrors.ZoneNameTooLong);
        DomainException.ThrowIf(
            _zones.Any(zone => zone.Id != self && string.Equals(zone.Name, zoneName, StringComparison.OrdinalIgnoreCase)),
            DeliverySettingsErrors.ZoneNameAlreadyUsed);
        DomainException.ThrowIf(fee.Currency != Currency || minimumOrder.Currency != Currency, CommonErrors.CurrencyMismatch);
        DomainException.ThrowIf(fee.IsNegative || minimumOrder.IsNegative, DeliverySettingsErrors.ZoneAmountNegative);
        DomainException.ThrowIf(
            estimatedMinutes is < DeliveryZone.MinMinutes or > DeliveryZone.MaxMinutes, DeliverySettingsErrors.ZoneMinutesOutOfRange);
        return zoneName;
    }

    private static void EnsureLeadTime(int minutes) =>
        DomainException.ThrowIf(minutes is < 0 or > MaxLeadMinutes, DeliverySettingsErrors.LeadTimeOutOfRange);

    private void ServicesChanged(IClock clock)
    {
        UpdatedAtUtc = clock.UtcNow;
        Raise(new DeliveryServicesChanged(TenantId, Id, LocationId, PickupEnabled, DeliveryEnabled, UpdatedAtUtc));
    }

    private void ZonesChanged(IClock clock)
    {
        UpdatedAtUtc = clock.UtcNow;
        Raise(new DeliveryZonesChanged(TenantId, Id, LocationId, _zones.Count(zone => zone.IsActive), UpdatedAtUtc));
    }
}
