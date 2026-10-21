namespace Notifications.Domain;

/// <summary>
/// Un message à délivrer à UN destinataire, sur UN canal, par UN prestataire, et son cycle de vie (§ 10 du cahier).
/// Le contenu est déjà RENDU (<see cref="NotificationContent"/>) : le template retenu et les variables restent dans
/// l'Application, l'agrégat ne garde que ce qui part réellement.
/// Cycle : Pending (ou Scheduled) -> Processing -> Sent -> (PartiallyDelivered ->) Delivered ;
/// Processing -> Failed -> Pending (relance) ; Pending / Scheduled / Failed -> Cancelled. Delivered et Cancelled sont
/// terminaux. Chaque tentative est une <see cref="NotificationDelivery"/> ; une tentative en cours existe si et seulement
/// si l'état est Processing. Un échec signalé après l'acceptation (rebond) fait passer Sent -> Failed.
/// Appartient à un tenant, ou à la plateforme elle-même quand <see cref="TenantId"/> est null (vérification de compte,
/// réinitialisation de mot de passe, OTP : messages envoyés avant qu'un tenant existe) — d'où l'absence de
/// <c>ITenantOwned</c> ; le filtre par tenant est posé par le repository.
/// SÉCURITÉ : ni l'adresse, ni le contenu (qui peut citer un OTP), ni le message du prestataire ne sortent de
/// l'agrégat par un événement ; <see cref="Redact"/> les efface une fois l'envoi clos.
/// </summary>
public sealed class Notification : AggregateRoot<NotificationId>
{
    private readonly List<NotificationDelivery> _deliveries = [];

    private Notification(
        NotificationId id,
        TenantId? tenantId,
        LocationId? locationId,
        NotificationTypeDefinition type,
        RecipientId? recipientId,
        NotificationRecipient recipient,
        NotificationProviderCode providerCode,
        NotificationLanguage language,
        NotificationContent content,
        TemplateId? templateId,
        NotificationRetryPolicy retryPolicy,
        DateTimeOffset? scheduledAtUtc,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        TemplateKey = type.Key;
        Purpose = type.Purpose;
        Priority = type.Priority;
        RecipientId = recipientId;
        Recipient = recipient;
        ProviderCode = providerCode;
        Language = language;
        Content = content;
        TemplateId = templateId;
        RetryPolicy = retryPolicy;
        ScheduledAtUtc = scheduledAtUtc;
        Status = scheduledAtUtc is null ? NotificationStatus.Pending : NotificationStatus.Scheduled;
        NextAttemptAtUtc = scheduledAtUtc ?? createdAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private Notification()
    {
        TemplateKey = null!;
        Recipient = null!;
        ProviderCode = null!;
        Language = null!;
        Content = null!;
        RetryPolicy = null!;
    }

    /// <summary>Null pour une notification de niveau plateforme.</summary>
    public TenantId? TenantId { get; private set; }

    public bool IsPlatformLevel => TenantId is null;

    /// <summary>L'établissement concerné, vérifié comme appartenant au tenant à la création ; null s'il n'y en a pas.</summary>
    public LocationId? LocationId { get; private set; }

    /// <summary>Le type de notification (<c>order.confirmed</c>...), connu du catalogue <see cref="NotificationTypes"/>.</summary>
    public TemplateKey TemplateKey { get; private set; }

    public NotificationTypeDefinition Type => NotificationTypes.Require(TemplateKey);

    /// <summary>Recopiée du type à la création : l'historique ne change pas si le catalogue évolue.</summary>
    public NotificationPurpose Purpose { get; private set; }

    /// <summary>Recopiée du type à la création : ordonne la file d'envoi (Critical d'abord).</summary>
    public NotificationPriority Priority { get; private set; }

    /// <summary>QUI reçoit le message (client ou utilisateur) ; null pour un invité sans fiche.</summary>
    public RecipientId? RecipientId { get; private set; }

    /// <summary>Le client destinataire, quand <see cref="RecipientId"/> en désigne un.</summary>
    public CustomerId? CustomerId => RecipientId?.ToCustomerId();

    /// <summary>OÙ joindre le destinataire ; adresse vide une fois la notification expurgée.</summary>
    public NotificationRecipient Recipient { get; private set; }

    public NotificationChannel Channel => Recipient.Channel;

    /// <summary>
    /// Le prestataire retenu par l'Application (configuration du restaurant ou de la plateforme) ; celui de la
    /// dernière tentative quand une relance est passée par un prestataire de secours.
    /// </summary>
    public NotificationProviderCode ProviderCode { get; private set; }

    public NotificationLanguage Language { get; private set; }

    /// <summary>Sujet et corps rendus ; vides une fois la notification expurgée.</summary>
    public NotificationContent Content { get; private set; }

    /// <summary>Le template qui a produit le contenu (preuve de ce qui a été envoyé) ; null pour un message libre.</summary>
    public TemplateId? TemplateId { get; private set; }

    /// <summary>Fixée à la création : nombre de tentatives et backoff ne changent pas d'un appel à l'autre.</summary>
    public NotificationRetryPolicy RetryPolicy { get; private set; }

    public NotificationStatus Status { get; private set; }

    /// <summary>Les tentatives, dans l'ordre (<see cref="NotificationDelivery.AttemptNumber"/> 1, 2...).</summary>
    public IReadOnlyCollection<NotificationDelivery> Deliveries => _deliveries.AsReadOnly();

    public int AttemptCount => _deliveries.Count;

    /// <summary>Instant au plus tôt du prochain essai ; null pendant un essai, après un échec et une fois remis ou annulé.</summary>
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }

    /// <summary>Date d'envoi demandée ; null pour un envoi immédiat.</summary>
    public DateTimeOffset? ScheduledAtUtc { get; private set; }

    /// <summary>L'échec de la dernière tentative échouée ; son message brut est effacé avec la notification.</summary>
    public NotificationFailure? LastFailure { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? SentAtUtc { get; private set; }

    public DateTimeOffset? DeliveredAtUtc { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public DateTimeOffset? RedactedAtUtc { get; private set; }

    /// <summary>In-app : lue par son destinataire.</summary>
    public DateTimeOffset? ReadAtUtc { get; private set; }

    /// <summary>In-app : rangée par son destinataire (elle sort de sa boîte de réception).</summary>
    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    /// <summary>Tentatives accordées en plus de la politique par un retry manuel (dead letter, § 26).</summary>
    public int ManualRetryCount { get; private set; }

    /// <summary>État d'affichage d'une notification in-app ; null pour les autres canaux.</summary>
    public InAppState? InAppState => Recipient.Channel != NotificationChannel.InApp
        ? null
        : ArchivedAtUtc is not null ? Domain.InAppState.Archived : ReadAtUtc is not null ? Domain.InAppState.Read : Domain.InAppState.Unread;

    public bool IsAttemptInProgress => Status == NotificationStatus.Processing;

    /// <summary>Delivered ou Cancelled : plus rien ne change.</summary>
    public bool IsTerminal => Status is NotificationStatus.Delivered or NotificationStatus.Cancelled;

    public bool CanRetry => Status == NotificationStatus.Failed && !IsDeliveryAbandoned;

    /// <summary>
    /// En échec et plus jamais envoyée (« dead letter », § 26) : échec permanent, tentatives épuisées, ou contenu déjà
    /// effacé. Elle peut encore être annulée ; le retry manuel relève de l'étape 19.
    /// </summary>
    public bool IsDeliveryAbandoned =>
        Status == NotificationStatus.Failed
        && (LastFailure is { IsPermanent: true }
            || !RetryPolicy.AllowsAttemptAfter(_deliveries.Count - ManualRetryCount)
            || IsRedacted);

    public bool IsRedacted => RedactedAtUtc is not null;

    private NotificationDelivery? LastDelivery => _deliveries.Count > 0 ? _deliveries[^1] : null;

    private NotificationDelivery? CurrentDelivery => LastDelivery is { IsInProgress: true } delivery ? delivery : null;

    /// <summary>Crée une notification d'un tenant.</summary>
    /// <param name="recipientId">
    /// Le client ou l'utilisateur destinataire. Obligatoire et client pour un type marketing. Pour InApp, déduit de
    /// l'adresse (l'utilisateur) s'il est null, et sinon doit désigner ce même utilisateur.
    /// </param>
    /// <param name="provider">IN_APP si et seulement si le canal est InApp.</param>
    /// <param name="content">Rendu pour le canal du destinataire, sans variable restante.</param>
    /// <param name="location">
    /// L'établissement concerné, chargé sous isolation par tenant ; doit appartenir à <paramref name="tenantId"/>.
    /// </param>
    /// <param name="consent">
    /// Obligatoire pour un type marketing (accordé, pour ce client et pour le canal) ; ignoré sinon. Relu avant chaque
    /// tentative (<see cref="StartProcessing"/>).
    /// </param>
    /// <param name="scheduledAtUtc">Envoi différé, strictement dans le futur ; null pour un envoi immédiat.</param>
    /// <param name="retryPolicy"><see cref="NotificationRetryPolicy.Default"/> si null.</param>
    public static Notification Create(
        TenantId tenantId,
        NotificationTypeDefinition type,
        NotificationRecipient recipient,
        RecipientId? recipientId,
        NotificationProviderCode provider,
        NotificationLanguage language,
        NotificationContent content,
        IClock clock,
        LocationReference? location = null,
        ConsentProof? consent = null,
        DateTimeOffset? scheduledAtUtc = null,
        TemplateId? templateId = null,
        NotificationRetryPolicy? retryPolicy = null)
    {
        Guard.NotEmpty(tenantId);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(recipient);

        // L'établissement vient de l'Application, mais l'agrégat revérifie qu'il est bien du même tenant.
        DomainException.ThrowIf(location is not null && location.TenantId != tenantId, CommonErrors.TenantMismatch);

        var resolvedRecipientId = ResolveRecipientId(recipient, recipientId);
        if (type.IsMarketing)
        {
            EnsureMarketingConsent(resolvedRecipientId, recipient.Channel, consent);
        }

        return CreateCore(
            tenantId, location?.LocationId, type, recipient, resolvedRecipientId, provider, language, content,
            scheduledAtUtc, templateId, retryPolicy, clock);
    }

    /// <summary>
    /// Crée une notification de la plateforme elle-même, hors de tout tenant et de tout établissement. Jamais de
    /// marketing : un consentement n'existe qu'au sein d'un tenant.
    /// </summary>
    public static Notification CreateForPlatform(
        NotificationTypeDefinition type,
        NotificationRecipient recipient,
        RecipientId? recipientId,
        NotificationProviderCode provider,
        NotificationLanguage language,
        NotificationContent content,
        IClock clock,
        DateTimeOffset? scheduledAtUtc = null,
        TemplateId? templateId = null,
        NotificationRetryPolicy? retryPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(recipient);
        DomainException.ThrowIf(type.IsMarketing, NotificationErrors.PlatformMarketing);

        var resolvedRecipientId = ResolveRecipientId(recipient, recipientId);
        return CreateCore(
            null, null, type, recipient, resolvedRecipientId, provider, language, content,
            scheduledAtUtc, templateId, retryPolicy, clock);
    }

    /// <summary>
    /// Fixe (ou déplace) la date d'envoi d'une notification qui n'a encore jamais été tentée. Une relance a sa propre
    /// échéance (<see cref="ScheduleRetry"/>) et ne se reprogramme pas.
    /// </summary>
    public void Schedule(DateTimeOffset scheduledAtUtc, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotHandedOverOrCancelled();
        DomainException.ThrowIf(
            Status is not (NotificationStatus.Pending or NotificationStatus.Scheduled) || _deliveries.Count > 0,
            NotificationErrors.NotSchedulable);

        var now = clock.UtcNow;
        DomainException.ThrowIf(scheduledAtUtc <= now, NotificationErrors.ScheduleInPast);
        if (Status == NotificationStatus.Scheduled && ScheduledAtUtc == scheduledAtUtc)
        {
            return;
        }

        Status = NotificationStatus.Scheduled;
        ScheduledAtUtc = scheduledAtUtc;
        NextAttemptAtUtc = scheduledAtUtc;

        Raise(new NotificationScheduled(TenantId, Id, scheduledAtUtc, now));
    }

    /// <summary>Vrai quand le worker peut la prendre : en attente (ou programmée) et arrivée à échéance.</summary>
    public bool IsDueAt(DateTimeOffset instantUtc) =>
        Status is (NotificationStatus.Pending or NotificationStatus.Scheduled) && NextAttemptAtUtc <= instantUtc;

    /// <summary>Ouvre une tentative d'envoi et passe en Processing. Au plus une tentative peut être en cours.</summary>
    /// <param name="consent">
    /// Consentement relu juste avant l'envoi : obligatoire et vérifié comme à la création pour un type marketing (un
    /// consentement retiré entre-temps arrête le message), ignoré sinon.
    /// </param>
    /// <param name="provider">
    /// Prestataire de secours pour cette tentative (même règle IN_APP qu'à la création) ; null pour garder le
    /// prestataire courant.
    /// </param>
    public void StartProcessing(IClock clock, ConsentProof? consent = null, NotificationProviderCode? provider = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotHandedOverOrCancelled();
        DomainException.ThrowIf(Status == NotificationStatus.Processing, NotificationErrors.AttemptInProgress);
        DomainException.ThrowIf(
            Status is not (NotificationStatus.Pending or NotificationStatus.Scheduled), NotificationErrors.NotPending);

        var now = clock.UtcNow;
        DomainException.ThrowIf(now < NextAttemptAtUtc, NotificationErrors.NotDue);
        if (Purpose == NotificationPurpose.Marketing)
        {
            EnsureMarketingConsent(RecipientId, Channel, consent);
        }

        if (provider is not null)
        {
            EnsureProviderServes(provider, Channel);
            ProviderCode = provider;
        }

        _deliveries.Add(NotificationDelivery.Create(ProviderCode, _deliveries.Count + 1, now));
        Status = NotificationStatus.Processing;
        NextAttemptAtUtc = null;
    }

    /// <summary>
    /// Le prestataire a accepté le message ; <paramref name="providerMessageId"/> est l'id qu'il lui a donné (celui que
    /// ses webhooks citeront). Sans effet si la remise est déjà connue (son webhook est arrivé avant que le worker
    /// n'enregistre l'envoi) : un worker « au moins une fois » peut rappeler cette méthode sans erreur.
    /// </summary>
    public void MarkAsSent(IClock clock, ProviderMessageId? providerMessageId = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (Status.IsHandedOver())
        {
            // Le webhook est arrivé avant et ne citait peut-être pas l'id : on le complète, sans rien lever.
            LastDelivery!.RecordProviderMessageId(providerMessageId);
            return;
        }

        DomainException.ThrowIf(Status == NotificationStatus.Cancelled, NotificationErrors.AlreadyCancelled);
        var delivery = CurrentDelivery;
        DomainException.ThrowIf(delivery is null, NotificationErrors.NoAttemptInProgress);

        var now = clock.UtcNow;
        delivery!.MarkAsSent(providerMessageId, now);
        Status = NotificationStatus.Sent;
        SentAtUtc = now;

        Raise(new NotificationSent(TenantId, Id, Channel, delivery.ProviderCode.Value, delivery.AttemptNumber, now));
    }

    /// <summary>
    /// Remise partielle (une partie des appareils, par exemple). Acceptée depuis Sent, ou depuis Processing quand le
    /// webhook précède l'enregistrement de l'envoi. Sans effet si déjà partielle ou complète.
    /// </summary>
    /// <param name="providerMessageId">L'id cité par le webhook : doit être celui de la tentative concernée.</param>
    public void MarkAsPartiallyDelivered(IClock clock, ProviderMessageId? providerMessageId = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (Status is NotificationStatus.PartiallyDelivered or NotificationStatus.Delivered)
        {
            return;
        }

        var delivery = DeliveryReportedOn(providerMessageId);
        var now = clock.UtcNow;
        if (Status == NotificationStatus.Processing)
        {
            SentAtUtc = now;
        }

        delivery.MarkAsPartiallyDelivered(providerMessageId, now);
        Status = NotificationStatus.PartiallyDelivered;

        Raise(new NotificationPartiallyDelivered(TenantId, Id, Channel, now));
    }

    /// <summary>
    /// Remise confirmée par le prestataire : état terminal. Acceptée depuis Sent, PartiallyDelivered, ou Processing
    /// quand le webhook précède l'enregistrement de l'envoi. Sans effet si déjà Delivered.
    /// </summary>
    /// <param name="providerMessageId">L'id cité par le webhook : doit être celui de la tentative concernée.</param>
    public void MarkAsDelivered(IClock clock, ProviderMessageId? providerMessageId = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (Status == NotificationStatus.Delivered)
        {
            return;
        }

        var delivery = DeliveryReportedOn(providerMessageId);
        var now = clock.UtcNow;
        if (Status == NotificationStatus.Processing)
        {
            SentAtUtc = now;
        }

        delivery.MarkAsDelivered(providerMessageId, now);
        Status = NotificationStatus.Delivered;
        DeliveredAtUtc = now;

        Raise(new NotificationDelivered(TenantId, Id, Channel, now));
    }

    /// <summary>
    /// La tentative a échoué : refus pendant l'appel (Processing), ou échec signalé après l'acceptation (Sent : rebond,
    /// numéro injoignable). Un échec permanent, ou le dernier permis, lève aussi <see cref="NotificationDeliveryAbandoned"/>.
    /// Le message brut de <paramref name="failure"/> reste sur l'agrégat : les événements ne portent que son code.
    /// </summary>
    /// <param name="providerMessageId">Pour un échec signalé par webhook : l'id cité, qui doit être celui de la tentative.</param>
    public void MarkAsFailed(NotificationFailure failure, IClock clock, ProviderMessageId? providerMessageId = null)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == NotificationStatus.Cancelled, NotificationErrors.AlreadyCancelled);
        DomainException.ThrowIf(
            Status is NotificationStatus.PartiallyDelivered or NotificationStatus.Delivered, NotificationErrors.AlreadyDelivered);
        DomainException.ThrowIf(
            Status is not (NotificationStatus.Processing or NotificationStatus.Sent), NotificationErrors.NoAttemptInProgress);

        var delivery = LastDelivery!;
        DomainException.ThrowIf(!delivery.Matches(providerMessageId), NotificationErrors.ProviderMessageMismatch);

        // Déjà expurgée (rebond signalé après coup) : le message brut du prestataire n'est pas gardé.
        var kept = IsRedacted ? failure.WithoutReason() : failure;
        var now = clock.UtcNow;
        delivery.MarkAsFailed(kept, providerMessageId, now);
        Status = NotificationStatus.Failed;
        LastFailure = kept;

        Raise(new NotificationFailed(TenantId, Id, Channel, delivery.AttemptNumber, failure.Code, failure.Kind, now));
        if (IsDeliveryAbandoned)
        {
            Raise(new NotificationDeliveryAbandoned(TenantId, Id, Channel, _deliveries.Count, failure.Code, now));
        }
    }

    /// <summary>
    /// Remet une notification en échec TEMPORAIRE dans la file, après le backoff de sa <see cref="RetryPolicy"/>. Un échec
    /// permanent n'est jamais relancé (<c>permanent_failure</c>).
    /// </summary>
    public void ScheduleRetry(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotHandedOverOrCancelled();
        DomainException.ThrowIf(Status != NotificationStatus.Failed, NotificationErrors.NotFailed);
        DomainException.ThrowIf(IsRedacted, NotificationErrors.AlreadyRedacted);
        DomainException.ThrowIf(LastFailure is { IsPermanent: true }, NotificationErrors.PermanentFailure);
        DomainException.ThrowIf(!CanRetry, NotificationErrors.RetriesExhausted);

        var now = clock.UtcNow;
        var nextAttemptAtUtc = now + RetryPolicy.BackoffAfter(_deliveries.Count);
        LastDelivery!.ScheduleRetry(nextAttemptAtUtc);
        Status = NotificationStatus.Pending;
        NextAttemptAtUtc = nextAttemptAtUtc;

        Raise(new NotificationRetryScheduled(TenantId, Id, _deliveries.Count + 1, nextAttemptAtUtc, now));
    }

    /// <summary>
    /// Retry MANUEL d'une notification abandonnée (« dead letter », § 26), décidé par un administrateur après diagnostic
    /// (prestataire réparé, configuration corrigée) : une tentative de plus, tout de suite, même après un échec permanent.
    /// Jamais pour une notification expurgée : son contenu n'existe plus.
    /// </summary>
    public void RetryManually(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(IsRedacted, NotificationErrors.AlreadyRedacted);
        DomainException.ThrowIf(!IsDeliveryAbandoned, NotificationErrors.NotAbandoned);

        var now = clock.UtcNow;
        ManualRetryCount++;
        LastFailure = null;
        Status = NotificationStatus.Pending;
        NextAttemptAtUtc = now;

        Raise(new NotificationRetryScheduled(TenantId, Id, _deliveries.Count + 1, now, now));
    }

    /// <summary>In-app : le destinataire l'a lue. Sans effet si elle l'est déjà.</summary>
    public void MarkAsRead(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureInAppDisplayed();
        ReadAtUtc ??= clock.UtcNow;
    }

    /// <summary>In-app : le destinataire la range ; elle compte comme lue. Sans effet si elle l'est déjà.</summary>
    public void Archive(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureInAppDisplayed();
        var now = clock.UtcNow;
        ReadAtUtc ??= now;
        ArchivedAtUtc ??= now;
    }

    /// <summary>Annule un envoi en attente, programmé ou en échec. Jamais pendant une tentative ni après la remise.</summary>
    public void Cancel(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotHandedOverOrCancelled();

        // Le prestataire détient peut-être déjà le message : l'issue de la tentative doit d'abord être enregistrée.
        DomainException.ThrowIf(Status == NotificationStatus.Processing, NotificationErrors.AttemptInProgress);

        var now = clock.UtcNow;
        Status = NotificationStatus.Cancelled;
        NextAttemptAtUtc = null;
        CancelledAtUtc = now;

        Raise(new NotificationCancelled(TenantId, Id, now));
    }

    /// <summary>
    /// Efface ce qui identifie le destinataire ou révèle le message : l'adresse, le contenu rendu (un OTP, un nom) et
    /// les messages du prestataire. Uniquement quand plus aucun envoi n'est possible — remis au prestataire, annulé, ou
    /// en échec sans tentative restante ; tout autre état doit d'abord être annulé. Une remise encore attendue
    /// (Sent -> Delivered) reste enregistrable ; un échec signalé ensuite n'est plus relancé. Canal, type, prestataire, statut, dates et ids restent. Sans effet si
    /// c'est déjà expurgé.
    /// </summary>
    public void Redact(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (IsRedacted)
        {
            return;
        }

        DomainException.ThrowIf(
            !Status.IsHandedOver() && Status != NotificationStatus.Cancelled && !IsDeliveryAbandoned,
            NotificationErrors.NotRedactable);

        Recipient = NotificationRecipient.Redacted(Channel);
        Content = NotificationContent.Redacted(Channel);
        LastFailure = LastFailure?.WithoutReason();
        foreach (var delivery in _deliveries)
        {
            delivery.EraseFailureReason();
        }

        var now = clock.UtcNow;
        RedactedAtUtc = now;

        Raise(new NotificationRedacted(TenantId, Id, now));
    }

    private static Notification CreateCore(
        TenantId? tenantId,
        LocationId? locationId,
        NotificationTypeDefinition type,
        NotificationRecipient recipient,
        RecipientId? recipientId,
        NotificationProviderCode provider,
        NotificationLanguage language,
        NotificationContent content,
        DateTimeOffset? scheduledAtUtc,
        TemplateId? templateId,
        NotificationRetryPolicy? retryPolicy,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(content.Channel != recipient.Channel, NotificationErrors.ContentChannelMismatch);

        EnsureProviderServes(provider, recipient.Channel);

        var now = clock.UtcNow;
        DomainException.ThrowIf(scheduledAtUtc <= now, NotificationErrors.ScheduleInPast);

        var notification = new Notification(
            NotificationId.New(),
            tenantId,
            locationId,
            type,
            recipientId,
            recipient,
            provider,
            language,
            content,
            templateId,
            retryPolicy ?? NotificationRetryPolicy.Default,
            scheduledAtUtc,
            now);

        notification.Raise(new NotificationCreated(
            tenantId, notification.Id, recipient.Channel, type.Key.Value, type.Purpose, provider.Value, type.Priority, scheduledAtUtc, now));
        return notification;
    }

    /// <summary>
    /// Une notification in-app s'affiche chez un utilisateur : son destinataire est cet utilisateur, déduit de l'adresse
    /// s'il n'est pas donné. Il s'ensuit qu'un type marketing (qui exige un client) n'existe jamais en in-app.
    /// </summary>
    private static RecipientId? ResolveRecipientId(NotificationRecipient recipient, RecipientId? recipientId)
    {
        if (recipient.Channel != NotificationChannel.InApp)
        {
            return recipientId;
        }

        var user = RecipientId.ForUser(UserId.From(Guid.ParseExact(recipient.Address, "N")));
        DomainException.ThrowIf(recipientId is not null && recipientId != user, NotificationErrors.RecipientMismatch);
        return user;
    }

    private static void EnsureMarketingConsent(RecipientId? recipientId, NotificationChannel channel, ConsentProof? consent)
    {
        DomainException.ThrowIf(recipientId is not { IsCustomer: true }, NotificationErrors.CustomerRequired);
        DomainException.ThrowIf(consent is null, NotificationErrors.ConsentRequired);
        DomainException.ThrowIf(consent!.CustomerId.Value != recipientId!.Id, NotificationErrors.ConsentCustomerMismatch);
        DomainException.ThrowIf(consent.Channel != channel.ToConsentChannel(), NotificationErrors.ConsentChannelMismatch);
        DomainException.ThrowIf(!consent.IsGranted, NotificationErrors.ConsentNotGranted);
    }

    // IN_APP est le seul prestataire interne : il n'affiche que des notifications in-app, et elles ne passent que par lui.
    private static void EnsureProviderServes(NotificationProviderCode provider, NotificationChannel channel) =>
        DomainException.ThrowIf(
            (provider == NotificationProviderCode.InApp) != (channel == NotificationChannel.InApp),
            NotificationErrors.ProviderChannelMismatch);

    /// <summary>La tentative visée par un rapport de remise : celle en cours, ou la dernière, acceptée.</summary>
    private NotificationDelivery DeliveryReportedOn(ProviderMessageId? providerMessageId)
    {
        DomainException.ThrowIf(Status == NotificationStatus.Cancelled, NotificationErrors.AlreadyCancelled);
        DomainException.ThrowIf(
            Status is not (NotificationStatus.Processing or NotificationStatus.Sent or NotificationStatus.PartiallyDelivered),
            NotificationErrors.NotSent);

        var delivery = LastDelivery!;
        DomainException.ThrowIf(!delivery.Matches(providerMessageId), NotificationErrors.ProviderMessageMismatch);
        return delivery;
    }

    private void EnsureInAppDisplayed()
    {
        DomainException.ThrowIf(Channel != NotificationChannel.InApp, NotificationErrors.NotInApp);
        DomainException.ThrowIf(!Status.IsHandedOver(), NotificationErrors.NotSent);
    }

    private void EnsureNotHandedOverOrCancelled()
    {
        DomainException.ThrowIf(Status.IsHandedOver(), NotificationErrors.AlreadySent);
        DomainException.ThrowIf(Status == NotificationStatus.Cancelled, NotificationErrors.AlreadyCancelled);
    }
}
