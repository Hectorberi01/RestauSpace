using Notifications.Application.Commands;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Application;

public sealed class CampaignUseCaseTests
{
    private readonly NotificationsFixture _fixture = new();

    private TenantId A => _fixture.TenantA;

    private CustomerId Consenting(string email, string? tag = null, TenantId? tenant = null)
    {
        var owner = tenant ?? A;
        var customer = _fixture.Customer(owner, email);
        _fixture.Consents[(owner, customer, CommunicationChannel.Email)] = true;
        if (tag is not null)
        {
            _fixture.CustomerTags[customer] = tag;
        }

        return customer;
    }

    private void EmailMarketingReady()
    {
        _fixture.Configured(A, NotificationsFixture.TestMail, NotificationChannel.Email);
        _fixture.Template(
            null, NotificationTypes.PromotionalCampaign, NotificationChannel.Email,
            "{{customer.firstName}}, {{campaign.title}} chez {{restaurant.name}}. Se désinscrire : {{unsubscribe.url}}", "{{campaign.title}}");
    }

    private Task<Guid> DraftAsync(string? tag = null, Dictionary<string, string>? variables = null) =>
        _fixture.SaveCampaign.HandleAsync(new SaveCampaignCommand(
            null, "Happy hour", "marketing.promotional_campaign", NotificationChannel.Email, tag, null,
            variables ?? new Dictionary<string, string> { ["campaign.title"] = "-20 % ce soir", ["restaurant.name"] = "Chez Awa" }));

    private async Task<int> RunToEndAsync(Guid campaign)
    {
        var batches = 0;
        do
        {
            batches++;
        }
        while (await _fixture.ProcessCampaign.HandleAsync(new ProcessCampaignCommand(campaign)) && batches < 50);

        return batches;
    }

    [Fact]
    public async Task A_campaign_can_target_only_the_customers_of_its_location()
    {
        EmailMarketingReady();
        var here = _fixture.Location(A).LocationId;
        var regular = Consenting("habitue@example.com");
        Consenting("ailleurs@example.com");
        _fixture.LocationCustomers.Add((here, regular));
        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.SaveCampaign.HandleAsync(new SaveCampaignCommand(
            null, "Local", "marketing.promotional_campaign", NotificationChannel.Email, null, null,
            new Dictionary<string, string> { ["campaign.title"] = "Soirée" }, OnlyLocationCustomers: true)));
        Assert.Equal(CampaignErrors.LocationRequired.Code, refused.Code);

        var campaign = await _fixture.SaveCampaign.HandleAsync(new SaveCampaignCommand(
            null, "Local", "marketing.promotional_campaign", NotificationChannel.Email, null, here.Value,
            new Dictionary<string, string> { ["campaign.title"] = "Soirée", ["restaurant.name"] = "Chez Awa" }, OnlyLocationCustomers: true));
        var audience = await _fixture.PreviewAudience.HandleAsync(new PreviewCampaignAudienceQuery(NotificationChannel.Email, null, here.Value));
        await _fixture.ScheduleCampaign.HandleAsync(new ScheduleCampaignCommand(campaign, null));
        await RunToEndAsync(campaign);

        Assert.Equal(1, audience.Count);
        Assert.Equal(RecipientId.ForCustomer(regular), Assert.Single(_fixture.NotificationRows).RecipientId);
        Assert.True(_fixture.CampaignRows.Single().OnlyLocationCustomers);
    }

    [Fact]
    public async Task A_campaign_reaches_once_each_consenting_customer_with_the_tag()
    {
        EmailMarketingReady();
        _fixture.Settings.CampaignBatchSize = 2;
        var vips = new[] { Consenting("a@example.com", "vip"), Consenting("b@example.com", "vip"), Consenting("c@example.com", "vip") };
        Consenting("d@example.com", "habitue");
        _fixture.Customer(A, "sans-consentement@example.com");
        Consenting("autre@example.com", "vip", _fixture.TenantB);
        var campaign = await DraftAsync(tag: " VIP ");

        var audience = await _fixture.PreviewAudience.HandleAsync(new PreviewCampaignAudienceQuery(NotificationChannel.Email, "vip"));
        await _fixture.ScheduleCampaign.HandleAsync(new ScheduleCampaignCommand(campaign, null));
        var batches = await RunToEndAsync(campaign);
        var again = await _fixture.ProcessCampaign.HandleAsync(new ProcessCampaignCommand(campaign));

        var done = _fixture.CampaignRows.Single();
        Assert.Equal(3, audience.Count);
        Assert.Equal(2, batches);
        Assert.False(again);
        Assert.Equal((CampaignStatus.Completed, 3, 3, 0), (done.Status, done.Processed, done.Created, done.Skipped));
        Assert.Equal(
            vips.Select(id => RecipientId.ForCustomer(id)).OrderBy(id => id.Id),
            _fixture.NotificationRows.Select(notification => notification.RecipientId!).OrderBy(id => id.Id));
        Assert.All(_fixture.NotificationRows, notification =>
        {
            Assert.Equal(NotificationPurpose.Marketing, notification.Purpose);
            Assert.Contains("-20 % ce soir chez Chez Awa", notification.Content.Body, StringComparison.Ordinal);
            Assert.Contains("desinscription?jeton=", notification.Content.Body, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task A_customer_messaged_recently_is_skipped_by_the_frequency_rule()
    {
        EmailMarketingReady();
        var recent = Consenting("a@example.com");
        Consenting("b@example.com");
        await _fixture.Send.HandleAsync(new SendNotificationCommand(
            "marketing.promotional_campaign", NotificationChannel.Email, recent.Value,
            Variables: new Dictionary<string, string> { ["campaign.title"] = "Hier", ["restaurant.name"] = "Chez Awa" }));
        _fixture.Clock.Advance(TimeSpan.FromDays(1));
        var campaign = await DraftAsync();
        await _fixture.ScheduleCampaign.HandleAsync(new ScheduleCampaignCommand(campaign, null));

        await RunToEndAsync(campaign);

        var done = _fixture.CampaignRows.Single();
        Assert.Equal((2, 1, 1, 1), (done.Processed, done.Created, done.Skipped, done.SkippedForFrequency));
    }

    [Fact]
    public async Task Without_a_frequency_rule_a_second_message_is_sent()
    {
        EmailMarketingReady();
        var customer = Consenting("a@example.com");
        await _fixture.SetMarketingRules.HandleAsync(new SetMarketingRulesCommand(0));
        var command = new SendNotificationCommand(
            "marketing.promotional_campaign", NotificationChannel.Email, customer.Value,
            Variables: new Dictionary<string, string> { ["campaign.title"] = "Ce soir", ["restaurant.name"] = "Chez Awa" });

        await _fixture.Send.HandleAsync(command);
        var second = await _fixture.Send.HandleAsync(command);

        Assert.Single(second.NotificationIds);
    }

    [Fact]
    public async Task The_frequency_rule_skips_a_second_marketing_message()
    {
        EmailMarketingReady();
        var customer = Consenting("a@example.com");
        var command = new SendNotificationCommand(
            "marketing.promotional_campaign", NotificationChannel.Email, customer.Value,
            Variables: new Dictionary<string, string> { ["campaign.title"] = "Ce soir", ["restaurant.name"] = "Chez Awa" });

        await _fixture.Send.HandleAsync(command);
        var second = await _fixture.Send.HandleAsync(command);
        _fixture.Clock.Advance(TimeSpan.FromDays(MarketingRules.DefaultIntervalDays));
        var later = await _fixture.Send.HandleAsync(command);

        Assert.Equal(NotificationSkipReason.FrequencyLimited, second.Skipped);
        Assert.Single(later.NotificationIds);
    }

    [Fact]
    public async Task A_campaign_cannot_set_the_first_name_or_the_unsubscribe_link()
    {
        var refused = await Assert.ThrowsAsync<DomainException>(() =>
            DraftAsync(variables: new Dictionary<string, string> { ["unsubscribe.url"] = "https://evil.example.com" }));

        Assert.Equal(CampaignErrors.VariableNotAllowed, refused.Error);
    }

    [Fact]
    public async Task A_campaign_sends_a_marketing_type_only()
    {
        var refused = await Assert.ThrowsAsync<DomainException>(() => _fixture.SaveCampaign.HandleAsync(new SaveCampaignCommand(
            null, "Faux", "order.ready", NotificationChannel.Email, null, null, null)));

        Assert.Equal(CampaignErrors.TypeNotMarketing, refused.Error);
    }

    [Fact]
    public async Task A_scheduled_campaign_waits_for_its_time_and_a_cancelled_one_stops()
    {
        EmailMarketingReady();
        Consenting("a@example.com");
        var campaign = await DraftAsync();
        await _fixture.ScheduleCampaign.HandleAsync(new ScheduleCampaignCommand(campaign, _fixture.Clock.UtcNow.AddHours(2)));

        var early = await _fixture.ProcessCampaign.HandleAsync(new ProcessCampaignCommand(campaign));
        await _fixture.CancelCampaign.HandleAsync(new CancelCampaignCommand(campaign));
        _fixture.Clock.Advance(TimeSpan.FromHours(3));
        var afterCancel = await _fixture.ProcessCampaign.HandleAsync(new ProcessCampaignCommand(campaign));

        Assert.False(early);
        Assert.False(afterCancel);
        Assert.Empty(_fixture.NotificationRows);
        Assert.Equal(CampaignStatus.Cancelled, _fixture.CampaignRows.Single().Status);
    }

    [Fact]
    public async Task A_campaign_of_another_restaurant_is_not_found()
    {
        var campaign = await DraftAsync();
        _fixture.CurrentTenantId = _fixture.TenantB;

        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.ScheduleCampaign.HandleAsync(new ScheduleCampaignCommand(campaign, null)));
    }

    [Fact]
    public async Task Without_campaigns_in_the_plan_a_campaign_is_neither_saved_scheduled_nor_sent()
    {
        EmailMarketingReady();
        Consenting("awa@example.com");
        var campaign = await DraftAsync();
        await _fixture.ScheduleCampaign.HandleAsync(new ScheduleCampaignCommand(campaign, null));
        _fixture.DeniedFeatures.Add(PlanFeatures.MarketingCampaigns);

        var save = await Assert.ThrowsAsync<ForbiddenException>(() => DraftAsync());
        var schedule = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.ScheduleCampaign.HandleAsync(new ScheduleCampaignCommand(campaign, null)));
        var more = await _fixture.ProcessCampaign.HandleAsync(new ProcessCampaignCommand(campaign));

        Assert.Equal((PlanFeatures.RequiredCode, PlanFeatures.RequiredCode), (save.Code, schedule.Code));
        Assert.False(more);
        Assert.Empty(_fixture.NotificationRows);
        Assert.Equal(CampaignStatus.Scheduled, _fixture.CampaignRows.Single().Status); // gelée, pas annulée

        _fixture.DeniedFeatures.Clear();
        await RunToEndAsync(campaign);
        Assert.Single(_fixture.NotificationRows);
    }

    [Fact]
    public async Task Scheduling_a_campaign_for_later_needs_automations_in_the_plan()
    {
        EmailMarketingReady();
        var campaign = await DraftAsync();
        _fixture.DeniedFeatures.Add(PlanFeatures.Automations);

        var later = await Assert.ThrowsAsync<ForbiddenException>(() =>
            _fixture.ScheduleCampaign.HandleAsync(new ScheduleCampaignCommand(campaign, _fixture.Clock.UtcNow.AddDays(1))));
        await _fixture.ScheduleCampaign.HandleAsync(new ScheduleCampaignCommand(campaign, null));

        Assert.Equal(PlanFeatures.RequiredCode, later.Code);
        Assert.Equal(CampaignStatus.Scheduled, _fixture.CampaignRows.Single().Status);
    }
}
