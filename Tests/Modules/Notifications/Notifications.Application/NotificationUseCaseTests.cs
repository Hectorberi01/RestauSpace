using Notifications.Application.Abstractions;
using Notifications.Application.Commands;
using Notifications.Application.Queries;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>Tests obligatoires du § 42 au niveau des cas d'usage (ports en mémoire).</summary>
public sealed class NotificationUseCaseTests
{
    private readonly NotificationsFixture _fixture = new();

    private TenantId A => _fixture.TenantA;

    private TenantId B => _fixture.TenantB;

    private (CustomerId Customer, RestaurantNotificationConfiguration Configuration) EmailReady(TenantId tenant)
    {
        var configuration = _fixture.Configured(tenant, NotificationsFixture.TestMail, NotificationChannel.Email);
        _fixture.Template(null, NotificationTypes.OrderReady, NotificationChannel.Email, "Bonjour {{customer.firstName}}, commande {{order.number}} prête.", "Commande prête");
        return (_fixture.Customer(tenant), configuration);
    }

    private Task<SendNotificationResult> SendOrderReadyAsync(CustomerId customer, NotificationChannel channel = NotificationChannel.Email, Guid? locationId = null, DateTimeOffset? at = null) =>
        _fixture.Send.HandleAsync(new SendNotificationCommand(
            "order.ready", channel, customer.Value, LocationId: locationId, Variables: new Dictionary<string, string> { ["order.number"] = "A-12" },
            ScheduledAtUtc: at));

    private async Task DispatchAllAsync()
    {
        foreach (var notification in _fixture.NotificationRows.Where(candidate => candidate.IsDueAt(_fixture.Clock.UtcNow)).ToList())
        {
            _fixture.CurrentTenantId = notification.TenantId;
            await _fixture.Dispatch.HandleAsync(new DispatchNotificationCommand(notification.Id.Value));
        }

        _fixture.CurrentTenantId = A;
    }

    // ---- Envoi, templates, prestataires

    [Fact]
    public async Task A_notification_is_rendered_from_the_platform_template_and_sent_by_the_restaurant_provider()
    {
        var (customer, _) = EmailReady(A);

        var result = await SendOrderReadyAsync(customer);
        await DispatchAllAsync();

        var notification = _fixture.Single();
        Assert.Equal([notification.Id.Value], result.NotificationIds);
        Assert.Null(result.Skipped);
        Assert.Equal(NotificationStatus.Sent, notification.Status);
        var sent = Assert.Single(_fixture.Sent);
        Assert.Equal("awa@example.com", sent.Recipient.Address);
        Assert.Equal("Bonjour Awa, commande A-12 prête.", sent.Content.Body);
        Assert.Equal(NotificationsFixture.TestMail, notification.ProviderCode);
        Assert.False(_fixture.UsageRows.ContainsKey((A, NotificationChannel.Email))); // compte du restaurant : non compté
    }

    [Fact]
    public async Task The_platform_sees_the_send_log_without_recipient_and_the_counts_of_the_month()
    {
        var (customer, _) = EmailReady(A);
        await SendOrderReadyAsync(customer);
        await DispatchAllAsync();

        var overview = await _fixture.Overview.HandleAsync(new GetNotificationsOverviewQuery());
        var log = Assert.Single(await _fixture.SendLog.HandleAsync(new ListPlatformNotificationsQuery(NotificationChannel.Email)));
        var none = await _fixture.SendLog.HandleAsync(new ListPlatformNotificationsQuery(Status: NotificationStatus.Failed));

        Assert.Equal((1, 1, 0), (overview.Created, overview.HandedOver, overview.Failed));
        Assert.Equal((NotificationChannel.Email, 1), (Assert.Single(overview.Channels).Channel, overview.Channels[0].HandedOver));
        Assert.NotNull(overview.AverageSendSeconds);
        Assert.Equal((A.Value, NotificationStatus.Sent, 1), (log.TenantId, log.Status, log.AttemptCount));
        Assert.DoesNotContain(typeof(NotificationLogDto).GetProperties(), property => property.Name is "Recipient" or "Body" or "Subject");
        Assert.Empty(none);
    }

    [Fact]
    public async Task The_restaurant_template_wins_over_the_platform_one()
    {
        var (customer, _) = EmailReady(A);
        _fixture.Template(A, NotificationTypes.OrderReady, NotificationChannel.Email, "Chez nous : {{order.number}} est prête !", "Prête");

        await SendOrderReadyAsync(customer);

        Assert.Equal("Chez nous : A-12 est prête !", _fixture.Single().Content.Body);
    }

    [Fact]
    public async Task A_disabled_restaurant_template_falls_back_to_the_platform_one()
    {
        var (customer, _) = EmailReady(A);
        _fixture.Template(A, NotificationTypes.OrderReady, NotificationChannel.Email, "Chez nous : {{order.number}}", "Prête").Disable(_fixture.Clock);

        await SendOrderReadyAsync(customer);

        Assert.StartsWith("Bonjour Awa", _fixture.Single().Content.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_template_or_provider_nothing_is_created()
    {
        var customer = _fixture.Customer(A);
        _fixture.Configured(A, NotificationsFixture.TestMail, NotificationChannel.Email);

        var noTemplate = await SendOrderReadyAsync(customer);
        _fixture.Template(null, NotificationTypes.OrderReady, NotificationChannel.Sms, "Commande {{order.number}} prête.");
        var noSmsProvider = await SendOrderReadyAsync(customer, NotificationChannel.Sms);

        Assert.Equal(NotificationSkipReason.NoTemplate, noTemplate.Skipped);
        Assert.Equal(NotificationSkipReason.NoProvider, noSmsProvider.Skipped);
        Assert.Empty(_fixture.NotificationRows);
    }

    [Fact]
    public async Task A_provider_disabled_by_the_platform_is_not_used()
    {
        var (customer, _) = EmailReady(A);
        _fixture.ProviderRows.Single(provider => provider.Code == NotificationsFixture.TestMail).Disable(_fixture.Clock);

        var result = await SendOrderReadyAsync(customer);

        Assert.Equal(NotificationSkipReason.NoProvider, result.Skipped);
    }

    [Fact]
    public async Task A_provider_disabled_by_the_restaurant_is_not_used()
    {
        var (customer, configuration) = EmailReady(A);
        configuration.Disable(_fixture.Clock);

        var result = await SendOrderReadyAsync(customer);

        Assert.Equal(NotificationSkipReason.NoProvider, result.Skipped);
    }

    [Fact]
    public async Task A_channel_reserved_to_other_locations_is_not_used()
    {
        var (customer, configuration) = EmailReady(A);
        var elsewhere = _fixture.Location(A);
        var here = _fixture.Location(A);
        configuration.AssignToLocation(elsewhere, 0, _fixture.Clock);

        var result = await SendOrderReadyAsync(customer, locationId: here.LocationId.Value);

        Assert.Equal(NotificationSkipReason.NoProvider, result.Skipped);
    }

    [Fact]
    public async Task A_template_with_a_variable_without_value_is_not_sent()
    {
        var (customer, _) = EmailReady(A);

        var result = await _fixture.Send.HandleAsync(new SendNotificationCommand("order.ready", NotificationChannel.Email, customer.Value));

        Assert.Equal(NotificationSkipReason.TemplateIncomplete, result.Skipped);
    }

    [Fact]
    public async Task An_unknown_template_variable_is_rejected_at_creation() =>
        await Assert.ThrowsAsync<DomainException>(() => _fixture.CreateTemplate.HandleAsync(new CreateTemplateCommand(
            TemplateOwner.Tenant, "order.ready", NotificationChannel.Sms, "fr", null, "Code {{otp.code}}")));

    [Fact]
    public async Task An_invalid_address_is_rejected()
    {
        EmailReady(A);

        var exception = await Assert.ThrowsAsync<DomainException>(() => _fixture.Send.HandleAsync(
            new SendNotificationCommand("order.ready", NotificationChannel.Email, Address: "pas-une-adresse")));

        Assert.Equal(CommonErrors.EmailInvalid.Code, exception.Code);
    }

    [Fact]
    public async Task A_location_of_another_tenant_is_rejected()
    {
        var (customer, _) = EmailReady(A);
        var foreign = _fixture.Location(B);

        await Assert.ThrowsAsync<NotFoundException>(() => SendOrderReadyAsync(customer, locationId: foreign.LocationId.Value));
    }

    [Fact]
    public async Task A_customer_of_another_tenant_is_not_found()
    {
        EmailReady(A);
        var foreign = _fixture.Customer(B);

        await Assert.ThrowsAsync<NotFoundException>(() => SendOrderReadyAsync(foreign));
    }

    // ---- Programmation, relances

    [Fact]
    public async Task A_scheduled_notification_is_not_sent_before_its_time_and_is_sent_after()
    {
        var (customer, _) = EmailReady(A);
        var at = _fixture.Clock.UtcNow.AddHours(2);

        await SendOrderReadyAsync(customer, at: at);
        await DispatchAllAsync();
        Assert.Empty(_fixture.Sent);
        Assert.Equal(NotificationStatus.Scheduled, _fixture.Single().Status);

        _fixture.Clock.Set(at);
        await DispatchAllAsync();
        Assert.Single(_fixture.Sent);
    }

    [Fact]
    public async Task A_temporary_failure_is_retried_and_a_permanent_one_is_not()
    {
        var (customer, _) = EmailReady(A);
        await SendOrderReadyAsync(customer);
        _fixture.NextResults.Enqueue(NotificationSendResult.Failed(NotificationFailure.Temporary(NotificationFailure.Timeout)));
        await DispatchAllAsync();
        var notification = _fixture.Single();
        Assert.Equal(NotificationStatus.Pending, notification.Status);

        _fixture.Clock.Set(notification.NextAttemptAtUtc!.Value);
        _fixture.NextResults.Enqueue(NotificationSendResult.Failed(NotificationFailure.Permanent(NotificationFailure.InvalidEmail)));
        await DispatchAllAsync();

        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.True(notification.IsDeliveryAbandoned);
        Assert.Equal(2, _fixture.Sent.Count);
        _fixture.Clock.Advance(TimeSpan.FromDays(1));
        await DispatchAllAsync();
        Assert.Equal(2, _fixture.Sent.Count);
    }

    [Fact]
    public async Task The_maximum_number_of_attempts_is_respected()
    {
        var (customer, _) = EmailReady(A);
        await SendOrderReadyAsync(customer);
        var notification = _fixture.Single();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            _fixture.NextResults.Enqueue(NotificationSendResult.Failed(NotificationFailure.Temporary(NotificationFailure.Timeout)));
            await DispatchAllAsync();
            _fixture.Clock.Advance(TimeSpan.FromHours(1));
        }

        Assert.Equal(NotificationRetryPolicy.Default.MaxAttempts, _fixture.Sent.Count);
        Assert.True(notification.IsDeliveryAbandoned);
    }

    // ---- Préférences, consentement, quotas

    [Fact]
    public async Task An_opt_out_is_respected()
    {
        var (customer, _) = EmailReady(A);
        await _fixture.SetPreference.HandleAsync(new SetNotificationPreferenceCommand(
            new PreferenceSubject(customer.Value), NotificationPurpose.Transactional, NotificationChannel.Email, IsAllowed: false));

        var result = await SendOrderReadyAsync(customer);

        Assert.Equal(NotificationSkipReason.OptedOut, result.Skipped);
    }

    [Fact]
    public async Task A_team_member_turns_off_one_alert_type_and_keeps_the_others()
    {
        await _fixture.SetAlertType.HandleAsync(new SetAlertTypeCommand("staff.low_stock", IsEnabled: false));

        var preferences = await _fixture.GetPreferences.HandleAsync(new GetNotificationPreferencesQuery(new PreferenceSubject(null)));
        var otp = await Assert.ThrowsAsync<DomainException>(() => _fixture.SetAlertType.HandleAsync(new SetAlertTypeCommand("security.otp", IsEnabled: false)));
        var unknown = await Assert.ThrowsAsync<DomainException>(() => _fixture.SetAlertType.HandleAsync(new SetAlertTypeCommand("staff.nothing", IsEnabled: false)));

        Assert.False(Assert.Single(preferences.Alerts, alert => alert.Type == "staff.low_stock").IsEnabled);
        Assert.True(Assert.Single(preferences.Alerts, alert => alert.Type == "staff.new_review").IsEnabled);
        Assert.DoesNotContain(preferences.Alerts, alert => !alert.Type.StartsWith("staff.", StringComparison.Ordinal));
        Assert.Equal(NotificationPreferenceErrors.TypeNotMutable, otp.Error);
        Assert.Equal(NotificationPreferenceErrors.TypeNotMutable, unknown.Error);
    }

    [Fact]
    public async Task A_marketing_message_needs_the_customer_consent()
    {
        var customer = _fixture.Customer(A);
        _fixture.Configured(A, NotificationsFixture.TestMail, NotificationChannel.Email);
        _fixture.Template(
            null, NotificationTypes.NewMenu, NotificationChannel.Email, "Nouveau menu ! Se désinscrire : {{unsubscribe.url}}", "Nouveau menu");
        var command = new SendNotificationCommand(
            "marketing.new_menu", NotificationChannel.Email, customer.Value, Variables: new Dictionary<string, string> { ["unsubscribe.url"] = "https://u.example.com/x" });

        var withoutConsent = await _fixture.Send.HandleAsync(command);
        _fixture.Consents[(A, customer, CommunicationChannel.Email)] = true;
        var withConsent = await _fixture.Send.HandleAsync(command);

        Assert.Equal(NotificationSkipReason.ConsentNotGranted, withoutConsent.Skipped);
        Assert.Single(withConsent.NotificationIds);
        Assert.Equal(NotificationPurpose.Marketing, _fixture.Single().Purpose);
    }

    [Fact]
    public async Task A_consent_withdrawn_before_sending_stops_the_marketing_message()
    {
        var customer = _fixture.Customer(A);
        _fixture.Configured(A, NotificationsFixture.TestMail, NotificationChannel.Email);
        _fixture.Template(null, NotificationTypes.NewMenu, NotificationChannel.Email, "Menu ! {{unsubscribe.url}}", "Menu");
        _fixture.Consents[(A, customer, CommunicationChannel.Email)] = true;
        await _fixture.Send.HandleAsync(new SendNotificationCommand(
            "marketing.new_menu", NotificationChannel.Email, customer.Value, Variables: new Dictionary<string, string> { ["unsubscribe.url"] = "https://u.example.com/x" }));

        _fixture.Consents[(A, customer, CommunicationChannel.Email)] = false;
        await DispatchAllAsync();

        Assert.Empty(_fixture.Sent);
        Assert.Equal(NotificationStatus.Cancelled, _fixture.Single().Status);
    }

    [Fact]
    public async Task A_marketing_email_carries_a_signed_unsubscribe_link_that_opts_the_customer_out()
    {
        var customer = _fixture.Customer(A);
        _fixture.Configured(A, NotificationsFixture.TestMail, NotificationChannel.Email);
        _fixture.Template(null, NotificationTypes.NewMenu, NotificationChannel.Email, "Menu ! {{unsubscribe.url}}", "Menu");
        _fixture.Consents[(A, customer, CommunicationChannel.Email)] = true;
        var command = new SendNotificationCommand(
            "marketing.new_menu", NotificationChannel.Email, customer.Value, Variables: new Dictionary<string, string> { ["unsubscribe.url"] = "https://evil.example.com/x" });

        await _fixture.Send.HandleAsync(command);

        var (token, claim) = Assert.Single(_fixture.IssuedLinks);
        Assert.Equal(new UnsubscribeClaim(A, customer, NotificationChannel.Email), claim);
        Assert.Contains($"https://site.example.com/desinscription?jeton={token}", _fixture.SavedBodies[^1], StringComparison.Ordinal);
        Assert.DoesNotContain("evil", _fixture.SavedBodies[^1], StringComparison.Ordinal);

        Assert.Equal(new UnsubscribeLinkDto("Chez A", NotificationChannel.Email), await _fixture.UnsubscribePreview.HandleAsync(new GetUnsubscribeLinkQuery(token)));
        Assert.Empty(_fixture.PreferenceRows);

        _fixture.CurrentTenantId = null;
        await _fixture.Unsubscribe.HandleAsync(new UnsubscribeCommand(token));
        await _fixture.Unsubscribe.HandleAsync(new UnsubscribeCommand(token));
        _fixture.CurrentTenantId = A;

        var preference = Assert.Single(_fixture.PreferenceRows);
        Assert.False(preference.Allows(NotificationPurpose.Marketing, NotificationChannel.Email));
        Assert.True(preference.Allows(NotificationPurpose.Transactional, NotificationChannel.Email));
        Assert.Equal(NotificationSkipReason.OptedOut, (await _fixture.Send.HandleAsync(command)).Skipped);
        var forged = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Unsubscribe.HandleAsync(new UnsubscribeCommand("forged")));
        Assert.Equal("notifications.unsubscribe.link_invalid", forged.Code);
    }

    [Fact]
    public async Task Without_an_unsubscribe_page_no_marketing_email_is_sent()
    {
        var customer = _fixture.Customer(A);
        _fixture.Configured(A, NotificationsFixture.TestMail, NotificationChannel.Email);
        _fixture.Template(null, NotificationTypes.NewMenu, NotificationChannel.Email, "Menu ! {{unsubscribe.url}}", "Menu");
        _fixture.Consents[(A, customer, CommunicationChannel.Email)] = true;
        _fixture.Settings.UnsubscribePageUrl = null;

        var result = await _fixture.Send.HandleAsync(new SendNotificationCommand(
            "marketing.new_menu", NotificationChannel.Email, customer.Value, Variables: new Dictionary<string, string> { ["unsubscribe.url"] = "https://evil.example.com/x" }));

        Assert.Empty(result.NotificationIds);
        Assert.Empty(_fixture.IssuedLinks);
    }

    [Fact]
    public async Task The_monthly_quota_is_respected_except_for_security_messages()
    {
        _fixture.PlatformConfigured(A, NotificationsFixture.TestMail, NotificationChannel.Email);
        _fixture.Template(null, NotificationTypes.OrderReady, NotificationChannel.Email, "Commande {{order.number}} prête.", "Commande prête");
        var customer = _fixture.Customer(A);
        _fixture.Limits[NotificationChannel.Email] = 1;
        _fixture.UsageRows[(A, NotificationChannel.Email)] = 1;

        var result = await SendOrderReadyAsync(customer);

        Assert.Equal(NotificationSkipReason.QuotaExceeded, result.Skipped);
    }

    [Fact]
    public async Task The_quota_only_counts_and_limits_sends_through_the_platform_account()
    {
        var (customer, _) = EmailReady(A);
        _fixture.Limits[NotificationChannel.Email] = 1;
        _fixture.UsageRows[(A, NotificationChannel.Email)] = 5;

        var result = await SendOrderReadyAsync(customer);
        await DispatchAllAsync();

        Assert.Null(result.Skipped);
        Assert.Single(_fixture.Sent);
        Assert.Equal(5, _fixture.UsageRows[(A, NotificationChannel.Email)]); // compte du restaurant : non compté
    }

    [Fact]
    public async Task Beyond_the_quota_of_the_platform_account_the_own_account_of_the_restaurant_is_used()
    {
        _fixture.PlatformConfigured(A, NotificationsFixture.OtherMail, NotificationChannel.Email);
        var (customer, _) = EmailReady(A);
        _fixture.UsageRows[(A, NotificationChannel.Email)] = 0;

        await SendOrderReadyAsync(customer);
        await DispatchAllAsync();
        Assert.Equal(NotificationsFixture.OtherMail, _fixture.NotificationRows.ElementAt(0).ProviderCode);
        Assert.Equal(1, _fixture.UsageRows[(A, NotificationChannel.Email)]); // compte de la plateforme : compté

        _fixture.Limits[NotificationChannel.Email] = 1;
        var second = await _fixture.Send.HandleAsync(new SendNotificationCommand(
            "order.ready", NotificationChannel.Email, customer.Value, Variables: new Dictionary<string, string> { ["order.number"] = "A-13" }));
        await DispatchAllAsync();

        Assert.Null(second.Skipped);
        Assert.Equal(NotificationsFixture.TestMail, _fixture.NotificationRows.ElementAt(1).ProviderCode);
        Assert.Equal(1, _fixture.UsageRows[(A, NotificationChannel.Email)]);
    }

    [Fact]
    public async Task Without_own_account_in_the_plan_a_restaurant_account_is_neither_set_up_nor_used()
    {
        var (customer, configuration) = EmailReady(A);
        var provider = _fixture.Provider(NotificationsFixture.TestSms, NotificationChannel.Sms);
        _fixture.DeniedFeatures.Add(PlanFeatures.NotificationsOwnAccount);

        var result = await SendOrderReadyAsync(customer);
        var refused = new[]
        {
            await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.CreateConfiguration.HandleAsync(
                new CreateNotificationConfigurationCommand(provider.Id.Value, NotificationProviderMode.TenantManaged))),
            await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.SetCredentials.HandleAsync(
                new SetConfigurationCredentialsCommand(configuration.Id.Value, new Dictionary<string, string> { ["api_key"] = "k" }))),
            await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.TestConfiguration.HandleAsync(
                new TestNotificationConfigurationCommand(configuration.Id.Value, "chef@example.com"))),
        };
        await _fixture.DisableConfiguration.HandleAsync(new DisableNotificationConfigurationCommand(configuration.Id.Value));
        var enable = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.EnableConfiguration.HandleAsync(
            new EnableNotificationConfigurationCommand(configuration.Id.Value)));
        await _fixture.CreateConfiguration.HandleAsync(new CreateNotificationConfigurationCommand(provider.Id.Value, NotificationProviderMode.PlatformManaged));

        Assert.Equal(NotificationSkipReason.NoProvider, result.Skipped);
        Assert.True(refused.Append(enable).All(error => error.Code == PlanFeatures.RequiredCode));
    }

    // ---- Idempotence

    [Fact]
    public async Task A_duplicated_integration_event_does_not_create_two_notifications()
    {
        var (customer, _) = EmailReady(A);
        var command = new TriggerNotificationsCommand(
            "notifications.triggers", "orders:42", "OrderReady",
            [new NotificationTrigger("order.ready", [NotificationChannel.Email], customer.Value, null, null, new Dictionary<string, string> { ["order.number"] = "A-12" })]);

        var first = await _fixture.Trigger.HandleAsync(command);
        var second = await _fixture.Trigger.HandleAsync(command);

        Assert.Equal((1, 0), (first, second));
        Assert.Single(_fixture.NotificationRows);
    }

    [Fact]
    public async Task A_replayed_request_with_the_same_idempotency_key_creates_nothing_more()
    {
        var (customer, _) = EmailReady(A);
        var command = new SendNotificationCommand(
            "order.ready", NotificationChannel.Email, customer.Value, Variables: new Dictionary<string, string> { ["order.number"] = "A-12" },
            IdempotencyKey: "pos-123");

        await _fixture.Send.HandleAsync(command);
        var replay = await _fixture.Send.HandleAsync(command);

        Assert.Equal(NotificationSkipReason.Duplicate, replay.Skipped);
        Assert.Single(_fixture.NotificationRows);
    }

    [Fact]
    public async Task A_trigger_falls_back_to_the_next_channel_when_the_first_is_not_possible()
    {
        var customer = _fixture.Customer(A, email: null);
        _fixture.Configured(A, NotificationsFixture.TestSms, NotificationChannel.Sms);
        _fixture.Template(null, NotificationTypes.OrderReady, NotificationChannel.Sms, "Commande {{order.number}} prête.");

        await _fixture.Trigger.HandleAsync(new TriggerNotificationsCommand(
            "notifications.triggers", "orders:43", "OrderReady",
            [new NotificationTrigger("order.ready", [NotificationChannel.Email, NotificationChannel.Sms], customer.Value, null, null,
                new Dictionary<string, string> { ["order.number"] = "A-13" })]));

        Assert.Equal(NotificationChannel.Sms, _fixture.Single().Channel);
    }

    // ---- Isolation entre tenants

    [Fact]
    public async Task Tenant_A_cannot_read_a_notification_of_tenant_B()
    {
        var (customer, _) = EmailReady(B);
        _fixture.CurrentTenantId = B;
        await SendOrderReadyAsync(customer);
        var foreign = _fixture.Single().Id.Value;

        _fixture.CurrentTenantId = A;

        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetNotification.HandleAsync(new GetNotificationQuery(foreign)));
        Assert.Empty(await _fixture.ListNotifications.HandleAsync(new ListNotificationsQuery()));
    }

    [Fact]
    public async Task Tenant_A_cannot_change_a_template_of_tenant_B_nor_of_the_platform()
    {
        var foreign = _fixture.Template(B, NotificationTypes.OrderReady, NotificationChannel.Sms, "Prête.");
        var platform = _fixture.Template(null, NotificationTypes.OrderReady, NotificationChannel.Sms, "Prête !");

        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.ChangeTemplate.HandleAsync(
            new ChangeTemplateCommand(TemplateOwner.Tenant, foreign.Id.Value, TemplateAction.Disable)));
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.UpdateTemplate.HandleAsync(
            new UpdateTemplateCommand(TemplateOwner.Tenant, platform.Id.Value, null, "Piraté")));
        Assert.True(foreign.IsEnabled);
        Assert.Equal("Prête !", platform.Body);
    }

    [Fact]
    public async Task The_tenant_comes_from_the_context_never_from_the_request()
    {
        _fixture.CurrentTenantId = null;

        await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.Send.HandleAsync(
            new SendNotificationCommand("order.ready", NotificationChannel.Email, Address: "awa@example.com")));
    }

    // ---- Secrets

    [Fact]
    public async Task Credentials_go_to_the_vault_of_the_tenant_and_are_never_returned()
    {
        var provider = _fixture.Provider(NotificationsFixture.TestMail, NotificationChannel.Email);
        var id = await _fixture.CreateConfiguration.HandleAsync(new CreateNotificationConfigurationCommand(provider.Id.Value, NotificationProviderMode.TenantManaged));

        await _fixture.SetCredentials.HandleAsync(new SetConfigurationCredentialsCommand(id, new Dictionary<string, string> { ["api_key"] = "re_SECRET_123" }));
        var listed = await _fixture.ListConfigurations.HandleAsync(new ListNotificationConfigurationsQuery());

        var reference = NotificationCredentialReference.ForTenant(A, NotificationsFixture.TestMail);
        Assert.Equal("re_SECRET_123", _fixture.Secrets[reference.Value]["api_key"]);
        var dto = Assert.Single(listed);
        Assert.True(dto.HasCredentials);
        Assert.DoesNotContain("re_SECRET_123", System.Text.Json.JsonSerializer.Serialize(listed), StringComparison.Ordinal);
        Assert.DoesNotContain(typeof(NotificationConfigurationDto).GetProperties(), property => property.Name.Contains("Secret", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_restaurant_sends_a_fixed_test_message_through_its_own_configuration()
    {
        var provider = _fixture.Provider(NotificationsFixture.TestMail, NotificationChannel.Email);
        var id = await _fixture.CreateConfiguration.HandleAsync(new CreateNotificationConfigurationCommand(provider.Id.Value, NotificationProviderMode.TenantManaged));

        var missing = await _fixture.TestConfiguration.HandleAsync(new TestNotificationConfigurationCommand(id, "chef@example.com"));
        await _fixture.SetCredentials.HandleAsync(new SetConfigurationCredentialsCommand(id, new Dictionary<string, string> { ["api_key"] = "re_SECRET_123" }));
        var badAddress = await Assert.ThrowsAsync<DomainException>(() => _fixture.TestConfiguration.HandleAsync(new TestNotificationConfigurationCommand(id, "pas-une-adresse")));
        var sent = await _fixture.TestConfiguration.HandleAsync(new TestNotificationConfigurationCommand(id, " chef@example.com "));
        _fixture.NextResults.Enqueue(NotificationSendResult.Failed(NotificationFailure.Temporary("PROVIDER_AUTH_FAILED", "401 for key re_SECRET_123")));
        var refused = await _fixture.TestConfiguration.HandleAsync(new TestNotificationConfigurationCommand(id, "chef@example.com"));
        _fixture.CurrentTenantId = _fixture.TenantB;
        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.TestConfiguration.HandleAsync(new TestNotificationConfigurationCommand(id, "chef@example.com")));

        Assert.Equal(NotificationConfigurationTestOutcomes.CredentialsMissing, missing.Outcome);
        Assert.NotNull(badAddress);
        Assert.Equal(NotificationConfigurationTestOutcomes.Sent, sent.Outcome);
        Assert.True(sent.Succeeded);
        Assert.Equal(2, _fixture.Sent.Count);
        Assert.Equal("chef@example.com", _fixture.Sent[0].Recipient.Address);
        Assert.Equal(TestNotificationConfigurationHandler.Subject, _fixture.Sent[0].Content.Subject);
        Assert.Equal(NotificationConfigurationTestOutcomes.Failed, refused.Outcome);
        // Résultat binaire : aucun code ne distingue un refus d'identifiants d'un délai ou d'un serveur injoignable.
        Assert.Equal((null, false), (refused.FailureCode, refused.IsPermanent));
        Assert.DoesNotContain("re_SECRET_123", System.Text.Json.JsonSerializer.Serialize(refused), StringComparison.Ordinal);
        Assert.Empty(_fixture.NotificationRows);
    }

    [Fact]
    public async Task Credentials_must_match_the_integration_fields()
    {
        var provider = _fixture.Provider(NotificationsFixture.TestMail, NotificationChannel.Email);
        var id = await _fixture.CreateConfiguration.HandleAsync(new CreateNotificationConfigurationCommand(provider.Id.Value, NotificationProviderMode.TenantManaged));

        var exception = await Assert.ThrowsAsync<DomainException>(() => _fixture.SetCredentials.HandleAsync(
            new SetConfigurationCredentialsCommand(id, new Dictionary<string, string> { ["api_key"] = "k", ["unexpected"] = "x" })));

        Assert.Equal(NotificationsApplicationErrors.CredentialsIncomplete.Code, exception.Code);
        Assert.Empty(_fixture.Secrets);
    }

    // ---- OTP

    private void OtpReady()
    {
        _fixture.Configured(A, NotificationsFixture.TestSms, NotificationChannel.Sms);
        _fixture.Template(null, NotificationTypes.SecurityOtp, NotificationChannel.Sms, "Code : {{otp.code}} ({{otp.expiresInMinutes}} min)");
    }

    private string LastCode() => _fixture.Sent.Last().Content.Body.Split(' ')[2];

    [Fact]
    public async Task An_otp_is_sent_but_never_stored_in_clear()
    {
        OtpReady();

        var requested = await _fixture.RequestOtp.HandleAsync(new RequestOtpCommand("phone_verification", NotificationChannel.Sms, "+22997000000"));

        var code = LastCode();
        Assert.Matches("^[0-9]{6}$", code);
        Assert.DoesNotContain(_fixture.SavedBodies, body => body.Contains(code, StringComparison.Ordinal));
        var notification = _fixture.Single();
        Assert.True(notification.IsRedacted);
        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.DoesNotContain(code, _fixture.OtpRows.Single().CodeHash.Value, StringComparison.Ordinal);
        Assert.Equal(_fixture.Clock.UtcNow + _fixture.Settings.OtpLifetime, requested.ExpiresAtUtc);

        await _fixture.VerifyOtp.HandleAsync(new VerifyOtpCommand(requested.ChallengeId, code));
    }

    [Fact]
    public async Task A_used_or_expired_otp_is_rejected()
    {
        OtpReady();
        var first = await _fixture.RequestOtp.HandleAsync(new RequestOtpCommand("login", NotificationChannel.Sms, "+22997000000"));
        var code = LastCode();
        await _fixture.VerifyOtp.HandleAsync(new VerifyOtpCommand(first.ChallengeId, code));

        var reused = await Assert.ThrowsAsync<DomainException>(() => _fixture.VerifyOtp.HandleAsync(new VerifyOtpCommand(first.ChallengeId, code)));

        _fixture.Clock.Advance(_fixture.Settings.OtpCooldown);
        var second = await _fixture.RequestOtp.HandleAsync(new RequestOtpCommand("login", NotificationChannel.Sms, "+22997000000"));
        _fixture.Clock.Advance(_fixture.Settings.OtpLifetime);
        var expired = await Assert.ThrowsAsync<DomainException>(() => _fixture.VerifyOtp.HandleAsync(new VerifyOtpCommand(second.ChallengeId, LastCode())));

        Assert.Equal(NotificationsApplicationErrors.OtpAlreadyUsed.Code, reused.Code);
        Assert.Equal(NotificationsApplicationErrors.OtpExpired.Code, expired.Code);
    }

    [Fact]
    public async Task Wrong_codes_are_counted_even_when_the_answer_is_an_error()
    {
        OtpReady();
        var requested = await _fixture.RequestOtp.HandleAsync(new RequestOtpCommand("login", NotificationChannel.Sms, "+22997000000"));

        for (var attempt = 1; attempt < _fixture.Settings.OtpMaxAttempts; attempt++)
        {
            var invalid = await Assert.ThrowsAsync<DomainException>(() => _fixture.VerifyOtp.HandleAsync(new VerifyOtpCommand(requested.ChallengeId, "000000x")));
            Assert.Equal(NotificationsApplicationErrors.OtpInvalid.Code, invalid.Code);
        }

        var exhausted = await Assert.ThrowsAsync<DomainException>(() => _fixture.VerifyOtp.HandleAsync(new VerifyOtpCommand(requested.ChallengeId, "000000x")));
        var right = await Assert.ThrowsAsync<DomainException>(() => _fixture.VerifyOtp.HandleAsync(new VerifyOtpCommand(requested.ChallengeId, LastCode())));

        Assert.Equal(NotificationsApplicationErrors.OtpExhausted.Code, exhausted.Code);
        Assert.Equal(NotificationsApplicationErrors.OtpExhausted.Code, right.Code);
    }

    [Fact]
    public async Task Successive_otp_requests_are_limited_and_a_new_code_cancels_the_previous_one()
    {
        OtpReady();
        var first = await _fixture.RequestOtp.HandleAsync(new RequestOtpCommand("login", NotificationChannel.Sms, "+22997000000"));
        var firstCode = LastCode();

        await Assert.ThrowsAsync<TooManyRequestsException>(() => _fixture.RequestOtp.HandleAsync(new RequestOtpCommand("login", NotificationChannel.Sms, "+229 97 00 00 00")));
        _fixture.Clock.Advance(_fixture.Settings.OtpCooldown);
        await _fixture.RequestOtp.HandleAsync(new RequestOtpCommand("login", NotificationChannel.Sms, "+22997000000"));

        var cancelled = await Assert.ThrowsAsync<DomainException>(() => _fixture.VerifyOtp.HandleAsync(new VerifyOtpCommand(first.ChallengeId, firstCode)));
        Assert.Equal(NotificationsApplicationErrors.OtpExpired.Code, cancelled.Code);
    }

    [Fact]
    public async Task An_otp_that_could_not_be_sent_is_reported_and_its_content_is_erased()
    {
        OtpReady();
        _fixture.NextResults.Enqueue(NotificationSendResult.Failed(NotificationFailure.Temporary(NotificationFailure.ProviderUnavailable)));

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.RequestOtp.HandleAsync(new RequestOtpCommand("login", NotificationChannel.Sms, "+22997000000")));

        Assert.Equal(NotificationsApplicationErrors.OtpNotSent.Code, exception.Code);
        var notification = _fixture.Single();
        Assert.True(notification.IsRedacted);
        Assert.Equal(NotificationStatus.Cancelled, notification.Status);
        Assert.Equal(OtpStatus.Cancelled, _fixture.OtpRows.Single().Status);
    }
}
