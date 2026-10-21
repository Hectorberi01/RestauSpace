using Notifications.Application.Abstractions;
using Notifications.Domain;
using Notifications.Infrastructure.Persistence.Configurations;

namespace Notifications.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte : deux verrous sur la même porte.

internal sealed class NotificationProviderRepository(NotificationsDbContext context) : INotificationProviderRepository
{
    public Task<NotificationProvider?> FindAsync(NotificationProviderId id, CancellationToken cancellationToken = default) =>
        context.Providers.SingleOrDefaultAsync(provider => provider.Id == id, cancellationToken);

    public Task<NotificationProvider?> FindByCodeAsync(NotificationProviderCode code, CancellationToken cancellationToken = default) =>
        context.Providers.SingleOrDefaultAsync(provider => provider.Code == code, cancellationToken);

    public async Task<IReadOnlyList<NotificationProvider>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.Providers.AsNoTracking()
            .OrderBy(provider => provider.Channel)
            .ThenBy(provider => provider.Name)
            .ThenBy(provider => provider.Id)
            .ToListAsync(cancellationToken);

    public void Add(NotificationProvider provider) => context.Providers.Add(provider);
}

internal sealed class RestaurantNotificationConfigurationRepository(NotificationsDbContext context) : IRestaurantNotificationConfigurationRepository
{
    // Affectations aux établissements toujours chargées dans leur ordre d'ajout.
    private IQueryable<RestaurantNotificationConfiguration> Configurations => context.Configurations
        .Include(configuration => configuration.Assignments.OrderBy(
            assignment => EF.Property<int>(assignment, NotificationChannelAssignmentConfiguration.PositionProperty)));

    public Task<RestaurantNotificationConfiguration?> FindAsync(
        TenantId tenantId, RestaurantNotificationConfigurationId id, CancellationToken cancellationToken = default) =>
        Configurations.SingleOrDefaultAsync(configuration => configuration.TenantId == tenantId && configuration.Id == id, cancellationToken);

    public Task<RestaurantNotificationConfiguration?> FindByProviderAsync(
        TenantId tenantId, NotificationProviderCode provider, CancellationToken cancellationToken = default) =>
        Configurations.SingleOrDefaultAsync(
            configuration => configuration.TenantId == tenantId && configuration.Provider == provider, cancellationToken);

    public async Task<IReadOnlyList<RestaurantNotificationConfiguration>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await Configurations.AsNoTracking()
            .Where(configuration => configuration.TenantId == tenantId)
            .OrderBy(configuration => configuration.Channel)
            .ThenBy(configuration => configuration.CreatedAtUtc)
            .ThenBy(configuration => configuration.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<RestaurantNotificationConfiguration>> ListForChannelAsync(
        TenantId tenantId, NotificationChannel channel, CancellationToken cancellationToken = default) =>
        await Configurations.AsNoTracking()
            .Where(configuration => configuration.TenantId == tenantId && configuration.Channel == channel)
            .OrderBy(configuration => configuration.CreatedAtUtc)
            .ThenBy(configuration => configuration.Id)
            .ToListAsync(cancellationToken);

    public void Add(RestaurantNotificationConfiguration configuration) => context.Configurations.Add(configuration);
}

internal sealed class NotificationTemplateRepository(NotificationsDbContext context) : INotificationTemplateRepository
{
    public Task<NotificationTemplate?> FindAsync(TenantId? owner, TemplateId id, CancellationToken cancellationToken = default) =>
        context.Templates.SingleOrDefaultAsync(template => template.Id == id && template.TenantId == owner, cancellationToken);

    public async Task<IReadOnlyList<NotificationTemplate>> ListCandidatesAsync(
        TenantId? tenantId, TemplateKey key, NotificationChannel channel, NotificationLanguage language, CancellationToken cancellationToken = default) =>
        await context.Templates.AsNoTracking()
            .Where(template => (template.TenantId == null || template.TenantId == tenantId)
                && template.Key == key
                && template.Channel == channel
                && template.Language == language
                && template.Status == TemplateStatus.Published)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<NotificationTemplate>> ListAsync(TenantId? owner, CancellationToken cancellationToken = default) =>
        await context.Templates.AsNoTracking()
            .Where(template => template.TenantId == owner)
            .OrderBy(template => template.Key)
            .ThenBy(template => template.Channel)
            .ThenBy(template => template.Language)
            .ThenByDescending(template => template.Version)
            .ToListAsync(cancellationToken);

    public async Task<bool> IsKeyUniqueAsync(
        TenantId? tenantId, TemplateKey key, NotificationChannel channel, NotificationLanguage language, CancellationToken cancellationToken = default) =>
        !await context.Templates.AnyAsync(
            template => template.TenantId == tenantId && template.Key == key && template.Channel == channel && template.Language == language,
            cancellationToken);

    public async Task<bool> IsVersionUniqueAsync(
        TenantId? tenantId, TemplateKey key, NotificationChannel channel, NotificationLanguage language, int version, CancellationToken cancellationToken = default) =>
        !await context.Templates.AnyAsync(
            template => template.TenantId == tenantId && template.Key == key && template.Channel == channel
                && template.Language == language && template.Version == version,
            cancellationToken);

    public void Add(NotificationTemplate template) => context.Templates.Add(template);
}

internal sealed class NotificationPreferenceRepository(NotificationsDbContext context) : INotificationPreferenceRepository
{
    public Task<NotificationPreference?> FindAsync(TenantId tenantId, RecipientId owner, CancellationToken cancellationToken = default) =>
        context.Preferences.SingleOrDefaultAsync(preference => preference.TenantId == tenantId && preference.Owner == owner, cancellationToken);

    public Task<NotificationPreference?> FindForSignedLinkAsync(TenantId tenantId, RecipientId owner, CancellationToken cancellationToken = default) =>
        context.Preferences.IgnoreQueryFilters()
            .SingleOrDefaultAsync(preference => preference.TenantId == tenantId && preference.Owner == owner, cancellationToken);

    public void Add(NotificationPreference preference) => context.Preferences.Add(preference);
}

internal sealed class NotificationRepository(NotificationsDbContext context) : INotificationRepository
{
    public Task<Notification?> FindAsync(TenantId? owner, NotificationId id, CancellationToken cancellationToken = default) =>
        NotificationQueries.WithDeliveries(context.Notifications)
            .SingleOrDefaultAsync(notification => notification.Id == id && notification.TenantId == owner, cancellationToken);

    public async Task<IReadOnlyList<Notification>> ListAsync(
        TenantId tenantId, NotificationFilter filter, int skip, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = NotificationQueries.WithDeliveries(context.Notifications).AsNoTracking().Where(notification => notification.TenantId == tenantId);
        if (filter.Status is { } status)
        {
            query = query.Where(notification => notification.Status == status);
        }

        if (filter.Channel is { } channel)
        {
            query = query.Where(notification => notification.Recipient.Channel == channel);
        }

        if (filter.Type is { } type)
        {
            query = query.Where(notification => notification.TemplateKey == type);
        }

        if (filter.Recipient is { } recipient)
        {
            query = query.Where(notification => notification.RecipientId == recipient);
        }

        if (filter.From is { } from)
        {
            query = query.Where(notification => notification.CreatedAtUtc >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(notification => notification.CreatedAtUtc < to);
        }

        return await query
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .ThenByDescending(notification => notification.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Notification>> ListInAppAsync(
        TenantId tenantId, UserId userId, bool includeArchived, int skip, int take, CancellationToken cancellationToken = default)
    {
        var recipient = RecipientId.ForUser(userId);
        return await context.Notifications.AsNoTracking()
            .Where(notification => notification.TenantId == tenantId
                && notification.RecipientId == recipient
                && notification.Recipient.Channel == NotificationChannel.InApp
                && notification.Status == NotificationStatus.Delivered
                && (includeArchived || notification.ArchivedAtUtc == null))
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .ThenByDescending(notification => notification.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsAsync(TenantId? owner, string idempotencyKey, CancellationToken cancellationToken = default) =>
        context.Notifications.AnyAsync(
            notification => notification.TenantId == owner
                && EF.Property<string?>(notification, NotificationConfiguration.IdempotencyKeyProperty) == idempotencyKey,
            cancellationToken);

    public Task<Notification?> FindByIdempotencyKeyAsync(TenantId? owner, string idempotencyKey, CancellationToken cancellationToken = default) =>
        NotificationQueries.WithDeliveries(context.Notifications)
            .SingleOrDefaultAsync(
                notification => notification.TenantId == owner
                    && EF.Property<string?>(notification, NotificationConfiguration.IdempotencyKeyProperty) == idempotencyKey,
                cancellationToken);

    public void Add(Notification notification, string? idempotencyKey = null)
    {
        DomainException.ThrowIf(
            idempotencyKey is { Length: 0 or > NotificationConfiguration.MaxIdempotencyKeyLength }, NotificationsApplicationErrorsBridge.KeyInvalid);
        var entry = context.Notifications.Add(notification);
        entry.Property<string?>(NotificationConfiguration.IdempotencyKeyProperty).CurrentValue = idempotencyKey;
    }
}

internal static class NotificationsApplicationErrorsBridge
{
    public static readonly DomainError KeyInvalid =
        new("notifications.send.idempotency_key_invalid", "An idempotency key is 1 to 200 characters.");
}

internal static class NotificationQueries
{
    /// <summary>Tentatives toujours chargées dans leur ordre.</summary>
    public static IQueryable<Notification> WithDeliveries(IQueryable<Notification> notifications) =>
        notifications.Include(notification => notification.Deliveries.OrderBy(
            delivery => EF.Property<int>(delivery, NotificationDeliveryConfiguration.PositionProperty)));
}

/// <summary>Lectures TOUS TENANTS des traitements de la plateforme (worker, webhooks, administration).</summary>
internal sealed class NotificationDirectory(NotificationsDbContext context) : INotificationDirectory
{
    private IQueryable<Notification> All => context.Notifications.IgnoreQueryFilters();

    public async Task<IReadOnlyList<NotificationLocator>> ListDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default) =>
        (await All.AsNoTracking()
            .Where(notification => (notification.Status == NotificationStatus.Pending || notification.Status == NotificationStatus.Scheduled)
                && notification.NextAttemptAtUtc <= now)
            // Une campagne marketing ne bloque jamais un OTP : les plus urgentes d'abord.
            .OrderBy(notification => notification.Priority)
            .ThenBy(notification => notification.NextAttemptAtUtc)
            .Take(take)
            .Select(notification => new { notification.Id, notification.TenantId })
            .ToListAsync(cancellationToken))
        .Select(row => new NotificationLocator(row.Id, row.TenantId))
        .ToList();

    public async Task<IReadOnlyList<NotificationLocator>> ListStuckAsync(DateTimeOffset startedBefore, int take, CancellationToken cancellationToken = default) =>
        (await All.AsNoTracking()
            .Where(notification => notification.Status == NotificationStatus.Processing
                && notification.Deliveries.Any(delivery => delivery.Status == NotificationDeliveryStatus.InProgress && delivery.StartedAtUtc < startedBefore))
            .OrderBy(notification => notification.CreatedAtUtc)
            .ThenBy(notification => notification.Id)
            .Take(take)
            .Select(notification => new { notification.Id, notification.TenantId })
            .ToListAsync(cancellationToken))
        .Select(row => new NotificationLocator(row.Id, row.TenantId))
        .ToList();

    public async Task<IReadOnlyList<NotificationLocator>> ListToRedactAsync(DateTimeOffset before, int take, CancellationToken cancellationToken = default) =>
        (await All.AsNoTracking()
            .Where(notification => notification.RedactedAtUtc == null
                && notification.CreatedAtUtc < before
                && (notification.Status == NotificationStatus.Sent
                    || notification.Status == NotificationStatus.PartiallyDelivered
                    || notification.Status == NotificationStatus.Delivered
                    || notification.Status == NotificationStatus.Cancelled))
            // L'in-app reste lisible dans la boîte de l'utilisateur tant qu'il ne l'a pas rangée.
            .Where(notification => notification.Recipient.Channel != NotificationChannel.InApp || notification.ArchivedAtUtc != null)
            .OrderBy(notification => notification.CreatedAtUtc)
            .ThenBy(notification => notification.Id)
            .Take(take)
            .Select(notification => new { notification.Id, notification.TenantId })
            .ToListAsync(cancellationToken))
        .Select(row => new NotificationLocator(row.Id, row.TenantId))
        .ToList();

    public async Task<NotificationLocator?> FindByProviderMessageAsync(
        NotificationProviderCode provider, ProviderMessageId messageId, CancellationToken cancellationToken = default)
    {
        var row = await All.AsNoTracking()
            .Where(notification => notification.Deliveries.Any(delivery => delivery.ProviderCode == provider && delivery.ProviderMessageId == messageId))
            .Select(notification => new { notification.Id, notification.TenantId })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new NotificationLocator(row.Id, row.TenantId);
    }

    public async Task<IReadOnlyList<Notification>> ListRecentAsync(
        NotificationChannel? channel, NotificationStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = NotificationQueries.WithDeliveries(All).AsNoTracking();
        if (channel is { } wanted)
        {
            query = query.Where(notification => notification.Recipient.Channel == wanted);
        }

        if (status is { } state)
        {
            query = query.Where(notification => notification.Status == state);
        }

        return await query
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .ThenByDescending(notification => notification.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<NotificationCount>> CountSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
        (await All.AsNoTracking()
            .Where(notification => notification.CreatedAtUtc >= since)
            .Select(notification => new { notification.Recipient.Channel, notification.Status })
            .GroupBy(row => new { row.Channel, row.Status })
            .Select(group => new { group.Key.Channel, group.Key.Status, Count = group.Count() })
            .ToListAsync(cancellationToken))
        .Select(row => new NotificationCount(row.Channel, row.Status, row.Count))
        .ToList();

    public async Task<IReadOnlyList<TimeSpan>> SendDelaysSinceAsync(DateTimeOffset since, int take, CancellationToken cancellationToken = default) =>
        (await All.AsNoTracking()
            .Where(notification => notification.CreatedAtUtc >= since && notification.SentAtUtc != null)
            .OrderByDescending(notification => notification.SentAtUtc)
            .Take(take)
            .Select(notification => new { notification.CreatedAtUtc, notification.ScheduledAtUtc, notification.SentAtUtc })
            .ToListAsync(cancellationToken))
        .Select(row => row.SentAtUtc!.Value - (row.ScheduledAtUtc ?? row.CreatedAtUtc))
        .ToList();

    public async Task<IReadOnlyList<Notification>> ListAbandonedAsync(int skip, int take, CancellationToken cancellationToken = default)
    {
        // « Abandonnée » se calcule dans le domaine (politique de relance, échec permanent) : on lit les échecs récents.
        var failed = await NotificationQueries.WithDeliveries(All).AsNoTracking()
            .Where(notification => notification.Status == NotificationStatus.Failed && notification.RedactedAtUtc == null)
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .Take((skip + take) * 4)
            .ToListAsync(cancellationToken);
        return failed.Where(notification => notification.IsDeliveryAbandoned).Skip(skip).Take(take).ToList();
    }

    public async Task<NotificationLocator?> LocateAsync(NotificationId id, CancellationToken cancellationToken = default)
    {
        var row = await All.AsNoTracking()
            .Where(notification => notification.Id == id)
            .Select(notification => new { notification.Id, notification.TenantId })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new NotificationLocator(row.Id, row.TenantId);
    }

    public Task<Notification?> LoadAnyAsync(NotificationId id, CancellationToken cancellationToken = default) =>
        NotificationQueries.WithDeliveries(All).SingleOrDefaultAsync(notification => notification.Id == id, cancellationToken);
}

internal sealed class DeviceRegistrationRepository(NotificationsDbContext context) : IDeviceRegistrationRepository
{
    public Task<DeviceRegistration?> FindAsync(UserId userId, string deviceId, CancellationToken cancellationToken = default) =>
        context.Devices.SingleOrDefaultAsync(device => device.UserId == userId && device.DeviceId == deviceId, cancellationToken);

    public async Task<IReadOnlyList<DeviceRegistration>> ListActiveAsync(UserId userId, CancellationToken cancellationToken = default) =>
        await context.Devices.AsNoTracking()
            .Where(device => device.UserId == userId && device.IsActive)
            .OrderBy(device => device.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DeviceRegistration>> ListByTokenAsync(string pushToken, CancellationToken cancellationToken = default) =>
        await context.Devices.Where(device => device.PushToken == pushToken && device.IsActive).ToListAsync(cancellationToken);

    public void Add(DeviceRegistration registration) => context.Devices.Add(registration);
}

internal sealed class OtpChallengeRepository(NotificationsDbContext context) : IOtpChallengeRepository
{
    public Task<OtpChallenge?> FindAsync(TenantId? owner, OtpChallengeId id, CancellationToken cancellationToken = default) =>
        context.OtpChallenges.SingleOrDefaultAsync(challenge => challenge.Id == id && challenge.TenantId == owner, cancellationToken);

    /// <summary>Tous tenants : la limite des demandes vaut pour le destinataire, où qu'il les fasse.</summary>
    public async Task<IReadOnlyList<OtpChallenge>> ListRecentAsync(
        OtpHash subjectKey, OtpPurpose purpose, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        await context.OtpChallenges.IgnoreQueryFilters()
            .Where(challenge => challenge.SubjectKey == subjectKey && challenge.Purpose == purpose && challenge.CreatedAtUtc >= since)
            .OrderByDescending(challenge => challenge.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public void Add(OtpChallenge challenge) => context.OtpChallenges.Add(challenge);
}

internal sealed class NotificationInbox(NotificationsDbContext context) : INotificationInbox
{
    public Task<bool> HasProcessedAsync(string consumer, string eventId, CancellationToken cancellationToken = default) =>
        context.Inbox.AnyAsync(message => message.Consumer == consumer && message.EventId == eventId, cancellationToken);

    public void MarkProcessed(string consumer, string eventId, string eventType, DateTimeOffset processedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        context.Inbox.Add(new InboxMessage
        {
            Consumer = consumer[..Math.Min(consumer.Length, InboxMessage.MaxConsumerLength)],
            EventId = eventId[..Math.Min(eventId.Length, InboxMessage.MaxEventIdLength)],
            EventType = eventType[..Math.Min(eventType.Length, 200)],
            ProcessedAtUtc = processedAtUtc,
        });
    }
}

/// <summary>
/// Compteur par (tenant, mois, canal, prestataire), incrémenté par une requête atomique (INSERT ... ON CONFLICT) : deux
/// envois simultanés ne perdent jamais une unité. Écrit IMMÉDIATEMENT, hors de l'unit of work : une consommation n'est
/// jamais annulée, même si la sauvegarde de la notification échoue ensuite (le message est parti).
/// </summary>
internal sealed class NotificationUsageCounter(NotificationsDbContext context) : INotificationUsage
{
    public async Task RecordAsync(
        TenantId tenantId, NotificationChannel channel, NotificationProviderCode provider, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var period = Period(at);
        var channelValue = (int)channel;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO notifications.notification_usage (tenant_id, period, channel, provider, count)
             VALUES ({tenantId.Value}, {period}, {channelValue}, {provider.Value}, 1)
             ON CONFLICT (tenant_id, period, channel, provider) DO UPDATE SET count = notification_usage.count + 1
             """,
            cancellationToken);
    }

    public async Task<long> CountAsync(TenantId tenantId, NotificationChannel channel, DateTimeOffset monthOf, CancellationToken cancellationToken = default)
    {
        var period = Period(monthOf);
        var channelValue = (int)channel;
        return await context.Usage.AsNoTracking()
            .Where(usage => usage.TenantId == tenantId.Value && usage.Period == period && usage.Channel == channelValue)
            .SumAsync(usage => usage.Count, cancellationToken);
    }

    private static DateOnly Period(DateTimeOffset at)
    {
        var utc = at.UtcDateTime;
        return new DateOnly(utc.Year, utc.Month, 1);
    }
}

internal sealed class CampaignRepository(NotificationsDbContext context) : ICampaignRepository
{
    public Task<Campaign?> FindAsync(TenantId tenantId, CampaignId id, CancellationToken cancellationToken = default) =>
        context.Campaigns.SingleOrDefaultAsync(campaign => campaign.TenantId == tenantId && campaign.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Campaign>> ListAsync(TenantId tenantId, int take, CancellationToken cancellationToken = default) =>
        await context.Campaigns.AsNoTracking()
            .Where(campaign => campaign.TenantId == tenantId)
            .OrderByDescending(campaign => campaign.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public void Add(Campaign campaign) => context.Campaigns.Add(campaign);
}

/// <summary>Tous tenants (worker) : campagnes en cours ou dont l'heure de départ est passée.</summary>
internal sealed class CampaignDirectory(NotificationsDbContext context) : ICampaignDirectory
{
    public async Task<IReadOnlyList<CampaignLocator>> ListDueAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default) =>
        (await context.Campaigns.IgnoreQueryFilters().AsNoTracking()
            .Where(campaign => campaign.Status == CampaignStatus.Sending
                || (campaign.Status == CampaignStatus.Scheduled && campaign.ScheduledForUtc <= now))
            .OrderBy(campaign => campaign.ScheduledForUtc)
            .Select(campaign => new { campaign.Id, campaign.TenantId })
            .Take(take)
            .ToListAsync(cancellationToken))
        .Select(row => new CampaignLocator(row.Id, row.TenantId))
        .ToList();
}

internal sealed class MarketingRulesRepository(NotificationsDbContext context) : IMarketingRulesRepository
{
    public Task<MarketingRules?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        context.MarketingRules.SingleOrDefaultAsync(rules => rules.TenantId == tenantId, cancellationToken);

    public void Add(MarketingRules rules) => context.MarketingRules.Add(rules);
}

/// <summary>Messages marketing créés pour une personne : comptés tant qu'ils n'ont été ni annulés ni en échec définitif.</summary>
internal sealed class MarketingHistory(NotificationsDbContext context) : IMarketingHistory
{
    public Task<bool> ReceivedSinceAsync(TenantId tenantId, RecipientId recipient, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        context.Notifications.IgnoreQueryFilters().AsNoTracking().AnyAsync(
            notification => notification.TenantId == tenantId
                && notification.RecipientId == recipient
                && notification.Purpose == NotificationPurpose.Marketing
                && notification.CreatedAtUtc > since
                && notification.Status != NotificationStatus.Cancelled
                && notification.Status != NotificationStatus.Failed,
            cancellationToken);
}
