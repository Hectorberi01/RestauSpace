using Delivery.Domain;

namespace Delivery.Application.Queries;

public sealed record DeliveryZoneDto(Guid Id, string Name, decimal Fee, decimal MinimumOrder, int EstimatedMinutes, bool IsActive);

/// <param name="IsConfigured">Faux : l'établissement n'a encore rien réglé, ce sont les valeurs par défaut.</param>
public sealed record DeliverySettingsDto(
    Guid LocationId,
    string CurrencyCode,
    bool PickupEnabled,
    int PickupLeadMinutes,
    bool DeliveryEnabled,
    int DeliveryLeadMinutes,
    IReadOnlyList<DeliveryZoneDto> Zones,
    bool IsConfigured,
    DateTimeOffset? UpdatedAtUtc)
{
    public static DeliverySettingsDto From(DeliverySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new DeliverySettingsDto(
            settings.LocationId.Value,
            settings.Currency.Code,
            settings.PickupEnabled,
            settings.PickupLeadMinutes,
            settings.DeliveryEnabled,
            settings.DeliveryLeadMinutes,
            settings.Zones
                .OrderBy(zone => zone.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(zone => new DeliveryZoneDto(zone.Id.Value, zone.Name, zone.Fee.Amount, zone.MinimumOrder.Amount, zone.EstimatedMinutes, zone.IsActive))
                .ToList(),
            true,
            settings.UpdatedAtUtc);
    }

    public static DeliverySettingsDto Defaults(LocationId locationId, Currency currency) => new(
        locationId.Value,
        currency.Code,
        true,
        DeliverySettings.DefaultPickupLeadMinutes,
        false,
        DeliverySettings.DefaultDeliveryLeadMinutes,
        [],
        false,
        null);
}

/// <summary>Coordonnées du client et du livreur comprises : réservé à delivery.operate, jamais journalisé.</summary>
public sealed record FulfillmentDto(
    Guid Id,
    Guid OrderId,
    Guid LocationId,
    FulfillmentMode Mode,
    FulfillmentStatus Status,
    string CustomerName,
    string? CustomerPhone,
    DateTimeOffset PromisedAtUtc,
    string? AddressLine,
    string? Landmark,
    string? Instructions,
    Guid? ZoneId,
    string? ZoneName,
    decimal? DeliveryFee,
    string? FeeCurrencyCode,
    string? CourierName,
    string? CourierPhone,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadyAtUtc,
    DateTimeOffset? AssignedAtUtc,
    DateTimeOffset? DepartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset? FailedAtUtc,
    string? FailureReason,
    DateTimeOffset? CancelledAtUtc,
    string? CancellationReason,
    Guid? CourierId = null,
    decimal? CourierFee = null,
    decimal? AmountToCollect = null,
    decimal? AmountCollected = null,
    CollectionMethod? CollectionMethod = null,
    Guid? RemittanceId = null,
    DateTimeOffset? RemittedAtUtc = null)
{
    public static FulfillmentDto From(Fulfillment fulfillment)
    {
        ArgumentNullException.ThrowIfNull(fulfillment);
        return new FulfillmentDto(
            fulfillment.Id.Value,
            fulfillment.OrderId.Value,
            fulfillment.LocationId.Value,
            fulfillment.Mode,
            fulfillment.Status,
            fulfillment.CustomerName,
            fulfillment.CustomerPhone?.Value,
            fulfillment.PromisedAtUtc,
            fulfillment.AddressLine,
            fulfillment.Landmark,
            fulfillment.Instructions,
            fulfillment.ZoneId?.Value,
            fulfillment.ZoneName,
            fulfillment.DeliveryFeeAmount,
            fulfillment.FeeCurrency?.Code,
            fulfillment.CourierName,
            fulfillment.CourierPhone?.Value,
            fulfillment.CreatedAtUtc,
            fulfillment.ReadyAtUtc,
            fulfillment.AssignedAtUtc,
            fulfillment.DepartedAtUtc,
            fulfillment.CompletedAtUtc,
            fulfillment.FailedAtUtc,
            fulfillment.FailureReason,
            fulfillment.CancelledAtUtc,
            fulfillment.CancellationReason,
            fulfillment.CourierId?.Value,
            fulfillment.CourierFeeAmount,
            fulfillment.AmountToCollect,
            fulfillment.AmountCollected,
            fulfillment.CollectionMethod,
            fulfillment.RemittanceId?.Value,
            fulfillment.RemittedAtUtc);
    }
}

public sealed record CourierDto(
    Guid Id,
    string Name,
    string? Phone,
    CourierKind Kind,
    decimal? FeePerDelivery,
    string? FeeCurrencyCode,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static CourierDto From(Courier courier)
    {
        ArgumentNullException.ThrowIfNull(courier);
        return new CourierDto(
            courier.Id.Value,
            courier.Name,
            courier.Phone?.Value,
            courier.Kind,
            courier.FeeAmount,
            courier.FeeCurrency?.Code,
            courier.IsActive,
            courier.CreatedAtUtc,
            courier.UpdatedAtUtc);
    }
}

/// <summary>Une course terminée en attente de remise : ce que le livreur a encaissé en espèces et son tarif.</summary>
public sealed record PendingDeliveryDto(
    Guid FulfillmentId,
    Guid OrderId,
    string CustomerName,
    string? ZoneName,
    DateTimeOffset? CompletedAtUtc,
    decimal? AmountCollected,
    CollectionMethod? CollectionMethod,
    decimal CashHeld,
    decimal CourierFee);

/// <summary>
/// Ce que le livreur doit au restaurant (ou l'inverse) sur ses courses pas encore remises. <paramref name="Expected"/> =
/// espèces encaissées - tarifs : positif, il doit le remettre ; négatif, on lui doit.
/// </summary>
public sealed record CourierBalanceDto(
    Guid? CourierId,
    string CourierName,
    string? CourierPhone,
    Guid LocationId,
    string CurrencyCode,
    int Deliveries,
    decimal CashHeld,
    decimal MobileMoneyCollected,
    decimal CourierFees,
    decimal Expected,
    IReadOnlyList<PendingDeliveryDto> Items);

public sealed record CourierRemittanceItemDto(Guid FulfillmentId, Guid OrderId, decimal CashCollected, decimal CourierFee);

/// <summary>Une remise : <paramref name="Counted"/> positif = remis par le livreur, négatif = payé au livreur ; écart = compté - attendu.</summary>
public sealed record CourierRemittanceDto(
    Guid Id,
    Guid LocationId,
    Guid? CourierId,
    string CourierName,
    string CurrencyCode,
    int Deliveries,
    decimal CashCollected,
    decimal CourierFees,
    decimal Expected,
    decimal Counted,
    decimal Difference,
    string? Note,
    Guid? RecordedBy,
    DateTimeOffset RecordedAtUtc,
    IReadOnlyList<CourierRemittanceItemDto> Items)
{
    public static CourierRemittanceDto From(CourierRemittance remittance, bool withItems = true)
    {
        ArgumentNullException.ThrowIfNull(remittance);
        return new CourierRemittanceDto(
            remittance.Id.Value,
            remittance.LocationId.Value,
            remittance.CourierId?.Value,
            remittance.CourierName,
            remittance.Currency.Code,
            remittance.Items.Count,
            remittance.CashCollected,
            remittance.CourierFees,
            remittance.Expected,
            remittance.Counted,
            remittance.Difference,
            remittance.Note,
            remittance.RecordedBy,
            remittance.RecordedAtUtc,
            withItems
                ? remittance.Items.Select(item => new CourierRemittanceItemDto(item.FulfillmentId.Value, item.OrderId.Value, item.CashCollected, item.CourierFee)).ToList()
                : []);
    }
}
