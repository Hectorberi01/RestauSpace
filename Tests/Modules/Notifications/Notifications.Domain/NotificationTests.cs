using Notifications.Domain;

namespace Notifications.Domain;

public class NotificationTests
{
    private static readonly NotificationRetryPolicy ThreeAttempts =
        NotificationRetryPolicy.Create(3, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));

    private static readonly NotificationRecipient EmailRecipient = NotificationRecipient.ForEmail(Email.Create("guest@example.com"));
    private static readonly NotificationContent EmailContent = NotificationContent.Create(NotificationChannel.Email, "Votre réservation", "Bonjour Awa, à demain 20 h.");
    private static readonly NotificationProviderCode Resend = NotificationProviderCode.Create("RESEND");
    private static readonly NotificationLanguage French = NotificationLanguage.Create("fr");
    private static readonly CountryCode Benin = CountryCode.Create("BJ");
    private static readonly NotificationFailure TemporaryTimeout = NotificationFailure.Temporary(NotificationFailure.Timeout, "provider timeout");
    private static readonly ProviderMessageId MessageId = ProviderMessageId.Create("re_01JAB7X9");
    private static readonly CustomerId Awa = CustomerId.New();
    private static readonly RecipientId AwaRecipient = RecipientId.ForCustomer(Awa);
    private static readonly ConsentProof EmailConsent = ConsentProof.Granted(Awa, CommunicationChannel.Email);

    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();

    private Notification Create(
        NotificationTypeDefinition? type = null,
        NotificationRecipient? recipient = null,
        RecipientId? recipientId = null,
        NotificationProviderCode? provider = null,
        NotificationContent? content = null,
        LocationReference? location = null,
        ConsentProof? consent = null,
        DateTimeOffset? scheduledAtUtc = null,
        TemplateId? templateId = null) =>
        Notification.Create(
            _tenantId,
            type ?? NotificationTypes.ReservationConfirmed,
            recipient ?? EmailRecipient,
            recipientId,
            provider ?? Resend,
            French,
            content ?? EmailContent,
            _clock,
            location,
            consent,
            scheduledAtUtc,
            templateId,
            ThreeAttempts);

    private Notification Marketing()
    {
        var notification = Create(NotificationTypes.PromotionalCampaign, recipientId: AwaRecipient, content: MarketingContent(), consent: EmailConsent);
        notification.ClearDomainEvents();
        return notification;
    }

    private static NotificationContent MarketingContent() =>
        NotificationContent.Create(NotificationChannel.Email, "-20 % ce week-end", "Profitez-en. Se désinscrire : https://example.com/u/abc");

    private Notification Platform()
    {
        var notification = Notification.CreateForPlatform(
            NotificationTypes.AccountPasswordReset, EmailRecipient, null, Resend, French, EmailContent, _clock, retryPolicy: ThreeAttempts);
        notification.ClearDomainEvents();
        return notification;
    }

    private Notification Pending()
    {
        var notification = Create();
        notification.ClearDomainEvents();
        return notification;
    }

    private Notification Scheduled()
    {
        var notification = Create(scheduledAtUtc: _clock.UtcNow.AddHours(2));
        notification.ClearDomainEvents();
        return notification;
    }

    private Notification Processing()
    {
        var notification = Pending();
        notification.StartProcessing(_clock);
        return notification;
    }

    private Notification Failed()
    {
        var notification = Processing();
        notification.MarkAsFailed(TemporaryTimeout, _clock);
        notification.ClearDomainEvents();
        return notification;
    }

    private Notification Sent()
    {
        var notification = Processing();
        notification.MarkAsSent(_clock);
        notification.ClearDomainEvents();
        return notification;
    }

    private Notification Delivered()
    {
        var notification = Sent();
        notification.MarkAsDelivered(_clock);
        notification.ClearDomainEvents();
        return notification;
    }

    private Notification Cancelled()
    {
        var notification = Pending();
        notification.Cancel(_clock);
        notification.ClearDomainEvents();
        return notification;
    }

    /// <summary>Échouée à sa dernière tentative autorisée : plus rien ne peut être envoyé.</summary>
    private Notification Abandoned()
    {
        var notification = Failed();
        RetryAndFail(notification);
        RetryAndFail(notification);
        notification.ClearDomainEvents();
        return notification;
    }

    /// <summary>Planifie une nouvelle tentative, attend son échéance, puis fait échouer cette tentative.</summary>
    private void RetryAndFail(Notification notification)
    {
        notification.ScheduleRetry(_clock);
        _clock.Set(notification.NextAttemptAtUtc!.Value);
        notification.StartProcessing(_clock);
        notification.MarkAsFailed(TemporaryTimeout, _clock);
    }

    // ---- Création

    [Fact]
    public void Create_builds_a_pending_notification_and_raises_NotificationCreated()
    {
        var location = new LocationReference(_tenantId, LocationId.New(), Benin);
        var templateId = TemplateId.New();

        var notification = Create(recipientId: AwaRecipient, location: location, templateId: templateId);

        Assert.NotEqual(default, notification.Id);
        Assert.Equal(_tenantId, notification.TenantId);
        Assert.False(notification.IsPlatformLevel);
        Assert.Equal(location.LocationId, notification.LocationId);
        Assert.Equal(NotificationTypes.ReservationConfirmed.Key, notification.TemplateKey);
        Assert.Same(NotificationTypes.ReservationConfirmed, notification.Type);
        Assert.Equal(NotificationPurpose.Transactional, notification.Purpose);
        Assert.Equal(NotificationPriority.Normal, notification.Priority);
        Assert.Equal(AwaRecipient, notification.RecipientId);
        Assert.Equal(Awa, notification.CustomerId);
        Assert.Equal(EmailRecipient, notification.Recipient);
        Assert.Equal(NotificationChannel.Email, notification.Channel);
        Assert.Equal(Resend, notification.ProviderCode);
        Assert.Equal(French, notification.Language);
        Assert.Equal(EmailContent, notification.Content);
        Assert.Equal(templateId, notification.TemplateId);
        Assert.Equal(ThreeAttempts, notification.RetryPolicy);
        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Empty(notification.Deliveries);
        Assert.Null(notification.ScheduledAtUtc);
        Assert.Equal(_clock.UtcNow, notification.CreatedAtUtc);
        Assert.False(notification.IsRedacted);
        Assert.True(notification.IsDueAt(_clock.UtcNow));
        Assert.Equal(
            new NotificationCreated(
                _tenantId, notification.Id, NotificationChannel.Email, "reservation.confirmed", NotificationPurpose.Transactional,
                "RESEND", NotificationPriority.Normal, null, _clock.UtcNow),
            DomainAssert.Raised<NotificationCreated>(notification));
    }

    [Fact]
    public void Create_takes_purpose_and_priority_from_the_type()
    {
        var notification = Notification.CreateForPlatform(
            NotificationTypes.SecurityOtp, EmailRecipient, null, Resend, French,
            NotificationContent.Create(NotificationChannel.Email, "Votre code", "Votre code : 482913"), _clock);

        Assert.Equal(NotificationPriority.Critical, notification.Priority);
        Assert.Equal(NotificationPurpose.Transactional, notification.Purpose);
        Assert.Equal(NotificationRetryPolicy.Default, notification.RetryPolicy);
    }

    [Fact]
    public void Create_rejects_a_default_tenant() =>
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => Notification.Create(
                default, NotificationTypes.OrderConfirmed, EmailRecipient, null, Resend, French, EmailContent, _clock));

    [Fact]
    public void Create_rejects_a_location_of_another_tenant() =>
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => Create(location: new LocationReference(TenantId.New(), LocationId.New(), Benin)));

    [Fact]
    public void Create_rejects_a_content_rendered_for_another_channel() =>
        DomainAssert.Throws(
            NotificationErrors.ContentChannelMismatch,
            () => Create(content: NotificationContent.Create(NotificationChannel.Sms, null, "Votre table est prête.")));

    [Fact]
    public void Create_rejects_the_in_app_provider_outside_the_in_app_channel() =>
        DomainAssert.Throws(NotificationErrors.ProviderChannelMismatch, () => Create(provider: NotificationProviderCode.InApp));

    [Fact]
    public void Create_rejects_an_in_app_notification_through_an_external_provider()
    {
        var user = UserId.New();

        DomainAssert.Throws(
            NotificationErrors.ProviderChannelMismatch,
            () => Create(
                NotificationTypes.StaffNewReservation,
                NotificationRecipient.ForInApp(user),
                provider: Resend,
                content: NotificationContent.Create(NotificationChannel.InApp, null, "Nouvelle réservation")));
    }

    [Fact]
    public void An_in_app_notification_is_addressed_to_the_user_it_is_displayed_to()
    {
        var user = UserId.New();
        var inApp = NotificationContent.Create(NotificationChannel.InApp, "Réservation", "Nouvelle réservation pour 4");

        var notification = Create(
            NotificationTypes.StaffNewReservation, NotificationRecipient.ForInApp(user), provider: NotificationProviderCode.InApp, content: inApp);

        Assert.Equal(RecipientId.ForUser(user), notification.RecipientId);
        Assert.Null(notification.CustomerId);
        Assert.Equal(NotificationChannel.InApp, notification.Channel);
        DomainAssert.Throws(
            NotificationErrors.RecipientMismatch,
            () => Create(
                NotificationTypes.StaffNewReservation, NotificationRecipient.ForInApp(user), RecipientId.ForUser(UserId.New()),
                NotificationProviderCode.InApp, inApp));
        DomainAssert.Throws(
            NotificationErrors.RecipientMismatch,
            () => Create(
                NotificationTypes.StaffNewReservation, NotificationRecipient.ForInApp(user), AwaRecipient,
                NotificationProviderCode.InApp, inApp));
    }

    // ---- Marketing et consentement

    [Fact]
    public void Create_transactional_needs_no_consent_and_ignores_a_denied_one()
    {
        var notification = Create(recipientId: AwaRecipient, consent: ConsentProof.Denied(Awa, CommunicationChannel.Email));

        Assert.Equal(NotificationPurpose.Transactional, notification.Purpose);
    }

    [Fact]
    public void Create_marketing_with_granted_consent_is_accepted()
    {
        var notification = Create(NotificationTypes.PromotionalCampaign, recipientId: AwaRecipient, content: MarketingContent(), consent: EmailConsent);

        Assert.Equal(NotificationPurpose.Marketing, notification.Purpose);
        Assert.Equal(NotificationPriority.Low, notification.Priority);
        Assert.Equal(Awa, notification.CustomerId);
    }

    [Fact]
    public void Create_marketing_without_consent_proof_is_rejected() =>
        DomainAssert.Throws(
            NotificationErrors.ConsentRequired,
            () => Create(NotificationTypes.PromotionalCampaign, recipientId: AwaRecipient, content: MarketingContent()));

    [Fact]
    public void Create_marketing_needs_a_customer_as_recipient()
    {
        DomainAssert.Throws(
            NotificationErrors.CustomerRequired,
            () => Create(NotificationTypes.PromotionalCampaign, content: MarketingContent(), consent: EmailConsent));
        DomainAssert.Throws(
            NotificationErrors.CustomerRequired,
            () => Create(
                NotificationTypes.PromotionalCampaign, recipientId: RecipientId.ForUser(UserId.New()), content: MarketingContent(), consent: EmailConsent));
    }

    [Fact]
    public void A_marketing_notification_never_goes_in_app()
    {
        var user = UserId.New();

        DomainAssert.Throws(
            NotificationErrors.CustomerRequired,
            () => Create(
                NotificationTypes.PromotionalCampaign,
                NotificationRecipient.ForInApp(user),
                provider: NotificationProviderCode.InApp,
                content: NotificationContent.Create(NotificationChannel.InApp, null, "-20 % ce week-end"),
                consent: EmailConsent));
    }

    [Fact]
    public void Create_marketing_with_the_consent_of_another_customer_is_rejected() =>
        DomainAssert.Throws(
            NotificationErrors.ConsentCustomerMismatch,
            () => Create(
                NotificationTypes.PromotionalCampaign, recipientId: AwaRecipient, content: MarketingContent(),
                consent: ConsentProof.Granted(CustomerId.New(), CommunicationChannel.Email)));

    [Fact]
    public void Create_marketing_with_denied_consent_is_rejected() =>
        DomainAssert.Throws(
            NotificationErrors.ConsentNotGranted,
            () => Create(
                NotificationTypes.PromotionalCampaign, recipientId: AwaRecipient, content: MarketingContent(),
                consent: ConsentProof.Denied(Awa, CommunicationChannel.Email)));

    [Fact]
    public void Create_marketing_with_consent_for_another_channel_is_rejected() =>
        DomainAssert.Throws(
            NotificationErrors.ConsentChannelMismatch,
            () => Create(
                NotificationTypes.PromotionalCampaign, recipientId: AwaRecipient, content: MarketingContent(),
                consent: ConsentProof.Granted(Awa, CommunicationChannel.Sms)));

    [Fact]
    public void StartProcessing_marketing_with_a_fresh_granted_consent_opens_the_attempt()
    {
        var notification = Marketing();

        notification.StartProcessing(_clock, EmailConsent);

        Assert.Equal(NotificationStatus.Processing, notification.Status);
    }

    [Fact]
    public void StartProcessing_marketing_is_stopped_by_a_consent_withdrawn_since_creation()
    {
        var notification = Marketing();

        DomainAssert.Throws(
            NotificationErrors.ConsentNotGranted,
            () => notification.StartProcessing(_clock, ConsentProof.Denied(Awa, CommunicationChannel.Email)));
        DomainAssert.Throws(NotificationErrors.ConsentRequired, () => notification.StartProcessing(_clock));
        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Empty(notification.Deliveries);
    }

    [Fact]
    public void A_retried_marketing_notification_is_stopped_by_a_withdrawn_consent()
    {
        var notification = Marketing();
        notification.StartProcessing(_clock, EmailConsent);
        notification.MarkAsFailed(TemporaryTimeout, _clock);
        notification.ScheduleRetry(_clock);
        _clock.Set(notification.NextAttemptAtUtc!.Value);

        DomainAssert.Throws(
            NotificationErrors.ConsentNotGranted,
            () => notification.StartProcessing(_clock, ConsentProof.Denied(Awa, CommunicationChannel.Email)));
        notification.Cancel(_clock);

        Assert.Equal(NotificationStatus.Cancelled, notification.Status);
    }

    [Fact]
    public void StartProcessing_transactional_ignores_the_consent()
    {
        var notification = Pending();

        notification.StartProcessing(_clock, ConsentProof.Denied(Awa, CommunicationChannel.Email));

        Assert.Equal(NotificationStatus.Processing, notification.Status);
    }

    // ---- Plateforme

    [Fact]
    public void CreateForPlatform_builds_a_notification_without_tenant_nor_location()
    {
        var notification = Notification.CreateForPlatform(
            NotificationTypes.AccountVerifyEmail, EmailRecipient, RecipientId.ForUser(UserId.New()), Resend, French, EmailContent, _clock);

        Assert.Null(notification.TenantId);
        Assert.True(notification.IsPlatformLevel);
        Assert.Null(notification.LocationId);
        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Null(DomainAssert.Raised<NotificationCreated>(notification).TenantId);
    }

    [Fact]
    public void A_platform_notification_can_never_be_marketing() =>
        DomainAssert.Throws(
            NotificationErrors.PlatformMarketing,
            () => Notification.CreateForPlatform(
                NotificationTypes.PromotionalCampaign, EmailRecipient, AwaRecipient, Resend, French, MarketingContent(), _clock));

    [Fact]
    public void A_platform_notification_follows_the_same_lifecycle_and_its_events_carry_no_tenant()
    {
        var notification = Platform();

        notification.StartProcessing(_clock);
        notification.MarkAsSent(_clock);
        notification.MarkAsDelivered(_clock);
        notification.Redact(_clock);

        Assert.Equal(NotificationStatus.Delivered, notification.Status);
        Assert.Null(DomainAssert.Raised<NotificationSent>(notification).TenantId);
        Assert.Null(DomainAssert.Raised<NotificationDelivered>(notification).TenantId);
        Assert.Null(DomainAssert.Raised<NotificationRedacted>(notification).TenantId);
    }

    // ---- Programmation

    [Fact]
    public void Create_with_a_future_date_builds_a_scheduled_notification_due_only_at_that_date()
    {
        var at = _clock.UtcNow.AddHours(2);

        var notification = Create(scheduledAtUtc: at);

        Assert.Equal(NotificationStatus.Scheduled, notification.Status);
        Assert.Equal(at, notification.ScheduledAtUtc);
        Assert.Equal(at, notification.NextAttemptAtUtc);
        Assert.Equal(at, DomainAssert.Raised<NotificationCreated>(notification).ScheduledAtUtc);
        Assert.False(notification.IsDueAt(_clock.UtcNow));
        Assert.True(notification.IsDueAt(at));
        DomainAssert.Throws(NotificationErrors.NotDue, () => notification.StartProcessing(_clock));

        _clock.Set(at);
        notification.StartProcessing(_clock);
        Assert.Equal(NotificationStatus.Processing, notification.Status);
    }

    [Fact]
    public void Create_rejects_a_date_in_the_past_or_now()
    {
        DomainAssert.Throws(NotificationErrors.ScheduleInPast, () => Create(scheduledAtUtc: _clock.UtcNow));
        DomainAssert.Throws(NotificationErrors.ScheduleInPast, () => Create(scheduledAtUtc: _clock.UtcNow.AddMinutes(-1)));
    }

    [Fact]
    public void Schedule_postpones_a_pending_notification_and_raises_NotificationScheduled()
    {
        var notification = Pending();
        var at = _clock.UtcNow.AddDays(1);

        notification.Schedule(at, _clock);

        Assert.Equal(NotificationStatus.Scheduled, notification.Status);
        Assert.Equal(at, notification.ScheduledAtUtc);
        Assert.Equal(at, notification.NextAttemptAtUtc);
        Assert.Equal(
            new NotificationScheduled(_tenantId, notification.Id, at, _clock.UtcNow),
            DomainAssert.Raised<NotificationScheduled>(notification));
    }

    [Fact]
    public void Schedule_moves_a_scheduled_notification_and_is_silent_for_the_same_date()
    {
        var notification = Scheduled();
        var at = notification.ScheduledAtUtc!.Value;

        notification.Schedule(at, _clock);
        DomainAssert.NoEvents(notification);

        notification.Schedule(at.AddHours(1), _clock);
        Assert.Equal(at.AddHours(1), notification.NextAttemptAtUtc);
        DomainAssert.Raised<NotificationScheduled>(notification);
    }

    [Fact]
    public void Schedule_is_rejected_once_an_attempt_was_made()
    {
        var future = _clock.UtcNow.AddDays(1);

        DomainAssert.Throws(NotificationErrors.NotSchedulable, () => Processing().Schedule(future, _clock));
        DomainAssert.Throws(NotificationErrors.NotSchedulable, () => Failed().Schedule(future, _clock));
        DomainAssert.Throws(NotificationErrors.AlreadySent, () => Sent().Schedule(future, _clock));
        DomainAssert.Throws(NotificationErrors.AlreadyCancelled, () => Cancelled().Schedule(future, _clock));
        DomainAssert.Throws(NotificationErrors.ScheduleInPast, () => Pending().Schedule(_clock.UtcNow, _clock));
    }

    [Fact]
    public void A_scheduled_notification_can_be_cancelled()
    {
        var notification = Scheduled();

        notification.Cancel(_clock);

        Assert.Equal(NotificationStatus.Cancelled, notification.Status);
        Assert.Null(notification.NextAttemptAtUtc);
    }

    // ---- Envoi

    [Fact]
    public void StartProcessing_opens_an_attempt_and_moves_to_Processing()
    {
        var notification = Pending();

        notification.StartProcessing(_clock);

        Assert.Equal(NotificationStatus.Processing, notification.Status);
        Assert.True(notification.IsAttemptInProgress);
        Assert.Null(notification.NextAttemptAtUtc);
        Assert.False(notification.IsDueAt(_clock.UtcNow));
        var delivery = Assert.Single(notification.Deliveries);
        Assert.Equal((1, NotificationDeliveryStatus.InProgress, _clock.UtcNow), (delivery.AttemptNumber, delivery.Status, delivery.StartedAtUtc));
        Assert.Equal(Resend, delivery.ProviderCode);
        Assert.Null(delivery.ProviderMessageId);
        Assert.Null(delivery.CompletedAtUtc);
        DomainAssert.NoEvents(notification);
    }

    [Fact]
    public void StartProcessing_rejects_a_second_concurrent_attempt()
    {
        var notification = Processing();

        DomainAssert.Throws(NotificationErrors.AttemptInProgress, () => notification.StartProcessing(_clock));
        Assert.Single(notification.Deliveries);
    }

    [Fact]
    public void StartProcessing_rejects_a_failed_notification_until_a_retry_is_scheduled() =>
        DomainAssert.Throws(NotificationErrors.NotPending, () => Failed().StartProcessing(_clock));

    [Fact]
    public void MarkAsSent_closes_the_attempt_and_raises_NotificationSent()
    {
        var notification = Processing();
        _clock.Advance(TimeSpan.FromSeconds(2));

        notification.MarkAsSent(_clock, MessageId);

        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.Equal(_clock.UtcNow, notification.SentAtUtc);
        Assert.False(notification.IsAttemptInProgress);
        Assert.False(notification.IsTerminal);
        var delivery = Assert.Single(notification.Deliveries);
        Assert.Equal((NotificationDeliveryStatus.Sent, _clock.UtcNow, MessageId), (delivery.Status, delivery.CompletedAtUtc, delivery.ProviderMessageId));
        Assert.True(delivery.IsAccepted);
        Assert.Equal(
            new NotificationSent(_tenantId, notification.Id, NotificationChannel.Email, "RESEND", 1, _clock.UtcNow),
            DomainAssert.Raised<NotificationSent>(notification));
    }

    [Fact]
    public void MarkAsSent_requires_an_attempt_in_progress()
    {
        DomainAssert.Throws(NotificationErrors.NoAttemptInProgress, () => Pending().MarkAsSent(_clock));
        DomainAssert.Throws(NotificationErrors.NoAttemptInProgress, () => Failed().MarkAsSent(_clock));
        DomainAssert.Throws(NotificationErrors.AlreadyCancelled, () => Cancelled().MarkAsSent(_clock));
    }

    [Fact]
    public void MarkAsSent_again_is_a_silent_no_op_for_an_at_least_once_worker()
    {
        var sent = Sent();
        var delivered = Delivered();
        var sentAt = sent.SentAtUtc;
        _clock.Advance(TimeSpan.FromMinutes(1));

        sent.MarkAsSent(_clock);
        delivered.MarkAsSent(_clock);

        Assert.Equal(sentAt, sent.SentAtUtc);
        Assert.Equal(NotificationStatus.Delivered, delivered.Status);
        DomainAssert.NoEvents(sent);
        DomainAssert.NoEvents(delivered);
    }

    // ---- Remise

    [Fact]
    public void MarkAsDelivered_ends_the_lifecycle_and_raises_NotificationDelivered()
    {
        var notification = Sent();
        _clock.Advance(TimeSpan.FromSeconds(30));

        notification.MarkAsDelivered(_clock);

        Assert.Equal(NotificationStatus.Delivered, notification.Status);
        Assert.Equal(_clock.UtcNow, notification.DeliveredAtUtc);
        Assert.True(notification.IsTerminal);
        Assert.Equal(
            new NotificationDelivered(_tenantId, notification.Id, NotificationChannel.Email, _clock.UtcNow),
            DomainAssert.Raised<NotificationDelivered>(notification));
    }

    [Fact]
    public void MarkAsPartiallyDelivered_then_MarkAsDelivered()
    {
        var notification = Sent();

        notification.MarkAsPartiallyDelivered(_clock);
        notification.MarkAsPartiallyDelivered(_clock);

        Assert.Equal(NotificationStatus.PartiallyDelivered, notification.Status);
        Assert.False(notification.IsTerminal);
        DomainAssert.Raised<NotificationPartiallyDelivered>(notification);

        notification.MarkAsDelivered(_clock);
        Assert.Equal(NotificationStatus.Delivered, notification.Status);
    }

    [Fact]
    public void A_partial_report_after_the_full_delivery_changes_nothing()
    {
        var notification = Delivered();

        notification.MarkAsPartiallyDelivered(_clock);
        notification.MarkAsDelivered(_clock);

        Assert.Equal(NotificationStatus.Delivered, notification.Status);
        DomainAssert.NoEvents(notification);
    }

    [Fact]
    public void A_delivery_report_arriving_before_the_send_is_recorded_closes_the_attempt()
    {
        var notification = Processing();

        notification.MarkAsDelivered(_clock);

        Assert.Equal(NotificationStatus.Delivered, notification.Status);
        Assert.Equal(_clock.UtcNow, notification.SentAtUtc);
        Assert.Equal(NotificationDeliveryStatus.Delivered, Assert.Single(notification.Deliveries).Status);
        DomainAssert.NotRaised<NotificationSent>(notification);

        var partial = Processing();
        partial.MarkAsPartiallyDelivered(_clock);
        Assert.Equal(NotificationStatus.PartiallyDelivered, partial.Status);
        Assert.False(partial.IsAttemptInProgress);
    }

    [Fact]
    public void A_delivery_report_needs_a_notification_handed_over_or_in_progress()
    {
        DomainAssert.Throws(NotificationErrors.NotSent, () => Pending().MarkAsDelivered(_clock));
        DomainAssert.Throws(NotificationErrors.NotSent, () => Scheduled().MarkAsPartiallyDelivered(_clock));
        DomainAssert.Throws(NotificationErrors.NotSent, () => Failed().MarkAsDelivered(_clock));
        DomainAssert.Throws(NotificationErrors.AlreadyCancelled, () => Cancelled().MarkAsDelivered(_clock));
    }

    // ---- Échecs et relances

    [Fact]
    public void MarkAsFailed_closes_the_attempt_and_raises_NotificationFailed_with_the_code_only()
    {
        var notification = Processing();

        notification.MarkAsFailed(TemporaryTimeout, _clock);

        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(TemporaryTimeout, notification.LastFailure);
        Assert.Null(notification.NextAttemptAtUtc);
        Assert.True(notification.CanRetry);
        var delivery = Assert.Single(notification.Deliveries);
        Assert.Equal(
            (NotificationDeliveryStatus.Failed, "TIMEOUT", _clock.UtcNow),
            (delivery.Status, delivery.FailureCode, delivery.CompletedAtUtc));
        Assert.Equal("provider timeout", delivery.Failure!.Reason);
        Assert.Equal(
            new NotificationFailed(_tenantId, notification.Id, NotificationChannel.Email, 1, "TIMEOUT", NotificationFailureKind.Temporary, _clock.UtcNow),
            DomainAssert.Raised<NotificationFailed>(notification));
        DomainAssert.NotRaised<NotificationDeliveryAbandoned>(notification);
    }

    [Fact]
    public void MarkAsFailed_requires_an_attempt_in_progress_or_an_accepted_message()
    {
        DomainAssert.Throws(NotificationErrors.NoAttemptInProgress, () => Pending().MarkAsFailed(TemporaryTimeout, _clock));
        DomainAssert.Throws(NotificationErrors.NoAttemptInProgress, () => Failed().MarkAsFailed(TemporaryTimeout, _clock));
        DomainAssert.Throws(NotificationErrors.AlreadyDelivered, () => Delivered().MarkAsFailed(TemporaryTimeout, _clock));
        DomainAssert.Throws(NotificationErrors.AlreadyCancelled, () => Cancelled().MarkAsFailed(TemporaryTimeout, _clock));
    }

    [Fact]
    public void A_permanent_failure_is_a_dead_letter_at_once_and_is_never_retried()
    {
        var notification = Processing();

        notification.MarkAsFailed(NotificationFailure.Permanent(NotificationFailure.InvalidPhoneNumber), _clock);

        Assert.True(notification.IsDeliveryAbandoned);
        Assert.False(notification.CanRetry);
        Assert.Equal(
            new NotificationDeliveryAbandoned(_tenantId, notification.Id, NotificationChannel.Email, 1, "INVALID_PHONE_NUMBER", _clock.UtcNow),
            DomainAssert.Raised<NotificationDeliveryAbandoned>(notification));
        DomainAssert.Throws(NotificationErrors.PermanentFailure, () => notification.ScheduleRetry(_clock));
        notification.Cancel(_clock);
        Assert.Equal(NotificationStatus.Cancelled, notification.Status);
    }

    [Fact]
    public void A_bounce_reported_after_the_hand_over_fails_the_accepted_delivery()
    {
        var notification = Processing();
        notification.MarkAsSent(_clock, MessageId);
        notification.ClearDomainEvents();
        var bounce = NotificationFailure.Permanent(NotificationFailure.Bounced, "550 guest@example.com unknown");

        DomainAssert.Throws(
            NotificationErrors.ProviderMessageMismatch,
            () => notification.MarkAsFailed(bounce, _clock, ProviderMessageId.Create("another")));
        notification.MarkAsFailed(bounce, _clock, MessageId);

        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(NotificationDeliveryStatus.Failed, Assert.Single(notification.Deliveries).Status);
        Assert.True(notification.IsDeliveryAbandoned);
        DomainAssert.Raised<NotificationFailed>(notification);
    }

    [Fact]
    public void ScheduleRetry_requeues_a_failed_notification_after_the_backoff()
    {
        var notification = Failed();

        notification.ScheduleRetry(_clock);

        var next = _clock.UtcNow + TimeSpan.FromMinutes(1);
        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Equal(next, notification.NextAttemptAtUtc);
        Assert.False(notification.IsDueAt(_clock.UtcNow));
        Assert.Equal(
            new NotificationRetryScheduled(_tenantId, notification.Id, 2, next, _clock.UtcNow),
            DomainAssert.Raised<NotificationRetryScheduled>(notification));
        DomainAssert.Throws(NotificationErrors.NotDue, () => notification.StartProcessing(_clock));
    }

    [Fact]
    public void ScheduleRetry_is_rejected_when_the_notification_did_not_fail()
    {
        DomainAssert.Throws(NotificationErrors.NotFailed, () => Pending().ScheduleRetry(_clock));
        DomainAssert.Throws(NotificationErrors.NotFailed, () => Processing().ScheduleRetry(_clock));
        DomainAssert.Throws(NotificationErrors.AlreadySent, () => Sent().ScheduleRetry(_clock));
    }

    [Fact]
    public void A_retry_opens_a_second_attempt_which_can_succeed()
    {
        var notification = Failed();
        notification.ScheduleRetry(_clock);
        _clock.Set(notification.NextAttemptAtUtc!.Value);

        notification.StartProcessing(_clock);
        notification.MarkAsSent(_clock);

        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.Equal(
            [NotificationDeliveryStatus.Failed, NotificationDeliveryStatus.Sent],
            notification.Deliveries.Select(attempt => attempt.Status));
        Assert.Equal(2, DomainAssert.Raised<NotificationSent>(notification).AttemptNumber);
    }

    [Fact]
    public void The_last_allowed_failure_raises_NotificationDeliveryAbandoned_and_retries_stop()
    {
        var notification = Failed();
        RetryAndFail(notification);
        notification.ClearDomainEvents();

        notification.ScheduleRetry(_clock);
        _clock.Set(notification.NextAttemptAtUtc!.Value);
        notification.StartProcessing(_clock);
        notification.MarkAsFailed(TemporaryTimeout, _clock);

        Assert.True(notification.IsDeliveryAbandoned);
        Assert.False(notification.CanRetry);
        Assert.Equal(
            new NotificationDeliveryAbandoned(_tenantId, notification.Id, NotificationChannel.Email, 3, "TIMEOUT", _clock.UtcNow),
            DomainAssert.Raised<NotificationDeliveryAbandoned>(notification));
        DomainAssert.Throws(NotificationErrors.RetriesExhausted, () => notification.ScheduleRetry(_clock));
    }

    [Fact]
    public void Retry_backoff_grows_with_each_failed_attempt()
    {
        var notification = Failed();
        var start = _clock.UtcNow;

        notification.ScheduleRetry(_clock);
        Assert.Equal(start + TimeSpan.FromMinutes(1), notification.NextAttemptAtUtc);
        _clock.Set(notification.NextAttemptAtUtc!.Value);
        notification.StartProcessing(_clock);
        notification.MarkAsFailed(TemporaryTimeout, _clock);
        var second = _clock.UtcNow;
        notification.ScheduleRetry(_clock);

        Assert.Equal(second + TimeSpan.FromMinutes(2), notification.NextAttemptAtUtc);
    }

    [Fact]
    public void Events_carry_no_address_no_content_and_no_provider_message()
    {
        Type[] events =
        [
            typeof(NotificationCreated), typeof(NotificationScheduled), typeof(NotificationSent),
            typeof(NotificationPartiallyDelivered), typeof(NotificationDelivered), typeof(NotificationFailed),
            typeof(NotificationDeliveryAbandoned), typeof(NotificationRetryScheduled), typeof(NotificationCancelled),
            typeof(NotificationRedacted),
        ];
        var properties = events.SelectMany(type => type.GetProperties()).ToList();

        Assert.DoesNotContain(properties, property => property.PropertyType == typeof(NotificationRecipient));
        Assert.DoesNotContain(properties, property => property.PropertyType == typeof(NotificationContent));
        // Les seuls textes : la clé du type et le code du prestataire, jamais un message.
        Assert.Equal(
            ["FailureCode", "FailureCode", "Provider", "Provider", "TemplateKey"],
            properties.Where(property => property.PropertyType == typeof(string)).Select(property => property.Name).Order());
    }

    [Fact]
    public void ScheduleRetry_records_the_retry_date_on_the_failed_delivery()
    {
        var notification = Failed();

        notification.ScheduleRetry(_clock);

        Assert.Equal(notification.NextAttemptAtUtc, Assert.Single(notification.Deliveries).NextRetryAtUtc);
    }

    [Fact]
    public void A_retry_can_go_through_a_fallback_provider()
    {
        var twilio = NotificationProviderCode.Create("TWILIO");
        var notification = Failed();
        notification.ScheduleRetry(_clock);
        _clock.Set(notification.NextAttemptAtUtc!.Value);

        DomainAssert.Throws(
            NotificationErrors.ProviderChannelMismatch,
            () => notification.StartProcessing(_clock, provider: NotificationProviderCode.InApp));
        notification.StartProcessing(_clock, provider: twilio);
        notification.MarkAsSent(_clock);

        Assert.Equal(twilio, notification.ProviderCode);
        Assert.Equal([Resend, twilio], notification.Deliveries.Select(delivery => delivery.ProviderCode));
        Assert.Equal("TWILIO", DomainAssert.Raised<NotificationSent>(notification).Provider);
    }

    [Fact]
    public void A_delivery_report_must_cite_the_message_of_the_delivery()
    {
        var notification = Processing();
        notification.MarkAsSent(_clock, MessageId);

        DomainAssert.Throws(
            NotificationErrors.ProviderMessageMismatch,
            () => notification.MarkAsDelivered(_clock, ProviderMessageId.Create("another")));
        notification.MarkAsDelivered(_clock, MessageId);

        Assert.Equal(NotificationDeliveryStatus.Delivered, Assert.Single(notification.Deliveries).Status);
        Assert.Equal(_clock.UtcNow, notification.Deliveries.Single().DeliveredAtUtc);
    }

    [Fact]
    public void An_early_report_without_id_is_completed_by_the_late_MarkAsSent()
    {
        var notification = Processing();
        notification.MarkAsDelivered(_clock);

        notification.MarkAsSent(_clock, MessageId);

        Assert.Equal(MessageId, Assert.Single(notification.Deliveries).ProviderMessageId);
        Assert.Equal(NotificationStatus.Delivered, notification.Status);
    }

    [Fact]
    public void A_bounce_after_redaction_keeps_no_provider_message_and_is_not_retried()
    {
        var notification = Sent();
        notification.Redact(_clock);

        notification.MarkAsFailed(NotificationFailure.Temporary(NotificationFailure.Bounced, "mailbox guest@example.com full"), _clock);

        Assert.Null(notification.LastFailure!.Reason);
        Assert.Null(notification.Deliveries.Single().Failure!.Reason);
        Assert.True(notification.IsDeliveryAbandoned);
        DomainAssert.Throws(NotificationErrors.AlreadyRedacted, () => notification.ScheduleRetry(_clock));
    }

    // ---- Annulation

    [Fact]
    public void Cancel_stops_a_pending_notification_and_raises_NotificationCancelled()
    {
        var notification = Pending();

        notification.Cancel(_clock);

        Assert.Equal(NotificationStatus.Cancelled, notification.Status);
        Assert.Equal(_clock.UtcNow, notification.CancelledAtUtc);
        Assert.Null(notification.NextAttemptAtUtc);
        Assert.True(notification.IsTerminal);
        Assert.Equal(
            new NotificationCancelled(_tenantId, notification.Id, _clock.UtcNow),
            DomainAssert.Raised<NotificationCancelled>(notification));
    }

    [Fact]
    public void Cancel_stops_a_failed_or_abandoned_notification()
    {
        var failed = Failed();
        var abandoned = Abandoned();

        failed.Cancel(_clock);
        abandoned.Cancel(_clock);

        Assert.Equal(NotificationStatus.Cancelled, failed.Status);
        Assert.Equal(NotificationStatus.Cancelled, abandoned.Status);
    }

    [Fact]
    public void Cancel_is_rejected_during_an_attempt_and_after_the_hand_over()
    {
        DomainAssert.Throws(NotificationErrors.AttemptInProgress, () => Processing().Cancel(_clock));
        DomainAssert.Throws(NotificationErrors.AlreadySent, () => Sent().Cancel(_clock));
        DomainAssert.Throws(NotificationErrors.AlreadySent, () => Delivered().Cancel(_clock));
        DomainAssert.Throws(NotificationErrors.AlreadyCancelled, () => Cancelled().Cancel(_clock));
    }

    [Fact]
    public void Cancelled_is_terminal()
    {
        var notification = Cancelled();

        DomainAssert.Throws(NotificationErrors.AlreadyCancelled, () => notification.StartProcessing(_clock));
        DomainAssert.Throws(NotificationErrors.AlreadyCancelled, () => notification.ScheduleRetry(_clock));
        DomainAssert.Throws(NotificationErrors.AlreadyCancelled, () => notification.MarkAsFailed(TemporaryTimeout, _clock));
        Assert.False(notification.IsDueAt(_clock.UtcNow.AddYears(1)));
    }

    [Fact]
    public void Delivered_is_terminal()
    {
        var notification = Delivered();

        DomainAssert.Throws(NotificationErrors.AlreadySent, () => notification.StartProcessing(_clock));
        DomainAssert.Throws(NotificationErrors.AlreadySent, () => notification.ScheduleRetry(_clock));
        Assert.False(notification.IsDueAt(_clock.UtcNow.AddYears(1)));
    }

    [Fact]
    public void Attempts_and_state_cannot_be_mutated_from_outside()
    {
        var notification = Processing();

        var exposed = Assert.IsAssignableFrom<ICollection<NotificationDelivery>>(notification.Deliveries);
        Assert.Throws<NotSupportedException>(exposed.Clear);

        var mutators = typeof(NotificationDelivery)
            .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName);
        Assert.Empty(mutators);
        Assert.All(
            typeof(NotificationDelivery).GetProperties().Concat(typeof(Notification).GetProperties()),
            property => Assert.True(property.SetMethod is null || !property.SetMethod.IsPublic, property.Name));
    }

    // ---- Expurgation

    private static void AssertRedacted(Notification notification)
    {
        Assert.True(notification.IsRedacted);
        Assert.True(notification.Recipient.IsRedacted);
        Assert.Equal(string.Empty, notification.Recipient.Address);
        Assert.True(notification.Content.IsRedacted);
        Assert.Null(notification.Content.Subject);
        Assert.Equal(string.Empty, notification.Content.Body);
        Assert.Null(notification.LastFailure?.Reason);
        Assert.All(notification.Deliveries, delivery => Assert.Null(delivery.Failure?.Reason));
    }

    [Fact]
    public void Redact_erases_the_address_the_content_and_the_failure_reasons_of_a_sent_notification()
    {
        var notification = Create(recipientId: AwaRecipient);
        notification.StartProcessing(_clock);
        notification.MarkAsFailed(NotificationFailure.Temporary(NotificationFailure.ProviderUnavailable, "550 mailbox guest@example.com unavailable"), _clock);
        notification.ScheduleRetry(_clock);
        _clock.Set(notification.NextAttemptAtUtc!.Value);
        notification.StartProcessing(_clock);
        notification.MarkAsSent(_clock);
        var sentAt = notification.SentAtUtc;
        notification.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromDays(30));

        notification.Redact(_clock);

        AssertRedacted(notification);
        Assert.Equal(_clock.UtcNow, notification.RedactedAtUtc);
        Assert.Equal("Email:[redacted]", notification.Recipient.ToString());
        Assert.Equal("Email:[redacted]", notification.Content.ToString());
        // Ce qui n'est pas personnel reste : l'historique demeure exploitable.
        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.Equal(NotificationChannel.Email, notification.Channel);
        Assert.Equal(sentAt, notification.SentAtUtc);
        Assert.Equal(AwaRecipient, notification.RecipientId);
        Assert.Equal(Resend, notification.ProviderCode);
        Assert.Equal(NotificationTypes.ReservationConfirmed.Key, notification.TemplateKey);
        Assert.Equal(
            [NotificationDeliveryStatus.Failed, NotificationDeliveryStatus.Sent],
            notification.Deliveries.Select(attempt => attempt.Status));
    }

    [Fact]
    public void Redact_raises_NotificationRedacted_with_ids_only()
    {
        var notification = Sent();

        notification.Redact(_clock);

        Assert.Equal(
            new NotificationRedacted(_tenantId, notification.Id, _clock.UtcNow),
            DomainAssert.Raised<NotificationRedacted>(notification));
    }

    [Fact]
    public void A_redacted_sent_notification_can_still_be_reported_delivered()
    {
        var notification = Sent();
        notification.Redact(_clock);

        notification.MarkAsDelivered(_clock);

        Assert.Equal(NotificationStatus.Delivered, notification.Status);
        AssertRedacted(notification);
    }

    [Fact]
    public void Redact_is_allowed_once_nothing_can_be_sent_any_more()
    {
        foreach (var notification in new[] { Delivered(), Cancelled(), Abandoned() })
        {
            notification.Redact(_clock);
            AssertRedacted(notification);
        }
    }

    [Fact]
    public void Redact_is_rejected_while_the_notification_can_still_be_sent()
    {
        DomainAssert.Throws(NotificationErrors.NotRedactable, () => Pending().Redact(_clock));
        DomainAssert.Throws(NotificationErrors.NotRedactable, () => Scheduled().Redact(_clock));
        DomainAssert.Throws(NotificationErrors.NotRedactable, () => Processing().Redact(_clock));
        DomainAssert.Throws(NotificationErrors.NotRedactable, () => Failed().Redact(_clock));
    }

    [Fact]
    public void Redact_twice_is_a_silent_no_op()
    {
        var notification = Sent();
        notification.Redact(_clock);
        var redactedAt = notification.RedactedAtUtc;
        notification.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromDays(1));

        notification.Redact(_clock);

        Assert.Equal(redactedAt, notification.RedactedAtUtc);
        DomainAssert.NoEvents(notification);
    }

    [Fact]
    public void The_content_never_shows_in_its_text_form()
    {
        var otp = NotificationContent.Create(NotificationChannel.Sms, null, "Votre code : 482913");

        Assert.DoesNotContain("482913", otp.ToString(), StringComparison.Ordinal);
    }
}
