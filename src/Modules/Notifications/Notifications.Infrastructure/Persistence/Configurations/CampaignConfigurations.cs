using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Notifications.Domain;

namespace Notifications.Infrastructure.Persistence.Configurations;

/// <summary>
/// Campagnes marketing (<c>campaigns</c>) : variables communes en jsonb, jamais de donnée d'un client. Index pour le
/// worker (statut, date de départ) et pour la liste du restaurant.
/// </summary>
internal sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("campaigns", table =>
        {
            table.HasCheckConstraint("ck_campaigns_status", Checks.In<CampaignStatus>("status"));
            table.HasCheckConstraint("ck_campaigns_channel", Checks.In<NotificationChannel>("channel"));
            table.HasCheckConstraint("ck_campaigns_counters", "processed >= 0 AND created >= 0 AND skipped >= 0 AND skipped_for_frequency >= 0");
        });
        builder.HasKey(campaign => campaign.Id);
        builder.Property(campaign => campaign.Id).HasColumnName("id").HasConversion(id => id.Value, value => CampaignId.From(value)).ValueGeneratedNever();
        builder.Property(campaign => campaign.TenantId).HasColumnName("tenant_id").HasConversion(NotificationsConverters.TenantKey);
        builder.Property(campaign => campaign.Name).HasColumnName("name").HasMaxLength(Campaign.NameMaxLength);
        builder.Property(campaign => campaign.TypeKey).HasColumnName("type").HasMaxLength(TemplateKey.MaxLength);
        builder.Property(campaign => campaign.Channel).HasColumnName("channel");
        builder.Property(campaign => campaign.Tag).HasColumnName("tag").HasMaxLength(Campaign.TagMaxLength);
        builder.Property(campaign => campaign.LocationId).HasColumnName("location_id").HasConversion(NotificationsConverters.LocationKey);
        builder.Property(campaign => campaign.OnlyLocationCustomers).HasColumnName("only_location_customers").HasDefaultValue(false);
        builder.Property<Dictionary<string, string>>("_variables")
            .HasColumnName("variables")
            .HasColumnType("jsonb")
            .HasConversion(CampaignVariables.Converter, CampaignVariables.Comparer);
        builder.Ignore(campaign => campaign.Variables);
        builder.Property(campaign => campaign.Status).HasColumnName("status");
        builder.Property(campaign => campaign.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(campaign => campaign.ScheduledForUtc).HasColumnName("scheduled_for_utc");
        builder.Property(campaign => campaign.StartedAtUtc).HasColumnName("started_at_utc");
        builder.Property(campaign => campaign.CompletedAtUtc).HasColumnName("completed_at_utc");
        builder.Property(campaign => campaign.Processed).HasColumnName("processed");
        builder.Property(campaign => campaign.Created).HasColumnName("created");
        builder.Property(campaign => campaign.Skipped).HasColumnName("skipped");
        builder.Property(campaign => campaign.SkippedForFrequency).HasColumnName("skipped_for_frequency");
        builder.HasIndex(campaign => new { campaign.TenantId, campaign.CreatedAtUtc }).HasDatabaseName("ix_campaigns_tenant_id_created_at_utc");
        builder.HasIndex(campaign => new { campaign.Status, campaign.ScheduledForUtc }).HasDatabaseName("ix_campaigns_status_scheduled_for_utc");
        builder.Property<uint>("Version").IsRowVersion();
        builder.Ignore(campaign => campaign.DomainEvents);
    }
}

internal sealed class MarketingRulesConfiguration : IEntityTypeConfiguration<MarketingRules>
{
    public void Configure(EntityTypeBuilder<MarketingRules> builder)
    {
        builder.ToTable("marketing_rules", table =>
            table.HasCheckConstraint("ck_marketing_rules_interval", $"minimum_interval_days BETWEEN 0 AND {MarketingRules.MaxIntervalDays}"));
        builder.HasKey(rules => rules.TenantId);
        builder.Property(rules => rules.TenantId).HasColumnName("tenant_id").HasConversion(NotificationsConverters.TenantKey).ValueGeneratedNever();
        builder.Property(rules => rules.MinimumIntervalDays).HasColumnName("minimum_interval_days");
        builder.Property(rules => rules.UpdatedAtUtc).HasColumnName("updated_at_utc");
    }
}

internal static class CampaignVariables
{
    public static readonly ValueConverter<Dictionary<string, string>, string> Converter = new(
        variables => System.Text.Json.JsonSerializer.Serialize(variables, (System.Text.Json.JsonSerializerOptions?)null),
        json => new Dictionary<string, string>(
            System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json, (System.Text.Json.JsonSerializerOptions?)null) ?? new Dictionary<string, string>(),
            StringComparer.Ordinal));

    public static readonly ValueComparer<Dictionary<string, string>> Comparer = new(
        (left, right) => left!.Count == right!.Count && !left.Except(right).Any(),
        variables => variables.Aggregate(0, (hash, pair) => hash ^ HashCode.Combine(pair.Key, pair.Value)),
        variables => new Dictionary<string, string>(variables, StringComparer.Ordinal));
}
