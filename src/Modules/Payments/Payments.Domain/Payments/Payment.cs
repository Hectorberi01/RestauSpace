namespace Payments.Domain;

/// <summary>
/// Vérité métier d'un paiement pour une commande (une commande peut en avoir plusieurs : addition partagée), quel que
/// soit le prestataire. Le domaine n'appelle jamais le prestataire : il enregistre des résultats prestataire validés,
/// chacun rattaché à une tentative par sa référence prestataire, et absorbe le rejeu exact d'un résultat (webhook
/// dupliqué) sans aucun effet.
/// </summary>
public sealed class Payment : AggregateRoot<PaymentId>, ITenantOwned
{
    public const int MaxCancellationReasonLength = 500;

    /// <summary>Tentatives prestataire max. par paiement. Au-delà, il est annulé et un nouveau est démarré.</summary>
    public const int MaxAttempts = 20;

    /// <summary>Remboursements qu'un paiement peut contenir, échoués compris (ils restent comme historique).</summary>
    public const int MaxRefunds = 100;

    private readonly List<PaymentAttempt> _attempts = [];
    private readonly List<Refund> _refunds = [];

    // Pas readonly : l'ORM le pose au chargement (jsonb). Posé une fois, à la création du paiement.
    private List<PaymentItemShare> _coveredItems = [];

    private Payment(
        PaymentId id,
        TenantId tenantId,
        OrderId orderId,
        LocationId locationId,
        Money amount,
        PaymentProviderCode provider,
        PaymentMethodType method,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        OrderId = orderId;
        LocationId = locationId;
        Amount = amount;
        Provider = provider;
        Method = method;
        Status = PaymentStatus.Pending;
        CapturedAmount = Money.Zero(amount.Currency);
        CreatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private Payment()
    {
        Amount = null!;
        Provider = null!;
        CapturedAmount = null!;
    }

    public TenantId TenantId { get; private set; }

    /// <summary>
    /// La commande réglée. Pour un acompte pas encore déduit d'une addition, l'id de sa réservation (même espace
    /// d'identifiants, jamais celui d'une commande) : aucune commande ne le compte donc dans son règlement.
    /// </summary>
    public OrderId OrderId { get; private set; }

    public PaymentPurpose Purpose { get; private set; } = PaymentPurpose.Order;

    /// <summary>La réservation garantie par un acompte ; null pour un paiement de commande.</summary>
    public Guid? ReservationId { get; private set; }

    /// <summary>Quand l'acompte a été déduit d'une addition.</summary>
    public DateTimeOffset? AppliedAtUtc { get; private set; }

    /// <summary>Un acompte encore rattaché à sa réservation (pas déduit d'une addition).</summary>
    public bool IsUnappliedDeposit => Purpose == PaymentPurpose.ReservationDeposit && AppliedAtUtc is null;

    /// <summary>Établissement de la commande (même tenant, garanti par <see cref="OrderReference"/>).</summary>
    public LocationId LocationId { get; private set; }

    /// <summary>Montant demandé au payeur. Fixé à la création.</summary>
    public Money Amount { get; private set; }

    public Currency Currency => Amount.Currency;

    public PaymentStatus Status { get; private set; }

    public PaymentProviderCode Provider { get; private set; }

    /// <summary>Moyen utilisé chez ce prestataire (carte, mobile money, espèces...). Fixé à la création.</summary>
    public PaymentMethodType Method { get; private set; }

    /// <summary>
    /// Montant réellement prélevé par le prestataire. Zéro avant capture ; jamais au-dessus de <see cref="Amount"/>, à une
    /// exception près : une capture reçue après annulation (<see cref="HasUnexpectedCapture"/>) est enregistrée pour ce
    /// que le prestataire a vraiment pris, même plus que ce qui était demandé.
    /// </summary>
    public Money CapturedAmount { get; private set; }

    /// <summary>
    /// Vrai quand <see cref="CapturedAmount"/> a été prélevé par le prestataire après l'annulation du paiement
    /// (<see cref="RegisterLateCapture"/>). L'argent est remboursable comme tout montant capturé mais ne paie rien.
    /// </summary>
    public bool HasUnexpectedCapture => _attempts.Any(attempt => attempt.WasCapturedAfterCancellation);

    /// <summary>Somme des remboursements réussis.</summary>
    public Money RefundedAmount => SumRefunds(RefundStatus.Succeeded);

    /// <summary>Somme des remboursements demandés et encore en attente de la réponse du prestataire.</summary>
    public Money PendingRefundAmount => SumRefunds(RefundStatus.Requested);

    /// <summary>
    /// Ce qu'un nouveau remboursement peut encore demander : capturé - remboursé - en attente. Les remboursements en
    /// attente sont réservés, pour que deux remboursements en cours ne dépassent jamais ensemble le montant capturé.
    /// </summary>
    public Money RefundableAmount => CapturedAmount - RefundedAmount - PendingRefundAmount;

    /// <summary>
    /// Argent que ce paiement détient en ce moment pour sa commande : capturé moins remboursements réussis. C'est la vue
    /// comptable ; pour décider si une commande est payée, utiliser <see cref="SettledAmount"/>, qui écarte aussi les
    /// remboursements en cours. Zéro pour une capture reçue après annulation : cet argent est à rendre.
    /// </summary>
    public Money NetPaidAmount => HasUnexpectedCapture ? Money.Zero(Currency) : CapturedAmount - RefundedAmount;

    /// <summary>
    /// Ce sur quoi l'on peut compter de ce paiement pour régler sa commande : capturé moins remboursements réussis moins
    /// les remboursements encore en attente de la réponse du prestataire, puisque cet argent est en train de repartir.
    /// C'est le terme par paiement du règlement de la commande (sa somme sur tous les paiements de la commande). Un
    /// remboursement qui échoue compte de nouveau. Zéro pour une capture reçue après annulation : elle ne règle rien.
    /// </summary>
    public Money SettledAmount => HasUnexpectedCapture ? Money.Zero(Currency) : RefundableAmount;

    public IReadOnlyCollection<PaymentAttempt> Attempts => _attempts.AsReadOnly();

    public IReadOnlyCollection<Refund> Refunds => _refunds.AsReadOnly();

    /// <summary>
    /// Lignes de la commande que ce paiement règle (addition partagée par article) ; vide pour un paiement d'un montant
    /// libre ou de tout le reste à payer.
    /// </summary>
    public IReadOnlyList<PaymentItemShare> CoveredItems => _coveredItems.AsReadOnly();

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Autorisation de la tentative en cours ; remis à null quand un nouvel essai rouvre le paiement.</summary>
    public DateTimeOffset? AuthorizedAtUtc { get; private set; }

    public DateTimeOffset? CapturedAtUtc { get; private set; }

    /// <summary>Passage en échec ; remis à null quand un nouvel essai rouvre le paiement (l'historique reste sur les tentatives).</summary>
    public DateTimeOffset? FailedAtUtc { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>
    /// Démarre un paiement en ligne pour tout ou partie de ce qui reste à payer sur une commande. Plusieurs paiements
    /// peuvent partager une commande (addition partagée) : chacun est borné ici par <see cref="OrderReference.AmountDue"/>,
    /// leur somme est protégée par l'Application. La <paramref name="configuration"/> du restaurant doit être celle du
    /// même tenant, active, prête, accepter ce moyen dans l'établissement de la commande et pour ce montant.
    /// </summary>
    public static Payment Create(
        RestaurantPaymentConfiguration configuration, PaymentMethodType method, OrderReference order, Money amount, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(configuration.ProviderType != PaymentProviderType.Online, PaymentErrors.OnlineNeedsOnlineProvider);
        return Start(configuration, method, order, amount, clock.UtcNow);
    }

    /// <summary>
    /// Enregistre de l'argent déjà reçu hors de tout flux prestataire (espèces, TPE externe) : le paiement est créé et
    /// capturé d'un coup pour tout son montant, via une tentative réussie portant la référence fournie par l'opérateur
    /// (ticket ou référence de caisse). Il est ensuite remboursable comme tout paiement capturé. Mêmes contrôles de la
    /// configuration que <see cref="Create"/>, avec un prestataire sur place.
    /// </summary>
    public static Payment RecordOffline(
        RestaurantPaymentConfiguration configuration,
        PaymentMethodType method,
        OrderReference order,
        Money amount,
        ProviderReference operatorReference,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(operatorReference);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(configuration.ProviderType != PaymentProviderType.OnSite, PaymentErrors.OfflineNeedsOnSiteProvider);

        // Une opération, un instant : création, tentative et capture portent la même heure.
        var now = clock.UtcNow;
        var payment = Start(configuration, method, order, amount, now);
        payment.OpenAttempt(operatorReference, now);
        payment.RecordCapture(operatorReference, amount, now);
        return payment;
    }

    /// <summary>Acompte d'une réservation payé en ligne (mêmes contrôles que <see cref="Create"/>).</summary>
    public static Payment CreateDeposit(
        RestaurantPaymentConfiguration configuration, PaymentMethodType method, DepositReference deposit, Money amount, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(deposit);
        DomainException.ThrowIf(method == PaymentMethodType.GiftCard, PaymentErrors.DepositMethodNotAllowed);
        var payment = Create(configuration, method, deposit.AsHolder(), amount, clock);
        payment.Purpose = PaymentPurpose.ReservationDeposit;
        payment.ReservationId = deposit.ReservationId;
        return payment;
    }

    /// <summary>Acompte d'une réservation reçu au comptoir ou par téléphone (mêmes contrôles que <see cref="RecordOffline"/>).</summary>
    public static Payment RecordOfflineDeposit(
        RestaurantPaymentConfiguration configuration,
        PaymentMethodType method,
        DepositReference deposit,
        Money amount,
        ProviderReference operatorReference,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(deposit);
        DomainException.ThrowIf(method == PaymentMethodType.GiftCard, PaymentErrors.DepositMethodNotAllowed);
        var payment = RecordOffline(configuration, method, deposit.AsHolder(), amount, operatorReference, clock);
        payment.Purpose = PaymentPurpose.ReservationDeposit;
        payment.ReservationId = deposit.ReservationId;
        return payment;
    }

    /// <summary>
    /// Déduit l'acompte encaissé de l'addition du repas : le paiement règle désormais <paramref name="order"/> (même
    /// établissement, même devise), pour ce qu'il détient encore. Refusé s'il dépasse le reste à payer : la différence se
    /// rembourse d'abord.
    /// </summary>
    public void ApplyToOrder(OrderReference order, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Purpose != PaymentPurpose.ReservationDeposit, PaymentErrors.NotADeposit);
        DomainException.ThrowIf(AppliedAtUtc is not null, PaymentErrors.DepositAlreadyApplied);
        DomainException.ThrowIf(!SettledAmount.IsPositive, PaymentErrors.DepositNotCaptured);
        this.EnsureSameTenant(order.TenantId);
        DomainException.ThrowIf(order.LocationId != LocationId, PaymentErrors.DepositOtherLocation);
        DomainException.ThrowIf(order.AmountDue.Currency != Currency, PaymentErrors.CurrencyMismatch);
        DomainException.ThrowIf(SettledAmount > order.AmountDue, PaymentErrors.DepositAboveAmountDue);

        var now = clock.UtcNow;
        OrderId = order.OrderId;
        AppliedAtUtc = now;
        Raise(new DepositApplied(TenantId, Id, ReservationId!.Value, OrderId, SettledAmount, now));
    }

    /// <summary>Note les lignes de la commande réglées par ce paiement (une fois, juste après sa création).</summary>
    public void Cover(IReadOnlyCollection<PaymentItemShare> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        DomainException.ThrowIf(_coveredItems.Count > 0, PaymentErrors.ItemsAlreadyCovered);
        DomainException.ThrowIf(items.Count > PaymentItemShare.MaxItems, PaymentErrors.TooManyItems);
        DomainException.ThrowIf(items.Select(item => item.ItemId).Distinct().Count() != items.Count, PaymentErrors.ItemDuplicate);
        _coveredItems = [.. items];
    }

    /// <summary>
    /// Ouvre une nouvelle tentative chez le prestataire. Autorisé sur un paiement Pending, ou Failed (nouvel essai : le
    /// paiement repasse Pending). Une seule tentative peut être ouverte à la fois : deux tentatives ne peuvent jamais
    /// réussir toutes les deux.
    /// </summary>
    public PaymentAttemptId RegisterAttempt(ProviderReference providerReference, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(providerReference);
        ArgumentNullException.ThrowIfNull(clock);
        return OpenAttempt(providerReference, clock.UtcNow);
    }

    private PaymentAttemptId OpenAttempt(ProviderReference providerReference, DateTimeOffset now)
    {
        DomainException.ThrowIf(Status == PaymentStatus.Cancelled, PaymentErrors.AlreadyCancelled);
        DomainException.ThrowIf(
            Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded,
            PaymentErrors.AttemptNotAllowed);
        DomainException.ThrowIf(
            _attempts.Any(existing => existing.ProviderReference == providerReference),
            PaymentErrors.AttemptReferenceDuplicate);
        // Couvre aussi le paiement Authorized : sa tentative autorisée est encore ouverte.
        DomainException.ThrowIf(_attempts.Any(existing => existing.IsOpen), PaymentErrors.AttemptInProgress);
        DomainException.ThrowIf(_attempts.Count >= MaxAttempts, PaymentErrors.TooManyAttempts);

        var attempt = PaymentAttempt.Create(providerReference, now);
        _attempts.Add(attempt);
        Status = PaymentStatus.Pending;
        AuthorizedAtUtc = null;
        FailedAtUtc = null;
        Raise(new PaymentAttemptRegistered(TenantId, Id, OrderId, attempt.Id, providerReference, now));
        return attempt.Id;
    }

    /// <summary>Résultat prestataire : fonds bloqués pour cette tentative. Les rejeux, même après capture, ne changent rien.</summary>
    public void Authorize(ProviderReference providerReference, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var attempt = FindAttempt(providerReference);
        DomainException.ThrowIf(attempt.Status == PaymentAttemptStatus.Failed, PaymentErrors.AttemptAlreadyFailed);
        if (attempt.Status == PaymentAttemptStatus.Succeeded || attempt.IsAuthorized)
        {
            return;
        }

        var now = clock.UtcNow;
        attempt.MarkAuthorized(now);
        Status = PaymentStatus.Authorized;
        AuthorizedAtUtc = now;
        Raise(new PaymentAuthorized(TenantId, Id, OrderId, attempt.Id, attempt.ProviderReference, Amount, now));
    }

    /// <summary>
    /// Résultat prestataire : de l'argent a été prélevé par cette tentative, avec ou sans autorisation préalable. Le
    /// montant capturé peut être inférieur au montant demandé, jamais supérieur. Rejouer la même capture ne change rien.
    /// </summary>
    public void Capture(ProviderReference providerReference, Money capturedAmount, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(capturedAmount);
        ArgumentNullException.ThrowIfNull(clock);
        RecordCapture(providerReference, capturedAmount, clock.UtcNow);
    }

    private void RecordCapture(ProviderReference providerReference, Money capturedAmount, DateTimeOffset now)
    {
        DomainException.ThrowIf(capturedAmount.Currency != Currency, PaymentErrors.CurrencyMismatch);
        var attempt = FindAttempt(providerReference);
        DomainException.ThrowIf(attempt.Status == PaymentAttemptStatus.Failed, PaymentErrors.AttemptAlreadyFailed);
        if (attempt.Status == PaymentAttemptStatus.Succeeded)
        {
            DomainException.ThrowIf(capturedAmount != CapturedAmount, PaymentErrors.CaptureConflict);
            return;
        }

        DomainException.ThrowIf(!capturedAmount.IsPositive, PaymentErrors.CaptureAmountNotPositive);
        DomainException.ThrowIf(capturedAmount > Amount, PaymentErrors.CaptureExceedsAmount);

        attempt.Succeed(now);
        CapturedAmount = capturedAmount;
        CapturedAtUtc = now;
        Status = PaymentStatus.Captured;
        Raise(new PaymentCaptured(TenantId, Id, OrderId, attempt.Id, attempt.ProviderReference, capturedAmount, now));
    }

    /// <summary>
    /// Résultat prestataire qui ne devrait pas exister : de l'argent a été prélevé pour une tentative de ce paiement
    /// alors qu'il avait été annulé. <see cref="Capture"/> continue de rejeter ce cas ; ceci enregistre le fait, pour que
    /// l'argent soit connu et puisse être remboursé via <see cref="RequestRefund"/>. Le paiement quitte Cancelled
    /// pour Captured avec <see cref="HasUnexpectedCapture"/> positionné, et garde sa date et son motif d'annulation.
    /// Le montant est ce que le prestataire a vraiment pris, même au-dessus de <see cref="Amount"/> : le refuser ne
    /// laisserait aucune trace d'argent à rendre. Rejouer la même capture tardive ne change rien.
    /// </summary>
    public void RegisterLateCapture(ProviderReference providerReference, Money capturedAmount, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(capturedAmount);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(capturedAmount.Currency != Currency, PaymentErrors.CurrencyMismatch);
        var attempt = FindAttempt(providerReference);
        if (HasUnexpectedCapture)
        {
            DomainException.ThrowIf(
                !attempt.WasCapturedAfterCancellation || capturedAmount != CapturedAmount,
                PaymentErrors.CaptureConflict);
            return;
        }

        DomainException.ThrowIf(Status != PaymentStatus.Cancelled, PaymentErrors.LateCaptureNotAllowed);
        DomainException.ThrowIf(!capturedAmount.IsPositive, PaymentErrors.CaptureAmountNotPositive);

        var now = clock.UtcNow;
        attempt.MarkCapturedAfterCancellation(now);
        CapturedAmount = capturedAmount;
        CapturedAtUtc = now;
        Status = PaymentStatus.Captured;
        Raise(new PaymentCapturedAfterCancellation(
            TenantId, Id, OrderId, attempt.Id, attempt.ProviderReference, capturedAmount, now));
    }

    /// <summary>
    /// Résultat prestataire : tentative échouée (refusée, autorisation expirée...), avec son code et, au plus, le message
    /// borné du prestataire. Les rejeux ne changent rien : le premier échec et son code restent.
    /// </summary>
    public void Fail(ProviderReference providerReference, PaymentFailureCode failureCode, IClock clock, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(failureCode);
        ArgumentNullException.ThrowIfNull(clock);
        var attempt = FindAttempt(providerReference);
        DomainException.ThrowIf(attempt.Status == PaymentAttemptStatus.Succeeded, PaymentErrors.AttemptAlreadySucceeded);
        if (attempt.Status == PaymentAttemptStatus.Failed)
        {
            // Un rejeu est absorbé quoi qu'il porte : l'échec et son premier code sont déjà enregistrés.
            return;
        }

        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        DomainException.ThrowIf(trimmed is { Length: > PaymentAttempt.MaxFailureReasonLength }, PaymentErrors.FailureReasonInvalid);

        var now = clock.UtcNow;
        attempt.Fail(failureCode, trimmed, now);
        Status = PaymentStatus.Failed;
        FailedAtUtc = now;
        Raise(new PaymentFailed(TenantId, Id, OrderId, attempt.Id, attempt.ProviderReference, failureCode, trimmed, now));
    }

    /// <summary>
    /// Abandonne le paiement avant tout prélèvement. La tentative ouverte, s'il y en a une, est close en échec pour qu'un
    /// succès tardif du prestataire soit rejeté par <see cref="Capture"/> au lieu de ranimer silencieusement le
    /// paiement ; une telle capture tardive est enregistrée avec <see cref="RegisterLateCapture"/>.
    /// </summary>
    public void Cancel(string reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == PaymentStatus.Cancelled, PaymentErrors.AlreadyCancelled);
        DomainException.ThrowIf(
            Status is not (PaymentStatus.Pending or PaymentStatus.Authorized or PaymentStatus.Failed),
            PaymentErrors.CancelNotAllowed);
        var trimmed = reason?.Trim();
        DomainException.ThrowIf(
            string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxCancellationReasonLength,
            PaymentErrors.CancellationReasonInvalid);

        var now = clock.UtcNow;
        var closedAttempt = _attempts.SingleOrDefault(attempt => attempt.IsOpen);
        closedAttempt?.Fail(PaymentFailureCode.PaymentCancelled, null, now);
        Status = PaymentStatus.Cancelled;
        CancelledAtUtc = now;
        CancellationReason = trimmed;
        Raise(new PaymentCancelled(
            TenantId, Id, OrderId, trimmed!, closedAttempt?.Id, closedAttempt?.ProviderReference, now));
    }

    /// <summary>Demande un remboursement et réserve son montant jusqu'à la réponse du prestataire.</summary>
    public RefundId RequestRefund(Money amount, string reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(
            Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded),
            PaymentErrors.RefundNotAllowed);
        DomainException.ThrowIf(amount.Currency != Currency, PaymentErrors.CurrencyMismatch);

        var now = clock.UtcNow;
        var refund = Refund.Request(amount, reason, now);
        DomainException.ThrowIf(amount > RefundableAmount, PaymentErrors.RefundExceedsRefundable);
        DomainException.ThrowIf(_refunds.Count >= MaxRefunds, PaymentErrors.TooManyRefunds);

        _refunds.Add(refund);
        Raise(new RefundRequested(TenantId, Id, OrderId, refund.Id, refund.Amount, refund.Reason, now));
        return refund.Id;
    }

    /// <summary>
    /// Enregistre la réponse finale du prestataire à un remboursement, avec sa référence chez lui s'il en donne une.
    /// Rejouer la même réponse ne change rien ; une autre issue, ou une autre référence, est refusée (pas de double
    /// traitement).
    /// </summary>
    public void RegisterRefund(RefundId refundId, RefundOutcome outcome, IClock clock, ProviderReference? providerReference = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!Enum.IsDefined(outcome), PaymentErrors.RefundOutcomeUnknown);
        var refund = _refunds.FirstOrDefault(existing => existing.Id == refundId);
        DomainException.ThrowIf(refund is null, PaymentErrors.RefundNotFound);

        var targetStatus = outcome == RefundOutcome.Succeeded ? RefundStatus.Succeeded : RefundStatus.Failed;
        if (refund!.Status != RefundStatus.Requested)
        {
            DomainException.ThrowIf(!refund.IsSameOutcome(targetStatus, providerReference), PaymentErrors.RefundOutcomeConflict);
            return;
        }

        DomainException.ThrowIf(
            providerReference is not null && _refunds.Any(existing => existing.ProviderReference == providerReference),
            PaymentErrors.RefundReferenceDuplicate);

        var now = clock.UtcNow;
        if (outcome == RefundOutcome.Failed)
        {
            refund.Fail(now, providerReference);
            Raise(new RefundRegistered(TenantId, Id, OrderId, refund.Id, refund.Amount, outcome, Status, providerReference, now));
            return;
        }

        refund.Succeed(now, providerReference);
        var refunded = RefundedAmount;
        var isFullRefund = refunded == CapturedAmount;
        Status = isFullRefund ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        Raise(new RefundRegistered(TenantId, Id, OrderId, refund.Id, refund.Amount, outcome, Status, providerReference, now));
        Raise(new RefundSucceeded(TenantId, Id, OrderId, refund.Id, refund.Amount, refunded, isFullRefund, providerReference, now));
    }

    private static Payment Start(
        RestaurantPaymentConfiguration configuration, PaymentMethodType method, OrderReference order, Money amount, DateTimeOffset now)
    {
        var tenantId = configuration.TenantId;
        Guard.NotEmpty(tenantId);
        Guard.NotEmpty(order.OrderId);
        Guard.NotEmpty(order.LocationId);
        // La commande, donc son établissement, et la configuration doivent appartenir au même restaurant.
        DomainException.ThrowIf(order.TenantId != tenantId, CommonErrors.TenantMismatch);
        DomainException.ThrowIf(amount.Currency != order.AmountDue.Currency, PaymentErrors.CurrencyMismatch);
        DomainException.ThrowIf(!amount.IsPositive, PaymentErrors.AmountNotPositive);
        DomainException.ThrowIf(amount > order.AmountDue, PaymentErrors.AmountExceedsAmountDue);
        configuration.EnsureCanTake(order.LocationId, method, amount);

        var payment = new Payment(PaymentId.New(), tenantId, order.OrderId, order.LocationId, amount, configuration.Provider, method, now);
        payment.Raise(new PaymentCreated(tenantId, payment.Id, order.OrderId, order.LocationId, amount, configuration.Provider, method, now));
        return payment;
    }

    private PaymentAttempt FindAttempt(ProviderReference providerReference)
    {
        ArgumentNullException.ThrowIfNull(providerReference);
        var attempt = _attempts.FirstOrDefault(existing => existing.ProviderReference == providerReference);
        DomainException.ThrowIf(attempt is null, PaymentErrors.AttemptNotFound);
        return attempt!;
    }

    private Money SumRefunds(RefundStatus status) =>
        _refunds.Where(refund => refund.Status == status)
            .Aggregate(Money.Zero(Currency), (sum, refund) => sum + refund.Amount);
}
