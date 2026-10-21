using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application;
using Notifications.Application.Abstractions;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence;
using Notifications.Infrastructure.Webhooks;

namespace Notifications.Infrastructure;

/// <summary>
/// Étapes 11-12, 20-23, 31 sur un vrai PostgreSQL : aller-retour des agrégats, isolation par tenant (filtres ET
/// repositories), index uniques de dernier recours, inbox, consommation, coffre chiffré, webhooks signés.
/// </summary>
public sealed class NotificationsPersistenceTests(NotificationsDatabase database) : IClassFixture<NotificationsDatabase>
{
    private static readonly NotificationLanguage French = NotificationLanguage.Create("fr");

    private FakeClock Clock => database.Clock;

    private Notification EmailNotification(TenantId? tenant, string address = "awa@example.com", RecipientId? recipient = null)
    {
        var content = NotificationContent.Create(NotificationChannel.Email, "Commande prête", "Bonjour Awa, votre commande est prête.");
        return tenant is { } owner
            ? Notification.Create(
                owner, NotificationTypes.OrderReady, NotificationRecipient.ForEmail(Email.Create(address)), recipient,
                NotificationProviderCode.Create("FAKE"), French, content, Clock)
            : Notification.CreateForPlatform(
                NotificationTypes.AccountPasswordReset, NotificationRecipient.ForEmail(Email.Create(address)), recipient,
                NotificationProviderCode.Create("FAKE"), French, content, Clock);
    }

    private async Task<Notification> SavedAsync(Notification notification, string? key = null)
    {
        await using var scope = database.NewScope(notification.TenantId);
        scope.ServiceProvider.GetRequiredService<INotificationRepository>().Add(notification, key);
        await scope.ServiceProvider.GetRequiredService<INotificationsUnitOfWork>().SaveChangesAsync();
        return notification;
    }

    [Fact]
    public async Task A_notification_and_its_deliveries_round_trip()
    {
        var tenant = TenantId.New();
        var customer = RecipientId.ForCustomer(CustomerId.New());
        var notification = EmailNotification(tenant, recipient: customer);
        notification.StartProcessing(Clock);
        notification.MarkAsFailed(NotificationFailure.Temporary(NotificationFailure.Timeout, "timeout from 10.0.0.1"), Clock);
        notification.ScheduleRetry(Clock);
        Clock.Advance(TimeSpan.FromMinutes(5));
        notification.StartProcessing(Clock);
        notification.MarkAsSent(Clock, ProviderMessageId.Create("fake-msg-1"));
        await SavedAsync(notification);

        await using var scope = database.NewScope(tenant);
        var loaded = await scope.ServiceProvider.GetRequiredService<INotificationRepository>().FindAsync(tenant, notification.Id);

        Assert.NotNull(loaded);
        Assert.Equal(NotificationStatus.Sent, loaded.Status);
        Assert.Equal(customer, loaded.RecipientId);
        Assert.Equal("awa@example.com", loaded.Recipient.Address);
        Assert.Equal(NotificationChannel.Email, loaded.Channel);
        Assert.Equal("Commande prête", loaded.Content.Subject);
        Assert.Equal(NotificationRetryPolicy.Default, loaded.RetryPolicy);
        Assert.Equal(
            [(1, NotificationDeliveryStatus.Failed, "TIMEOUT"), (2, NotificationDeliveryStatus.Sent, (string?)null)],
            loaded.Deliveries.Select(delivery => (delivery.AttemptNumber, delivery.Status, delivery.FailureCode)));
        Assert.Equal("timeout from 10.0.0.1", loaded.Deliveries.First().Failure!.Reason);
        Assert.Equal(ProviderMessageId.Create("fake-msg-1"), loaded.Deliveries.Last().ProviderMessageId);
    }

    [Fact]
    public async Task A_tenant_never_sees_the_notifications_of_another_tenant_nor_of_the_platform()
    {
        var mine = await SavedAsync(EmailNotification(TenantId.New()));
        var other = await SavedAsync(EmailNotification(TenantId.New()));
        var platform = await SavedAsync(EmailNotification(null));

        await using var scope = database.NewScope(mine.TenantId);
        var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
        var visible = await context.Notifications.Select(notification => notification.Id).ToListAsync();

        Assert.Contains(mine.Id, visible);
        Assert.DoesNotContain(other.Id, visible);
        Assert.DoesNotContain(platform.Id, visible);
        Assert.Null(await repository.FindAsync(other.TenantId, other.Id));
    }

    [Fact]
    public async Task Without_a_tenant_only_platform_notifications_are_visible()
    {
        var restaurant = await SavedAsync(EmailNotification(TenantId.New()));
        var platform = await SavedAsync(EmailNotification(null));

        await using var scope = database.NewScope(tenantId: null);
        var visible = await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Notifications.Select(notification => notification.Id).ToListAsync();

        Assert.Contains(platform.Id, visible);
        Assert.DoesNotContain(restaurant.Id, visible);
    }

    [Fact]
    public async Task A_tenant_reads_its_templates_and_those_of_the_platform_but_not_those_of_another_tenant()
    {
        var tenant = TenantId.New();
        var other = TenantId.New();
        NotificationTemplate Template(TenantId? owner) => owner is { } id
            ? NotificationTemplate.Create(id, NotificationTypes.OrderReady.Key, NotificationChannel.Sms, French, null, "Prête.", true, Clock)
            : NotificationTemplate.CreatePlatformTemplate(NotificationTypes.StaffLowStock.Key, NotificationChannel.Sms, French, null, "Stock faible.", true, Clock);
        var own = Template(tenant);
        var foreign = Template(other);
        var platform = Template(null);
        foreach (var template in new[] { own, foreign, platform })
        {
            await using var write = database.NewScope(template.TenantId);
            scope(write).Templates.Add(template);
            await write.ServiceProvider.GetRequiredService<INotificationsUnitOfWork>().SaveChangesAsync();
        }

        await using var read = database.NewScope(tenant);
        var visible = await scope(read).Templates.Select(template => template.Id).ToListAsync();
        var repository = read.ServiceProvider.GetRequiredService<INotificationTemplateRepository>();

        Assert.Contains(own.Id, visible);
        Assert.Contains(platform.Id, visible);
        Assert.DoesNotContain(foreign.Id, visible);
        // Lu pour la résolution, jamais donné à modifier à un restaurant.
        Assert.Null(await repository.FindAsync(tenant, platform.Id));
        Assert.Null(await repository.FindAsync(tenant, foreign.Id));

        static NotificationsDbContext scope(AsyncServiceScope serviceScope) => serviceScope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
    }

    [Fact]
    public async Task A_configuration_with_assignments_and_a_preference_with_choices_round_trip()
    {
        var tenant = TenantId.New();
        var provider = NotificationProvider.Create(
            NotificationProviderCode.Create("FAKE" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()), "Fake", NotificationChannel.Sms,
            NotificationProviderConfigurationType.PlatformOrTenant, NotificationProviderCapability.Create(NotificationChannel.Sms, [NotificationFeature.Text]),
            true, Clock);
        provider.Enable(Clock);
        var configuration = RestaurantNotificationConfiguration.Create(tenant, provider, NotificationProviderMode.PlatformManaged, true, Clock);
        configuration.AssignToLocation(new LocationReference(tenant, LocationId.New(), CountryCode.Create("BJ")), 2, Clock);
        var owner = RecipientId.ForCustomer(CustomerId.New());
        var preference = NotificationPreference.Create(tenant, owner, true, Clock);
        preference.OptOut(NotificationPurpose.Marketing, NotificationChannel.Sms, PreferenceSource.Create("unsubscribe-link"), Clock);

        await using (var write = database.NewScope(tenant))
        {
            write.ServiceProvider.GetRequiredService<INotificationProviderRepository>().Add(provider);
            write.ServiceProvider.GetRequiredService<IRestaurantNotificationConfigurationRepository>().Add(configuration);
            write.ServiceProvider.GetRequiredService<INotificationPreferenceRepository>().Add(preference);
            await write.ServiceProvider.GetRequiredService<INotificationsUnitOfWork>().SaveChangesAsync();
        }

        // Le même choix changé dans une autre requête : une ligne remplacée, jamais un doublon.
        await using (var change = database.NewScope(tenant))
        {
            var loaded = await change.ServiceProvider.GetRequiredService<INotificationPreferenceRepository>().FindAsync(tenant, owner);
            loaded!.OptIn(NotificationPurpose.Marketing, NotificationChannel.Sms, PreferenceSource.Create("back-office"), Clock);
            await change.ServiceProvider.GetRequiredService<INotificationsUnitOfWork>().SaveChangesAsync();
        }

        await using var read = database.NewScope(tenant);
        var reloaded = await read.ServiceProvider.GetRequiredService<IRestaurantNotificationConfigurationRepository>().FindAsync(tenant, configuration.Id);
        var choices = await read.ServiceProvider.GetRequiredService<INotificationPreferenceRepository>().FindAsync(tenant, owner);

        Assert.Equal(2, Assert.Single(reloaded!.Assignments).Priority);
        Assert.True(choices!.Allows(NotificationPurpose.Marketing, NotificationChannel.Sms));
        Assert.Equal("back-office", Assert.Single(choices.Choices).Source.Value);

        // Les alertes d'équipe coupées par un utilisateur, relues telles quelles.
        var member = RecipientId.ForUser(UserId.New());
        await using (var mute = database.NewScope(tenant))
        {
            var staff = NotificationPreference.Create(tenant, member, isFirstForOwner: true, Clock);
            staff.SetTypeMuted(NotificationTypes.StaffLowStock, true, PreferenceSource.Create("console"), Clock);
            mute.ServiceProvider.GetRequiredService<INotificationPreferenceRepository>().Add(staff);
            await mute.ServiceProvider.GetRequiredService<INotificationsUnitOfWork>().SaveChangesAsync();
        }

        await using (var unmute = database.NewScope(tenant))
        {
            var staff = await unmute.ServiceProvider.GetRequiredService<INotificationPreferenceRepository>().FindAsync(tenant, member);
            staff!.SetTypeMuted(NotificationTypes.StaffNewReview, true, PreferenceSource.Create("console"), Clock);
            await unmute.ServiceProvider.GetRequiredService<INotificationsUnitOfWork>().SaveChangesAsync();
        }

        await using var again = database.NewScope(tenant);
        var muted = await again.ServiceProvider.GetRequiredService<INotificationPreferenceRepository>().FindAsync(tenant, member);
        Assert.Equal(["staff.low_stock", "staff.new_review"], muted!.MutedTypes);
    }

    [Fact]
    public async Task The_same_idempotency_key_cannot_create_two_notifications()
    {
        var tenant = TenantId.New();
        await SavedAsync(EmailNotification(tenant), "orders:7:0");

        var exception = await Assert.ThrowsAsync<DomainException>(() => SavedAsync(EmailNotification(tenant), "orders:7:0"));
        await SavedAsync(EmailNotification(TenantId.New()), "orders:7:0");

        Assert.Equal(NotificationsApplicationErrors.DuplicateRequest.Code, exception.Code);
    }

    [Fact]
    public async Task An_event_is_marked_processed_only_once()
    {
        async Task MarkAsync()
        {
            await using var scope = database.NewScope(TenantId.New());
            scope.ServiceProvider.GetRequiredService<INotificationInbox>().MarkProcessed("tests", "orders:99", "OrderReady", Clock.UtcNow);
            await scope.ServiceProvider.GetRequiredService<INotificationsUnitOfWork>().SaveChangesAsync();
        }

        await MarkAsync();
        var exception = await Assert.ThrowsAsync<DomainException>(MarkAsync);

        await using var read = database.NewScope(tenantId: null);
        Assert.True(await read.ServiceProvider.GetRequiredService<INotificationInbox>().HasProcessedAsync("tests", "orders:99"));
        Assert.Equal(NotificationsApplicationErrors.DuplicateEvent.Code, exception.Code);
    }

    [Fact]
    public async Task Usage_is_counted_per_tenant_month_and_channel()
    {
        var tenant = TenantId.New();
        await using var scope = database.NewScope(tenant);
        var usage = scope.ServiceProvider.GetRequiredService<INotificationUsage>();
        var provider = NotificationProviderCode.Create("FAKE");

        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var parallel = database.NewScope(tenant);
            await parallel.ServiceProvider.GetRequiredService<INotificationUsage>().RecordAsync(tenant, NotificationChannel.Sms, provider, Clock.UtcNow);
        }));

        Assert.Equal(5, await usage.CountAsync(tenant, NotificationChannel.Sms, Clock.UtcNow));
        Assert.Equal(0, await usage.CountAsync(tenant, NotificationChannel.Email, Clock.UtcNow));
        Assert.Equal(0, await usage.CountAsync(tenant, NotificationChannel.Sms, Clock.UtcNow.AddMonths(1)));
    }

    [Fact]
    public async Task Credentials_are_stored_encrypted_and_bound_to_their_reference()
    {
        var tenant = TenantId.New();
        var reference = NotificationCredentialReference.ForTenant(tenant, NotificationProviderCode.Create("FAKE"));
        await using var scope = database.NewScope(tenant);
        var store = scope.ServiceProvider.GetRequiredService<INotificationSecretStore>();
        var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        await store.StoreAsync(reference, new Dictionary<string, string> { ["api_key"] = "fake_SECRET_42", ["webhook_secret"] = "whsec_9" });
        var raw = await context.Secrets.AsNoTracking().SingleAsync(secret => secret.Reference == reference.Value);
        var read = await store.ReadAsync(reference);

        Assert.DoesNotContain("fake_SECRET_42", raw.CipherText, StringComparison.Ordinal);
        Assert.StartsWith("v1:", raw.CipherText, StringComparison.Ordinal);
        Assert.Equal(tenant.Value, raw.TenantId);
        Assert.Equal("fake_SECRET_42", read!["api_key"]);

        // Recopié sur la ligne d'un autre restaurant, le chiffré ne se déchiffre pas.
        var stolen = NotificationCredentialReference.ForTenant(TenantId.New(), NotificationProviderCode.Create("FAKE"));
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO notifications.notification_secrets (reference, tenant_id, cipher_text, updated_at_utc) VALUES ({stolen.Value}, {stolen.TenantId!.Value.Value}, {raw.CipherText}, {Clock.UtcNow})");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync(stolen));
    }

    [Fact]
    public async Task A_signed_webhook_is_kept_once_and_a_forged_one_is_rejected()
    {
        await using (var platform = database.NewScope(tenantId: null))
        {
            await platform.ServiceProvider.GetRequiredService<INotificationSecretStore>().StoreAsync(
                NotificationCredentialReference.ForPlatform(NotificationProviderCode.Create("FAKE")),
                new Dictionary<string, string> { ["api_key"] = "k", ["webhook_secret"] = "whsec_platform" });
        }

        var eventId = "evt-" + Guid.NewGuid().ToString("N");
        var body = Encoding.UTF8.GetBytes($$"""{"events":[{"id":"{{eventId}}","messageId":"fake-abc","kind":"delivered"}]}""");
        var signed = new Dictionary<string, string> { [FakeWebhookReader.SignatureHeader] = FakeWebhookReader.Sign("whsec_platform", body) };
        var forged = new Dictionary<string, string> { [FakeWebhookReader.SignatureHeader] = FakeWebhookReader.Sign("guess", body) };

        await using var scope = database.NewScope(tenantId: null);
        var receiver = scope.ServiceProvider.GetRequiredService<NotificationWebhookReceiver>();

        Assert.Equal(WebhookReception.Accepted, await receiver.ReceiveAsync("fake", null, signed, body));
        Assert.Equal(WebhookReception.Accepted, await receiver.ReceiveAsync("fake", null, signed, body));
        Assert.Equal(WebhookReception.Rejected, await receiver.ReceiveAsync("fake", null, forged, body));
        Assert.Equal(WebhookReception.UnknownProvider, await receiver.ReceiveAsync("postmark", null, signed, body));
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Webhooks.CountAsync(webhook => webhook.EventId == eventId));
    }

    [Fact]
    public async Task The_built_in_in_app_provider_and_otp_templates_are_seeded_once()
    {
        await database.Services.SeedBuiltInNotificationsAsync();

        await using var scope = database.NewScope(tenantId: null);
        var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var inApp = await context.Providers.SingleAsync(provider => provider.Code == NotificationProviderCode.InApp);
        var otpTemplates = await context.Templates.CountAsync(template => template.TenantId == null && template.Key == NotificationTypes.SecurityOtp.Key);

        Assert.True(inApp.IsEnabled);
        Assert.Equal(6, otpTemplates);
    }

    [Fact]
    public async Task The_platform_reads_the_send_log_and_counts_of_every_tenant_without_a_tenant_filter()
    {
        var since = Clock.UtcNow;
        var sent = EmailNotification(TenantId.New());
        sent.StartProcessing(Clock);
        Clock.Advance(TimeSpan.FromSeconds(3));
        sent.MarkAsSent(Clock, ProviderMessageId.Create("fake-log-1"));
        var failed = EmailNotification(TenantId.New());
        failed.StartProcessing(Clock);
        failed.MarkAsFailed(NotificationFailure.Permanent(NotificationFailure.InvalidRecipient, "refusé"), Clock);
        await SavedAsync(sent);
        await SavedAsync(failed);

        await using var scope = database.NewScope(null);
        var directory = scope.ServiceProvider.GetRequiredService<INotificationDirectory>();
        var counts = await directory.CountSinceAsync(since);
        var recent = await directory.ListRecentAsync(NotificationChannel.Email, null, 0, 500);
        var onlyFailed = await directory.ListRecentAsync(null, NotificationStatus.Failed, 0, 500);
        var delays = await directory.SendDelaysSinceAsync(since, 10);

        Assert.True(counts.Single(row => row.Channel == NotificationChannel.Email && row.Status == NotificationStatus.Sent).Count >= 1);
        Assert.True(counts.Single(row => row.Channel == NotificationChannel.Email && row.Status == NotificationStatus.Failed).Count >= 1);
        Assert.Contains(recent, notification => notification.Id == sent.Id);
        Assert.Contains(onlyFailed, notification => notification.Id == failed.Id);
        Assert.DoesNotContain(onlyFailed, notification => notification.Id == sent.Id);
        Assert.Contains(TimeSpan.FromSeconds(3), delays);
    }
}
