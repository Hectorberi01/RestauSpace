namespace Notifications.Domain;

public class CampaignTests
{
    private static readonly TenantId Tenant = TenantId.New();

    private readonly FakeClock _clock = new();

    private Campaign Draft(string? tag = null) => Campaign.Create(
        Tenant, " Soirée jazz ", "marketing.special_event", NotificationChannel.WhatsApp, tag, null,
        new Dictionary<string, string> { ["campaign.title"] = " Jazz vendredi ", ["event.date"] = "", ["campaign.url"] = "https://chez-awa.example.com" },
        _clock);

    [Fact]
    public void A_draft_keeps_trimmed_values_and_drops_empty_variables()
    {
        var campaign = Draft(" VIP ");

        Assert.Equal(("Soirée jazz", "vip", CampaignStatus.Draft), (campaign.Name, campaign.Tag, campaign.Status));
        Assert.Equal("Jazz vendredi", campaign.Variables["campaign.title"]);
        Assert.False(campaign.Variables.ContainsKey("event.date"));
    }

    [Fact]
    public void A_variable_outside_the_type_is_refused()
    {
        DomainAssert.Throws(CampaignErrors.VariableNotAllowed, () => Campaign.Create(
            Tenant, "Promo", "marketing.promotional_campaign", NotificationChannel.Email, null, null,
            new Dictionary<string, string> { ["order.number"] = "A-1" }, _clock));
    }

    [Fact]
    public void Push_and_in_app_are_not_campaign_channels()
    {
        DomainAssert.Throws(CampaignErrors.ChannelInvalid, () => Campaign.Create(
            Tenant, "Promo", "marketing.promotional_campaign", NotificationChannel.Push, null, null, null, _clock));
    }

    [Fact]
    public void Scheduling_in_the_past_starts_now_and_freezes_the_draft()
    {
        var campaign = Draft();

        campaign.Schedule(_clock.UtcNow.AddHours(-1), _clock);

        Assert.Equal((CampaignStatus.Scheduled, _clock.UtcNow), (campaign.Status, campaign.ScheduledForUtc));
        Assert.True(campaign.IsDue(_clock.UtcNow));
        DomainAssert.Throws(CampaignErrors.NotDraft, () => campaign.Change("Autre", "marketing.new_menu", NotificationChannel.Sms, null, null, null));
    }

    [Fact]
    public void A_schedule_more_than_ninety_days_ahead_is_refused()
    {
        DomainAssert.Throws(CampaignErrors.ScheduleTooFar, () => Draft().Schedule(_clock.UtcNow.AddDays(91), _clock));
    }

    [Fact]
    public void Unscheduling_returns_to_draft()
    {
        var campaign = Draft();
        campaign.Schedule(_clock.UtcNow.AddDays(1), _clock);

        campaign.Unschedule();

        Assert.Equal(CampaignStatus.Draft, campaign.Status);
        Assert.Null(campaign.ScheduledForUtc);
    }

    [Fact]
    public void Batches_add_up_until_the_audience_is_exhausted()
    {
        var campaign = Draft();
        campaign.Schedule(null, _clock);

        campaign.RecordBatch(100, 90, 10, 4, exhausted: false, _clock);
        Assert.Equal((CampaignStatus.Sending, _clock.UtcNow), (campaign.Status, campaign.StartedAtUtc));
        campaign.RecordBatch(20, 20, 0, 0, exhausted: true, _clock);

        Assert.Equal((CampaignStatus.Completed, 120, 110, 10, 4), (campaign.Status, campaign.Processed, campaign.Created, campaign.Skipped, campaign.SkippedForFrequency));
        DomainAssert.Throws(CampaignErrors.Finished, () => campaign.Cancel(_clock));
    }

    [Fact]
    public void An_inconsistent_batch_is_refused()
    {
        var campaign = Draft();
        campaign.Schedule(null, _clock);

        DomainAssert.Throws(CampaignErrors.BatchInvalid, () => campaign.RecordBatch(10, 8, 5, 0, exhausted: false, _clock));
    }

    [Fact]
    public void The_frequency_interval_stays_between_zero_and_thirty_days()
    {
        var rules = MarketingRules.Create(Tenant, 0, _clock);

        rules.Change(30, _clock);

        Assert.Equal(30, rules.MinimumIntervalDays);
        DomainAssert.Throws(CampaignErrors.IntervalInvalid, () => rules.Change(31, _clock));
    }
}
