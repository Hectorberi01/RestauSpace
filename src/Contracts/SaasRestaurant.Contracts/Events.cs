using System.Text.Json.Serialization;

namespace SaasRestaurant.Contracts.Orders
{
    [IntegrationEvent("orders", "OrderCreated")]
    public sealed record OrderCreated(Guid TenantId, Guid OrderId, Guid LocationId, Guid? TableSessionId) : ILocatedIntegrationEvent;

    [IntegrationEvent("orders", "OrderPlaced")]
    public sealed record OrderPlaced(
        Guid TenantId, Guid OrderId, Guid LocationId, string OrderNumber, string OrderType,
        MoneyAmount SubTotal, MoneyAmount DiscountTotal, MoneyAmount TaxTotal, MoneyAmount Total) : ILocatedIntegrationEvent;

    [IntegrationEvent("orders", "OrderAccepted")]
    public sealed record OrderAccepted(Guid TenantId, Guid OrderId, Guid LocationId) : ILocatedIntegrationEvent;

    [IntegrationEvent("orders", "OrderPreparationStarted")]
    public sealed record OrderPreparationStarted(Guid TenantId, Guid OrderId, Guid LocationId) : ILocatedIntegrationEvent;

    [IntegrationEvent("orders", "OrderReady")]
    public sealed record OrderReady(Guid TenantId, Guid OrderId, Guid LocationId) : ILocatedIntegrationEvent;

    [IntegrationEvent("orders", "OrderCompleted")]
    public sealed record OrderCompleted(Guid TenantId, Guid OrderId, Guid LocationId, MoneyAmount Total, Guid? CustomerId) : ILocatedIntegrationEvent;

    [IntegrationEvent("orders", "OrderTableSessionChanged")]
    public sealed record OrderTableSessionChanged(
        Guid TenantId, Guid OrderId, Guid LocationId, Guid? PreviousTableSessionId, Guid TableSessionId) : ILocatedIntegrationEvent;

    [IntegrationEvent("orders", "OrderCancelled")]
    public sealed record OrderCancelled(
        Guid TenantId, Guid OrderId, Guid LocationId, string PreviousStatus, string? Reason, MoneyAmount Total, Guid? CustomerId) : ILocatedIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Delivery
{
    /// <summary><c>DeliveryFee</c> : frais de la zone pour une livraison, null pour un retrait.</summary>
    [IntegrationEvent("delivery", "FulfillmentCreated")]
    public sealed record FulfillmentCreated(Guid TenantId, Guid FulfillmentId, Guid OrderId, string Mode, MoneyAmount? DeliveryFee) : IIntegrationEvent;

    [IntegrationEvent("delivery", "FulfillmentStatusChanged")]
    public sealed record FulfillmentStatusChanged(Guid TenantId, Guid FulfillmentId, Guid OrderId, string Mode, string Status) : IIntegrationEvent;

    /// <summary>Le livreur a encaissé le reste à payer à la livraison. <c>Method</c> : Cash ou MobileMoney.</summary>
    [IntegrationEvent("delivery", "DeliveryPaymentCollected")]
    public sealed record DeliveryPaymentCollected(Guid TenantId, Guid FulfillmentId, Guid OrderId, Guid LocationId, MoneyAmount Amount, string Method)
        : ILocatedIntegrationEvent;

    /// <summary>
    /// Remise d'un livreur. <c>Counted</c> positif : espèces remises par le livreur ; négatif : espèces payées au livreur.
    /// Montants seulement, jamais le nom ni le téléphone.
    /// </summary>
    [IntegrationEvent("delivery", "CourierRemittanceRecorded")]
    public sealed record CourierRemittanceRecorded(Guid TenantId, Guid RemittanceId, Guid LocationId, MoneyAmount Counted, MoneyAmount Expected)
        : ILocatedIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Catalog
{
    [IntegrationEvent("catalog", "CatalogPriced")]
    public sealed record CatalogPriced(Guid TenantId) : IIntegrationEvent;

    [IntegrationEvent("catalog", "CatalogChanged")]
    public sealed record CatalogChanged(Guid TenantId) : IIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Payments
{
    [IntegrationEvent("payments", "PaymentCaptured")]
    public sealed record PaymentCaptured(Guid TenantId, Guid PaymentId, Guid OrderId, MoneyAmount CapturedAmount) : IIntegrationEvent;

    [IntegrationEvent("payments", "PaymentSettlementChanged")]
    public sealed record PaymentSettlementChanged(Guid TenantId, Guid PaymentId, Guid OrderId, MoneyAmount SettledAmount) : IIntegrationEvent;

    [IntegrationEvent("payments", "PaymentFailed")]
    public sealed record PaymentFailed(Guid TenantId, Guid PaymentId, Guid OrderId) : IIntegrationEvent;

    [IntegrationEvent("payments", "DepositSettlementChanged")]
    public sealed record DepositSettlementChanged(Guid TenantId, Guid PaymentId, Guid ReservationId, MoneyAmount SettledAmount) : IIntegrationEvent;

    [IntegrationEvent("payments", "DepositApplied")]
    public sealed record DepositApplied(Guid TenantId, Guid PaymentId, Guid ReservationId, Guid OrderId, MoneyAmount Amount) : IIntegrationEvent;

    [IntegrationEvent("payments", "RefundSucceeded")]
    public sealed record RefundSucceeded(
        Guid TenantId, Guid PaymentId, Guid OrderId, Guid RefundId, MoneyAmount Amount, MoneyAmount TotalRefunded, bool IsFullRefund) : IIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Reservations
{
    [IntegrationEvent("reservations", "ReservationCreated")]
    public sealed record ReservationCreated(Guid TenantId, Guid ReservationId, Guid LocationId, int PartySize, DateTimeOffset StartAtUtc, TimeSpan Duration) : ILocatedIntegrationEvent;

    [IntegrationEvent("reservations", "ReservationConfirmed")]
    public sealed record ReservationConfirmed(Guid TenantId, Guid ReservationId, Guid LocationId) : ILocatedIntegrationEvent;

    [IntegrationEvent("reservations", "ReservationCancelled")]
    public sealed record ReservationCancelled(Guid TenantId, Guid ReservationId, Guid LocationId, string CancelledBy) : ILocatedIntegrationEvent;

    [IntegrationEvent("reservations", "ReservationNoShow")]
    public sealed record ReservationNoShow(Guid TenantId, Guid ReservationId, Guid LocationId) : ILocatedIntegrationEvent;

    [IntegrationEvent("reservations", "ReservationRescheduled")]
    public sealed record ReservationRescheduled(Guid TenantId, Guid ReservationId, Guid LocationId) : ILocatedIntegrationEvent;

    [IntegrationEvent("reservations", "ReservationDepositRequested")]
    public sealed record ReservationDepositRequested(Guid TenantId, Guid ReservationId, Guid LocationId, MoneyAmount Amount, DateTimeOffset DueBeforeUtc) : ILocatedIntegrationEvent;

    [IntegrationEvent("reservations", "ReservationDepositPaid")]
    public sealed record ReservationDepositPaid(Guid TenantId, Guid ReservationId, Guid LocationId, MoneyAmount Amount) : ILocatedIntegrationEvent;

    [IntegrationEvent("reservations", "ReservationDepositRefundRequested")]
    public sealed record ReservationDepositRefundRequested(Guid TenantId, Guid ReservationId, Guid LocationId) : ILocatedIntegrationEvent;

    [IntegrationEvent("reservations", "WaitlistOfferMade")]
    public sealed record WaitlistOfferMade(Guid TenantId, Guid EntryId, Guid LocationId, DateTimeOffset StartAtUtc, DateTimeOffset ExpiresAtUtc) : ILocatedIntegrationEvent;

    [IntegrationEvent("reservations", "WaitlistOfferReleased")]
    public sealed record WaitlistOfferReleased(Guid TenantId, Guid EntryId, Guid LocationId) : ILocatedIntegrationEvent;

    [IntegrationEvent("reservations", "WaitlistEntryClosed")]
    public sealed record WaitlistEntryClosed(Guid TenantId, Guid EntryId, Guid LocationId, string Status) : ILocatedIntegrationEvent;

    [IntegrationEvent("reservations", "ReservationPartySizeChanged")]
    public sealed record ReservationPartySizeChanged(Guid TenantId, Guid ReservationId, Guid LocationId, int PreviousPartySize, int PartySize) : ILocatedIntegrationEvent;

    [IntegrationEvent("reservations", "GuestSeated")]
    public sealed record GuestSeated(Guid TenantId, Guid ReservationId, Guid LocationId, int PartySize) : ILocatedIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Floor
{
    [IntegrationEvent("floor", "WaitingPartyNotified")]
    public sealed record WaitingPartyNotified(Guid TenantId, Guid PartyId, Guid LocationId, int Count) : ILocatedIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Kitchen
{
    [IntegrationEvent("kitchen", "KitchenTicketCreated")]
    public sealed record KitchenTicketCreated(Guid TenantId, Guid TicketId, Guid OrderId, Guid LocationId) : ILocatedIntegrationEvent;

    [IntegrationEvent("kitchen", "KitchenPreparationStarted")]
    public sealed record KitchenPreparationStarted(Guid TenantId, Guid TicketId, Guid OrderId, Guid LocationId) : ILocatedIntegrationEvent;

    [IntegrationEvent("kitchen", "KitchenTicketReady")]
    public sealed record KitchenTicketReady(Guid TenantId, Guid TicketId, Guid OrderId, Guid LocationId, int ReadyItemCount, int TotalItemCount) : ILocatedIntegrationEvent;

    [IntegrationEvent("kitchen", "KitchenTicketCancelled")]
    public sealed record KitchenTicketCancelled(Guid TenantId, Guid TicketId, Guid OrderId, Guid LocationId) : ILocatedIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Inventory
{
    [IntegrationEvent("inventory", "LowStockReached")]
    public sealed record LowStockReached(
        Guid TenantId, Guid InventoryItemId, Guid LocationId, Guid IngredientId, decimal CurrentQuantity, decimal ReorderThreshold, string Unit) : ILocatedIntegrationEvent;

    [IntegrationEvent("inventory", "StockReceived")]
    public sealed record StockReceived(Guid TenantId, Guid InventoryItemId, Guid LocationId) : ILocatedIntegrationEvent;

    [IntegrationEvent("inventory", "WasteRecorded")]
    public sealed record WasteRecorded(Guid TenantId, Guid InventoryItemId, Guid LocationId) : ILocatedIntegrationEvent;

    [IntegrationEvent("inventory", "StockAdjusted")]
    public sealed record StockAdjusted(Guid TenantId, Guid InventoryItemId, Guid LocationId) : ILocatedIntegrationEvent;

    [IntegrationEvent("inventory", "StockWentNegative")]
    public sealed record StockWentNegative(Guid TenantId, Guid InventoryItemId, Guid LocationId) : ILocatedIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Promotions
{
    [IntegrationEvent("promotions", "PromotionApplied")]
    public sealed record PromotionApplied(Guid TenantId, Guid PromotionId, Guid OrderId, MoneyAmount Discount) : IIntegrationEvent;

    [IntegrationEvent("promotions", "PromotionUsageReverted")]
    public sealed record PromotionUsageReverted(Guid TenantId, Guid PromotionId, Guid OrderId) : IIntegrationEvent;

    [IntegrationEvent("promotions", "CouponRedeemed")]
    public sealed record CouponRedeemed(Guid TenantId, Guid CouponId, Guid PromotionId, Guid OrderId) : IIntegrationEvent;

    [IntegrationEvent("promotions", "CouponReleased")]
    public sealed record CouponReleased(Guid TenantId, Guid CouponId, Guid PromotionId, Guid OrderId) : IIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Loyalty
{
    [IntegrationEvent("loyalty", "LoyaltyAccountOpened")]
    public sealed record LoyaltyAccountOpened(Guid TenantId, Guid LoyaltyAccountId, Guid CustomerId) : IIntegrationEvent;

    [IntegrationEvent("loyalty", "PointsEarned")]
    public sealed record PointsEarned(Guid TenantId, Guid LoyaltyAccountId, [property: JsonConverter(typeof(FlexibleNumberConverter))] decimal Points) : IIntegrationEvent;

    [IntegrationEvent("loyalty", "PointsRedeemed")]
    public sealed record PointsRedeemed(Guid TenantId, Guid LoyaltyAccountId, [property: JsonConverter(typeof(FlexibleNumberConverter))] decimal Points) : IIntegrationEvent;

    [IntegrationEvent("loyalty", "PointsExpired")]
    public sealed record PointsExpired(Guid TenantId, Guid LoyaltyAccountId, [property: JsonConverter(typeof(FlexibleNumberConverter))] decimal Points) : IIntegrationEvent;

    [IntegrationEvent("loyalty", "PointsEarnReversed")]
    public sealed record PointsEarnReversed(Guid TenantId, Guid LoyaltyAccountId, [property: JsonConverter(typeof(FlexibleNumberConverter))] decimal Points) : IIntegrationEvent;

    [IntegrationEvent("loyalty", "PointsRedeemReversed")]
    public sealed record PointsRedeemReversed(Guid TenantId, Guid LoyaltyAccountId, [property: JsonConverter(typeof(FlexibleNumberConverter))] decimal Points) : IIntegrationEvent;

    [IntegrationEvent("loyalty", "RewardRedeemed")]
    public sealed record RewardRedeemed(
        Guid TenantId,
        Guid LoyaltyAccountId,
        [property: JsonConverter(typeof(FlexibleNumberConverter))] decimal Points,
        MoneyAmount DiscountValue,
        string DiscountReference,
        string Reference) : IIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Customers
{
    [IntegrationEvent("customers", "CustomerAnonymized")]
    public sealed record CustomerAnonymized(Guid TenantId, Guid CustomerId) : IIntegrationEvent;

    /// <summary>Doublon fusionné : son historique est à rattacher à <paramref name="MergedIntoId"/>.</summary>
    [IntegrationEvent("customers", "CustomerMerged")]
    public sealed record CustomerMerged(Guid TenantId, Guid CustomerId, Guid MergedIntoId) : IIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Billing
{
    [IntegrationEvent("billing", "SubscriptionTrialStarted")]
    public sealed record SubscriptionTrialStarted(Guid TenantId, Guid SubscriptionId) : IIntegrationEvent;

    [IntegrationEvent("billing", "SubscriptionActivated")]
    public sealed record SubscriptionActivated(Guid TenantId, Guid SubscriptionId) : IIntegrationEvent;

    [IntegrationEvent("billing", "SubscriptionSuspended")]
    public sealed record SubscriptionSuspended(Guid TenantId, Guid SubscriptionId, string Reason) : IIntegrationEvent;

    [IntegrationEvent("billing", "SubscriptionCancelled")]
    public sealed record SubscriptionCancelled(Guid TenantId, Guid SubscriptionId, string Reason) : IIntegrationEvent;

    /// <summary>L'essai ou l'offre limitée se termine bientôt (préavis, une fois par échéance).</summary>
    [IntegrationEvent("billing", "SubscriptionEndingSoon")]
    public sealed record SubscriptionEndingSoon(Guid TenantId, Guid SubscriptionId, Guid PlanId, DateTimeOffset EndsAtUtc, bool Complimentary) : IIntegrationEvent;

    /// <summary>La plateforme offre le plan ; <paramref name="UntilUtc"/> null : sans limite de durée.</summary>
    [IntegrationEvent("billing", "SubscriptionComplimentaryGranted")]
    public sealed record SubscriptionComplimentaryGranted(Guid TenantId, Guid SubscriptionId, Guid PlanId, DateTimeOffset? UntilUtc, int? RenewalMonths)
        : IIntegrationEvent;

    [IntegrationEvent("billing", "SubscriptionComplimentaryRenewed")]
    public sealed record SubscriptionComplimentaryRenewed(Guid TenantId, Guid SubscriptionId, Guid PlanId, DateTimeOffset UntilUtc) : IIntegrationEvent;

    [IntegrationEvent("billing", "SubscriptionComplimentaryEnded")]
    public sealed record SubscriptionComplimentaryEnded(Guid TenantId, Guid SubscriptionId, Guid? PlanId) : IIntegrationEvent;

    /// <summary>La plateforme offre une option ; <paramref name="Then"/> : <c>Cancel</c> ou <c>Bill</c> à la fin de l'offre.</summary>
    [IntegrationEvent("billing", "TenantAddOnComplimentaryGranted")]
    public sealed record TenantAddOnComplimentaryGranted(
        Guid TenantId, Guid TenantAddOnId, Guid PlanId, DateTimeOffset? UntilUtc, int? RenewalMonths, string Then) : IIntegrationEvent;

    [IntegrationEvent("billing", "TenantAddOnComplimentaryRenewed")]
    public sealed record TenantAddOnComplimentaryRenewed(Guid TenantId, Guid TenantAddOnId, Guid PlanId, DateTimeOffset UntilUtc) : IIntegrationEvent;

    [IntegrationEvent("billing", "TenantAddOnComplimentaryEnded")]
    public sealed record TenantAddOnComplimentaryEnded(Guid TenantId, Guid TenantAddOnId, Guid PlanId, string Then) : IIntegrationEvent;

    [IntegrationEvent("billing", "Subscription*")]
    public sealed record SubscriptionEvent(Guid TenantId, Guid SubscriptionId) : IIntegrationEvent;

    /// <summary>Une option d'un restaurant a été souscrite, modifiée ou résiliée : le montant de son abonnement change.</summary>
    [IntegrationEvent("billing", "TenantAddOn*")]
    public sealed record TenantAddOnEvent(Guid TenantId) : IIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Tenants
{
    [IntegrationEvent("tenants", "Tenant*")]
    public sealed record TenantEvent(Guid TenantId) : IIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Integrations
{
    [IntegrationEvent("integrations", "WebhookEndpointAutoDisabled")]
    public sealed record WebhookEndpointAutoDisabled(Guid TenantId, Guid EndpointId, string RedactedUrl, int ConsecutiveDeadLetters) : IIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Reporting
{
    [IntegrationEvent("reporting", "ExportCompleted")]
    public sealed record ExportCompleted(Guid TenantId, Guid ExportJobId, Guid FileId, Guid? RequestedBy) : IIntegrationEvent;

    [IntegrationEvent("reporting", "ExportExpired")]
    public sealed record ExportExpired(Guid TenantId, Guid ExportJobId, Guid FileId) : IIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Reviews
{
    [IntegrationEvent("reviews", "FeedbackSubmitted")]
    public sealed record FeedbackSubmitted(Guid TenantId, Guid FeedbackId, Guid LocationId, int Rating) : ILocatedIntegrationEvent;
}

namespace SaasRestaurant.Contracts.Staff
{
    [IntegrationEvent("staff", "StaffInvited")]
    public sealed record StaffInvited(Guid TenantId, Guid StaffMemberId, Guid UserId) : IIntegrationEvent;

    [IntegrationEvent("staff", "StaffInvitationResent")]
    public sealed record StaffInvitationResent(Guid TenantId, Guid StaffMemberId, Guid UserId) : IIntegrationEvent;

    [IntegrationEvent("staff", "StaffOwnerCreated")]
    public sealed record StaffOwnerCreated(Guid TenantId, Guid StaffMemberId, Guid UserId) : IIntegrationEvent;

    [IntegrationEvent("staff", "ScheduleWeekPublished")]
    public sealed record ScheduleWeekPublished(Guid TenantId, Guid WeekId, Guid LocationId, int Version, IReadOnlyList<Guid> AffectedMembers)
        : ILocatedIntegrationEvent;

    [IntegrationEvent("staff", "TimeOffRequested")]
    public sealed record TimeOffRequested(Guid TenantId, Guid RequestId, Guid StaffMemberId) : IIntegrationEvent;

    [IntegrationEvent("staff", "TimeOffDecided")]
    public sealed record TimeOffDecided(Guid TenantId, Guid RequestId, Guid StaffMemberId, string Status) : IIntegrationEvent;

    [IntegrationEvent("staff", "ShiftSwapRequested")]
    public sealed record ShiftSwapRequested(Guid TenantId, Guid RequestId, Guid LocationId, Guid FromMemberId, Guid ToMemberId) : ILocatedIntegrationEvent;

    [IntegrationEvent("staff", "ShiftSwapAccepted")]
    public sealed record ShiftSwapAccepted(Guid TenantId, Guid RequestId, Guid LocationId) : ILocatedIntegrationEvent;

    [IntegrationEvent("staff", "ShiftSwapDecided")]
    public sealed record ShiftSwapDecided(Guid TenantId, Guid RequestId, Guid LocationId, Guid FromMemberId, Guid ToMemberId, string Status)
        : ILocatedIntegrationEvent;
}
