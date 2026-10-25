using System.Collections.Frozen;

namespace Notifications.Domain;

/// <summary>
/// Le catalogue des types de notification et de leurs variables autorisées. C'est une décision du produit (un type naît
/// avec l'événement qui le déclenche), pas une donnée saisie : un template ne peut exister que pour un type connu ici, et
/// n'utiliser que les variables de ce type. Clés en minuscules pointées (<c>order.confirmed</c> pour ORDER_CONFIRMED).
/// </summary>
public static class NotificationTypes
{
    // Groupes de variables, recomposés par type. Toujours le minimum de données personnelles.
    private static readonly string[] Restaurant = ["restaurant.name", "location.name"];
    private static readonly string[] Customer = ["customer.firstName"];
    private static readonly string[] Order = ["order.number", "order.total"];
    private static readonly string[] Reservation = ["reservation.date", "reservation.time", "reservation.guestCount"];
    private static readonly string[] Payment = ["payment.amount", "payment.method"];
    private static readonly string[] Action = ["user.firstName", "action.url", "action.expiresInMinutes"];
    private static readonly string[] Campaign = ["campaign.title", "campaign.url", UnsubscribeUrl];

    /// <summary>Lien de désinscription : OBLIGATOIRE dans un email marketing.</summary>
    public const string UnsubscribeUrl = "unsubscribe.url";

    // Transactionnel : client.
    public static readonly NotificationTypeDefinition OrderConfirmed = Transactional("order.confirmed", NotificationPriority.Normal, Restaurant, Customer, Order);
    public static readonly NotificationTypeDefinition OrderReady = Transactional("order.ready", NotificationPriority.High, Restaurant, Customer, Order);
    public static readonly NotificationTypeDefinition OrderCancelled = Transactional("order.cancelled", NotificationPriority.High, Restaurant, Customer, Order);

    /// <summary>Reçu d'une commande, envoyé par l'équipe : lien signé vers la page du reçu, jamais le détail des lignes.</summary>
    public static readonly NotificationTypeDefinition OrderReceipt =
        Transactional("order.receipt", NotificationPriority.Normal, Restaurant, Customer, Order, ["receipt.url"]);
    public static readonly NotificationTypeDefinition ReservationConfirmed = Transactional("reservation.confirmed", NotificationPriority.Normal, Restaurant, Customer, Reservation);
    public static readonly NotificationTypeDefinition ReservationReminder = Transactional("reservation.reminder", NotificationPriority.Normal, Restaurant, Customer, Reservation);
    public static readonly NotificationTypeDefinition ReservationCancelled = Transactional("reservation.cancelled", NotificationPriority.Normal, Restaurant, Customer, Reservation);
    /// <summary>Acompte de réservation à payer : lien signé vers la page de la réservation, montant et échéance.</summary>
    public static readonly NotificationTypeDefinition ReservationDepositRequest =
        Transactional("reservation.deposit_request", NotificationPriority.High, Restaurant, Customer, Reservation, ["deposit.amount", "deposit.dueTime", "reservation.url"]);

    /// <summary>Liste d'attente : une table s'est libérée, à accepter avant l'heure donnée par le lien signé.</summary>
    public static readonly NotificationTypeDefinition WaitlistOffer =
        Transactional("waitlist.offer", NotificationPriority.Critical, Restaurant, Customer, Reservation, ["offer.url", "offer.expiresTime"]);

    /// <summary>File d'attente à la porte : la table du client est prête.</summary>
    public static readonly NotificationTypeDefinition WaitlistTableReady =
        Transactional("waitlist.table_ready", NotificationPriority.Critical, Restaurant, Customer);

    public static readonly NotificationTypeDefinition PaymentSucceeded = Transactional("payment.succeeded", NotificationPriority.Normal, Restaurant, Customer, Order, Payment);
    public static readonly NotificationTypeDefinition PaymentFailed = Transactional("payment.failed", NotificationPriority.High, Restaurant, Customer, Order, Payment);
    public static readonly NotificationTypeDefinition RefundSucceeded = Transactional("refund.succeeded", NotificationPriority.Normal, Restaurant, Customer, Order, ["refund.amount"]);
    public static readonly NotificationTypeDefinition LoyaltyRewardEarned = Transactional("loyalty.reward_earned", NotificationPriority.Normal, Restaurant, Customer, ["loyalty.points", "loyalty.reward"]);

    // Transactionnel : comptes et sécurité (souvent de niveau plateforme, sans restaurant).
    public static readonly NotificationTypeDefinition AccountWelcome = Transactional("account.welcome", NotificationPriority.Normal, Action);
    public static readonly NotificationTypeDefinition AccountVerifyEmail = Essential("account.verify_email", NotificationPriority.High, Action);
    public static readonly NotificationTypeDefinition AccountPasswordReset = Essential("account.password_reset", NotificationPriority.Critical, Action);
    public static readonly NotificationTypeDefinition SecurityOtp = Essential("security.otp", NotificationPriority.Critical, ["otp.code", "otp.expiresInMinutes"]);

    // Transactionnel : équipe du restaurant (in-app surtout).
    public static readonly NotificationTypeDefinition StaffNewReservation = Transactional("staff.new_reservation", NotificationPriority.Normal, Restaurant, Reservation);
    public static readonly NotificationTypeDefinition StaffOrderCancelled = Transactional("staff.order_cancelled", NotificationPriority.High, Restaurant, Order);
    public static readonly NotificationTypeDefinition StaffPaymentFailed = Transactional("staff.payment_failed", NotificationPriority.High, Restaurant, Order, Payment);
    public static readonly NotificationTypeDefinition StaffLowStock = Transactional("staff.low_stock", NotificationPriority.Normal, Restaurant, ["stock.ingredient", "stock.quantity"]);
    public static readonly NotificationTypeDefinition StaffNewReview = Transactional("staff.new_review", NotificationPriority.Low, Restaurant, ["review.rating"]);

    /// <summary>Un rapport (export ou envoi planifié) est prêt : lien vers la route de téléchargement, jamais le fichier.</summary>
    public static readonly NotificationTypeDefinition StaffReportReady =
        Transactional("staff.report_ready", NotificationPriority.Normal, Restaurant, ["report.name", "report.period", "report.url"]);

    /// <summary>
    /// Un endpoint de webhook a été désactivé par le système après trop de livraisons abandonnées : URL MASQUÉE (schéma,
    /// hôte, port), jamais le chemin ni la requête.
    /// </summary>
    public static readonly NotificationTypeDefinition StaffWebhookDisabled =
        Transactional("staff.webhook_disabled", NotificationPriority.High, Restaurant, ["webhook.url", "webhook.failures"]);

    /// <summary>Planning publié : envoyé au membre dont les créneaux de la semaine ont changé (aucun horaire d'un collègue).</summary>
    public static readonly NotificationTypeDefinition StaffSchedulePublished =
        Transactional("staff.schedule_published", NotificationPriority.Normal, Restaurant, ["schedule.week", "schedule.shifts", "schedule.hours", "schedule.url"]);

    /// <summary>Un membre demande une absence : aux gérants du planning.</summary>
    public static readonly NotificationTypeDefinition StaffTimeOffRequested =
        Transactional("staff.time_off_requested", NotificationPriority.Low, Restaurant, ["staff.name", "timeoff.period"]);

    /// <summary>Réponse à une demande d'absence : au membre.</summary>
    public static readonly NotificationTypeDefinition StaffTimeOffDecided =
        Transactional("staff.time_off_decided", NotificationPriority.Normal, Restaurant, ["timeoff.period", "timeoff.status"]);

    /// <summary>Un remplacement avance : au collègue sollicité, aux gérants, puis aux deux membres.</summary>
    public static readonly NotificationTypeDefinition StaffShiftSwap =
        Transactional("staff.shift_swap", NotificationPriority.Normal, Restaurant, ["staff.name", "shift.date", "shift.time", "swap.status"]);

    /// <summary>L'essai ou l'offre de la plateforme se termine bientôt : aux gérants de l'abonnement (aucun montant).</summary>
    public static readonly NotificationTypeDefinition StaffSubscriptionEnding =
        Transactional("staff.subscription_ending", NotificationPriority.High, Restaurant, ["subscription.plan", "subscription.endDate", "subscription.reason", "subscription.url"]);

    /// <summary>Offre de la plateforme (plan ou option) accordée, renouvelée ou terminée : aux gérants de l'abonnement (aucun montant).</summary>
    public static readonly NotificationTypeDefinition StaffSubscriptionOffer =
        Transactional("staff.subscription_offer", NotificationPriority.Normal, Restaurant, ["offer.subject", "offer.detail", "subscription.url"]);

    // Marketing : consentement, opt-out et fréquence vérifiés avant tout envoi.
    public static readonly NotificationTypeDefinition PromotionalCampaign = Marketing("marketing.promotional_campaign", Restaurant, Customer, Campaign);
    public static readonly NotificationTypeDefinition NewMenu = Marketing("marketing.new_menu", Restaurant, Customer, Campaign);
    public static readonly NotificationTypeDefinition LoyaltyCampaign = Marketing("marketing.loyalty_campaign", Restaurant, Customer, Campaign, ["loyalty.points"]);
    public static readonly NotificationTypeDefinition CustomerReactivation = Marketing("marketing.customer_reactivation", Restaurant, Customer, Campaign);
    public static readonly NotificationTypeDefinition SpecialEvent = Marketing("marketing.special_event", Restaurant, Customer, Campaign, ["event.date"]);

    private static readonly FrozenDictionary<TemplateKey, NotificationTypeDefinition> ByKey = new[]
    {
        OrderConfirmed, OrderReady, OrderCancelled, OrderReceipt,
        ReservationConfirmed, ReservationReminder, ReservationCancelled, ReservationDepositRequest, WaitlistOffer, WaitlistTableReady,
        PaymentSucceeded, PaymentFailed, RefundSucceeded, LoyaltyRewardEarned,
        AccountWelcome, AccountVerifyEmail, AccountPasswordReset, SecurityOtp,
        StaffNewReservation, StaffOrderCancelled, StaffPaymentFailed, StaffLowStock, StaffNewReview, StaffReportReady, StaffWebhookDisabled,
        StaffSchedulePublished, StaffTimeOffRequested, StaffTimeOffDecided, StaffShiftSwap, StaffSubscriptionEnding, StaffSubscriptionOffer,
        PromotionalCampaign, NewMenu, LoyaltyCampaign, CustomerReactivation, SpecialEvent,
    }.ToFrozenDictionary(definition => definition.Key);

    /// <summary>Tous les types connus, par clé.</summary>
    public static IReadOnlyCollection<NotificationTypeDefinition> All => ByKey.Values;

    public static NotificationTypeDefinition? Find(TemplateKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return ByKey.GetValueOrDefault(key);
    }

    /// <summary>Lève <c>notifications.template.type_unknown</c> pour une clé hors catalogue.</summary>
    public static NotificationTypeDefinition Require(TemplateKey key) =>
        Find(key) ?? throw new DomainException(TemplateErrors.TypeUnknown);

    private static NotificationTypeDefinition Transactional(string key, NotificationPriority priority, params string[][] variables) =>
        new(TemplateKey.Create(key), NotificationPurpose.Transactional, priority, variables.SelectMany(group => group));

    private static NotificationTypeDefinition Essential(string key, NotificationPriority priority, params string[][] variables) =>
        new(TemplateKey.Create(key), NotificationPurpose.Transactional, priority, variables.SelectMany(group => group), isEssential: true);

    private static NotificationTypeDefinition Marketing(string key, params string[][] variables) =>
        new(TemplateKey.Create(key), NotificationPurpose.Marketing, NotificationPriority.Low, variables.SelectMany(group => group));
}
