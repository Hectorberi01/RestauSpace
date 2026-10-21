using Integrations.Domain;

namespace Integrations.Application.Webhooks;

/// <summary>
/// Le catalogue des événements publiés aux webhooks : un endpoint ne peut s'abonner qu'à eux. Chaque code est un
/// contrat public (nom et forme des données versionnés par <see cref="SchemaVersion"/>) : on en ajoute, on n'en change
/// pas le sens. Les données ne contiennent que des ids, des montants, des statuts et des dates : aucune donnée
/// personnelle (nom, e-mail, téléphone) ne sort par un webhook.
/// </summary>
public static class WebhookEvents
{
    public const int SchemaVersion = 1;

    public const string OrderPlaced = "orders.order_placed";
    public const string OrderCompleted = "orders.order_completed";
    public const string OrderCancelled = "orders.order_cancelled";
    public const string PaymentCaptured = "payments.payment_captured";
    public const string RefundSucceeded = "payments.refund_succeeded";
    public const string ReservationCreated = "reservations.reservation_created";
    public const string ReservationConfirmed = "reservations.reservation_confirmed";
    public const string ReservationCancelled = "reservations.reservation_cancelled";
    public const string ReservationNoShow = "reservations.reservation_no_show";
    public const string KitchenTicketReady = "kitchen.ticket_ready";
    public const string LowStockReached = "inventory.low_stock_reached";
    public const string CatalogChanged = "catalog.changed";

    /// <summary>Événement factice de l'envoi de test : jamais publié, on ne peut pas s'y abonner.</summary>
    public const string Ping = "integrations.ping";

    public static IReadOnlyList<string> All { get; } =
    [
        OrderPlaced, OrderCompleted, OrderCancelled,
        PaymentCaptured, RefundSucceeded,
        ReservationCreated, ReservationConfirmed, ReservationCancelled, ReservationNoShow,
        KitchenTicketReady, LowStockReached,
        CatalogChanged,
    ];

    private static readonly HashSet<string> Published = new(All, StringComparer.Ordinal);

    /// <summary>Un code du catalogue ; sinon <c>integrations.event_type.not_published</c> (après le contrôle de format du domaine).</summary>
    public static EventTypeCode Require(string? code)
    {
        var eventType = EventTypeCode.Create(code);
        return Published.Contains(eventType.Value)
            ? eventType
            : throw new DomainException(IntegrationsApplicationErrors.EventTypeNotPublished);
    }
}
