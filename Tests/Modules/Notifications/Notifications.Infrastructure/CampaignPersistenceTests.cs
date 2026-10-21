using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Abstractions;
using Notifications.Domain;

namespace Notifications.Infrastructure;

/// <summary>Campagnes, règle de fréquence et historique marketing sur un vrai PostgreSQL.</summary>
public sealed class CampaignPersistenceTests(NotificationsDatabase database) : IClassFixture<NotificationsDatabase>
{
    private FakeClock Clock => database.Clock;

    private async Task SaveAsync(TenantId tenant, Action<IServiceProvider> change)
    {
        await using var scope = database.NewScope(tenant);
        change(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<INotificationsUnitOfWork>().SaveChangesAsync();
    }

    private async Task<T> ReadAsync<T>(TenantId? tenant, Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = database.NewScope(tenant);
        return await read(scope.ServiceProvider);
    }

    [Fact]
    public async Task A_campaign_round_trips_with_its_variables_and_counters()
    {
        var tenant = TenantId.New();
        var campaign = Campaign.Create(
            tenant, "Happy hour", "marketing.promotional_campaign", NotificationChannel.WhatsApp, "vip", null,
            new Dictionary<string, string> { ["campaign.title"] = "-20 % ce soir", ["campaign.url"] = "https://chez-awa.example.com" }, Clock);
        await SaveAsync(tenant, services => services.GetRequiredService<ICampaignRepository>().Add(campaign));

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = (await scope.ServiceProvider.GetRequiredService<ICampaignRepository>().FindAsync(tenant, campaign.Id))!;
            loaded.Schedule(null, Clock);
            loaded.RecordBatch(10, 8, 2, 1, exhausted: false, Clock);
            await scope.ServiceProvider.GetRequiredService<INotificationsUnitOfWork>().SaveChangesAsync();
        }

        var back = await ReadAsync(tenant, services => services.GetRequiredService<ICampaignRepository>().FindAsync(tenant, campaign.Id));
        var due = await ReadAsync(null, services => services.GetRequiredService<ICampaignDirectory>().ListDueAsync(Clock.UtcNow, 100));
        var other = await ReadAsync(TenantId.New(), services => services.GetRequiredService<ICampaignRepository>().ListAsync(tenant, 10));

        Assert.Equal((CampaignStatus.Sending, 10, 8, 2, 1), (back!.Status, back.Processed, back.Created, back.Skipped, back.SkippedForFrequency));
        Assert.Equal("-20 % ce soir", back.Variables["campaign.title"]);
        Assert.Contains(due, locator => locator.Id == campaign.Id && locator.TenantId == tenant);
        Assert.Empty(other);
    }

    [Fact]
    public async Task The_marketing_history_counts_only_recent_marketing_messages_of_the_person()
    {
        var tenant = TenantId.New();
        var customer = RecipientId.ForCustomer(CustomerId.New());
        var content = NotificationContent.Create(NotificationChannel.Email, "Menu", "Nouveau menu");
        var marketing = Notification.Create(
            tenant, NotificationTypes.NewMenu, NotificationRecipient.ForEmail(Email.Create("awa@example.com")), customer,
            NotificationProviderCode.Create("FAKE"), NotificationLanguage.Create("fr"), content, Clock, consent: ConsentProof.Granted(customer.ToCustomerId()!.Value, CommunicationChannel.Email));
        await SaveAsync(tenant, services =>
        {
            services.GetRequiredService<INotificationRepository>().Add(marketing);
            services.GetRequiredService<IMarketingRulesRepository>().Add(MarketingRules.Create(tenant, 7, Clock));
        });
        var since = Clock.UtcNow.AddDays(-1);

        var received = await ReadAsync(tenant, services => services.GetRequiredService<IMarketingHistory>().ReceivedSinceAsync(tenant, customer, since));
        var later = await ReadAsync(tenant, services => services.GetRequiredService<IMarketingHistory>().ReceivedSinceAsync(tenant, customer, Clock.UtcNow));
        var otherPerson = await ReadAsync(tenant, services =>
            services.GetRequiredService<IMarketingHistory>().ReceivedSinceAsync(tenant, RecipientId.ForCustomer(CustomerId.New()), since));
        var rules = await ReadAsync(tenant, services => services.GetRequiredService<IMarketingRulesRepository>().FindAsync(tenant));

        Assert.True(received);
        Assert.False(later);
        Assert.False(otherPerson);
        Assert.Equal(7, rules!.MinimumIntervalDays);
    }
}
