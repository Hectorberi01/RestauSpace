using Notifications.Application.Abstractions;
using Notifications.Application.Commands;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Application.Queries;

// Catalogue de la plateforme.

public sealed record ListNotificationProvidersQuery(bool OnlyEnabled = false) : IQuery<IReadOnlyList<NotificationProviderDto>>;

/// <summary>
/// Le catalogue. Pour un prestataire qui accepte le compte de la plateforme, <see cref="NotificationProviderDto.HasPlatformCredentials"/>
/// dit si ses identifiants sont rangés (et lisibles) au coffre, sans jamais les renvoyer.
/// </summary>
public sealed class ListNotificationProvidersHandler(INotificationProviderRepository providers, INotificationSenderResolver resolver, INotificationSecretStore secrets)
    : IQueryHandler<ListNotificationProvidersQuery, IReadOnlyList<NotificationProviderDto>>
{
    public async Task<IReadOnlyList<NotificationProviderDto>> HandleAsync(ListNotificationProvidersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var result = new List<NotificationProviderDto>();
        foreach (var provider in (await providers.ListAsync(cancellationToken)).Where(provider => !query.OnlyEnabled || provider.IsEnabled))
        {
            var integration = resolver.FindIntegration(provider.Code);
            var hasIntegration = provider.Code == NotificationProviderCode.InApp || integration is not null;
            bool? platformAccount = null;
            if (provider.ConfigurationType.AllowsPlatformAccount() && integration is not null)
            {
                platformAccount = integration.CredentialFields.Count == 0 || await HasPlatformCredentialsAsync(provider.Code, cancellationToken);
            }

            result.Add(NotificationProviderDto.From(provider, hasIntegration, integration?.CredentialFields) with { HasPlatformCredentials = platformAccount });
        }

        return result;
    }

    private async Task<bool> HasPlatformCredentialsAsync(NotificationProviderCode code, CancellationToken cancellationToken)
    {
        try
        {
            return await secrets.ReadAsync(NotificationCredentialReference.ForPlatform(code), cancellationToken) is not null;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}

public sealed record ListNotificationIntegrationsQuery : IQuery<IReadOnlyList<NotificationIntegrationDto>>;

public sealed class ListNotificationIntegrationsHandler(INotificationSenderResolver resolver)
    : IQueryHandler<ListNotificationIntegrationsQuery, IReadOnlyList<NotificationIntegrationDto>>
{
    public Task<IReadOnlyList<NotificationIntegrationDto>> HandleAsync(ListNotificationIntegrationsQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<NotificationIntegrationDto>>(resolver.Integrations.Select(NotificationIntegrationDto.From).ToList());
}

/// <summary>
/// Le catalogue des types (décision du produit, aucune donnée). Pour un restaurant : les seuls types adressés aux
/// clients, ceux dont il peut écrire les modèles.
/// </summary>
public sealed record ListNotificationTypesQuery(TemplateOwner Owner) : IQuery<IReadOnlyList<NotificationTypeDto>>;

public sealed class ListNotificationTypesHandler : IQueryHandler<ListNotificationTypesQuery, IReadOnlyList<NotificationTypeDto>>
{
    public Task<IReadOnlyList<NotificationTypeDto>> HandleAsync(ListNotificationTypesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return Task.FromResult<IReadOnlyList<NotificationTypeDto>>(NotificationTypes.All
            .Where(type => query.Owner != TemplateOwner.Tenant || type.Audience == "customer")
            .OrderBy(type => type.IsMarketing)
            .ThenBy(type => type.Key.Value, StringComparer.Ordinal)
            .Select(NotificationTypeDto.From)
            .ToList());
    }
}

// Configurations du restaurant.

public sealed record ListNotificationConfigurationsQuery : IQuery<IReadOnlyList<NotificationConfigurationDto>>;

public sealed class ListNotificationConfigurationsHandler(NotificationsWorkspace workspace)
    : IQueryHandler<ListNotificationConfigurationsQuery, IReadOnlyList<NotificationConfigurationDto>>
{
    public async Task<IReadOnlyList<NotificationConfigurationDto>> HandleAsync(
        ListNotificationConfigurationsQuery query, CancellationToken cancellationToken = default) =>
        (await workspace.Configurations.ListAsync(workspace.CurrentTenant, cancellationToken)).Select(NotificationConfigurationDto.From).ToList();
}

// Templates : ceux du propriétaire ; pour un restaurant, aussi ceux de la plateforme en lecture (IsPlatform).

public sealed record ListTemplatesQuery(TemplateOwner Owner, bool IncludePlatform = true) : IQuery<IReadOnlyList<NotificationTemplateDto>>;

public sealed class ListTemplatesHandler(NotificationsWorkspace workspace) : IQueryHandler<ListTemplatesQuery, IReadOnlyList<NotificationTemplateDto>>
{
    public async Task<IReadOnlyList<NotificationTemplateDto>> HandleAsync(ListTemplatesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var owner = query.Owner == TemplateOwner.Platform ? (TenantId?)null : workspace.CurrentTenant;
        var own = await workspace.Templates.ListAsync(owner, cancellationToken);
        var platform = owner is not null && query.IncludePlatform
            ? await workspace.Templates.ListAsync(null, cancellationToken)
            : Array.Empty<NotificationTemplate>();
        return own.Concat(platform)
            .OrderBy(template => template.Key.Value, StringComparer.Ordinal)
            .ThenBy(template => template.Channel)
            .ThenBy(template => template.Language.Value, StringComparer.Ordinal)
            .ThenBy(template => template.IsPlatformLevel)
            .ThenByDescending(template => template.Version)
            .Select(NotificationTemplateDto.From)
            .ToList();
    }
}

public sealed record GetTemplateQuery(TemplateOwner Owner, Guid TemplateId) : IQuery<NotificationTemplateDto>;

public sealed class GetTemplateHandler(NotificationsWorkspace workspace) : IQueryHandler<GetTemplateQuery, NotificationTemplateDto>
{
    public async Task<NotificationTemplateDto> HandleAsync(GetTemplateQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var owner = query.Owner == TemplateOwner.Platform ? (TenantId?)null : workspace.CurrentTenant;
        return NotificationTemplateDto.From(await workspace.LoadTemplateAsync(owner, query.TemplateId, cancellationToken));
    }
}

// Historique du restaurant.

public sealed record ListNotificationsQuery(
    NotificationStatus? Status = null,
    NotificationChannel? Channel = null,
    string? Type = null,
    Guid? CustomerId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    int Skip = 0,
    int Take = 50) : IQuery<IReadOnlyList<NotificationDto>>;

public sealed class ListNotificationsHandler(NotificationsWorkspace workspace) : IQueryHandler<ListNotificationsQuery, IReadOnlyList<NotificationDto>>
{
    public async Task<IReadOnlyList<NotificationDto>> HandleAsync(ListNotificationsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        NotificationsWorkspace.EnsurePage(query.Skip, query.Take);
        var filter = new NotificationFilter(
            query.Status,
            query.Channel,
            string.IsNullOrWhiteSpace(query.Type) ? null : TemplateKey.Create(query.Type),
            query.CustomerId is { } customer ? RecipientId.ForCustomer(CustomerId.From(customer)) : null,
            query.From,
            query.To);
        return (await workspace.Notifications.ListAsync(workspace.CurrentTenant, filter, query.Skip, query.Take, cancellationToken))
            .Select(NotificationDto.From)
            .ToList();
    }
}

public sealed record GetNotificationQuery(Guid NotificationId) : IQuery<NotificationDto>;

public sealed class GetNotificationHandler(NotificationsWorkspace workspace) : IQueryHandler<GetNotificationQuery, NotificationDto>
{
    public async Task<NotificationDto> HandleAsync(GetNotificationQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return NotificationDto.From(await workspace.LoadNotificationAsync(workspace.CurrentTenant, query.NotificationId, cancellationToken));
    }
}

// Boîte in-app de l'utilisateur connecté, dans le restaurant de son access token.

public sealed record ListMyInAppNotificationsQuery(bool IncludeArchived = false, int Take = 50, int Skip = 0) : IQuery<IReadOnlyList<InAppNotificationDto>>;

public sealed class ListMyInAppNotificationsHandler(NotificationsWorkspace workspace, INotificationsCurrentUser currentUser)
    : IQueryHandler<ListMyInAppNotificationsQuery, IReadOnlyList<InAppNotificationDto>>
{
    public async Task<IReadOnlyList<InAppNotificationDto>> HandleAsync(ListMyInAppNotificationsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        NotificationsWorkspace.EnsurePage(query.Skip, query.Take);
        var user = currentUser.UserId ?? throw new ForbiddenException(NotificationsApplicationErrors.UserRequired);
        return (await workspace.Notifications.ListInAppAsync(workspace.CurrentTenant, user, query.IncludeArchived, query.Skip, query.Take, cancellationToken))
            .Select(InAppNotificationDto.From)
            .ToList();
    }
}

// Préférences.

public sealed record GetNotificationPreferencesQuery(PreferenceSubject Subject) : IQuery<NotificationPreferencesDto>;

public sealed class GetNotificationPreferencesHandler(
    NotificationsWorkspace workspace, INotificationContacts contacts, INotificationsCurrentUser currentUser)
    : IQueryHandler<GetNotificationPreferencesQuery, NotificationPreferencesDto>
{
    public async Task<NotificationPreferencesDto> HandleAsync(GetNotificationPreferencesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var owner = await PreferenceOwners.ResolveAsync(tenant, query.Subject, contacts, currentUser, cancellationToken);
        return NotificationPreferencesDto.From(owner, await workspace.Preferences.FindAsync(tenant, owner, cancellationToken));
    }
}

// Dead letter (administration de la plateforme, tous tenants).

/// <summary>Administration : le journal d'envoi de tous les restaurants, les plus récents d'abord, sans destinataire.</summary>
public sealed record ListPlatformNotificationsQuery(NotificationChannel? Channel = null, NotificationStatus? Status = null, int Take = 50, int Skip = 0)
    : IQuery<IReadOnlyList<NotificationLogDto>>;

public sealed class ListPlatformNotificationsHandler(INotificationDirectory directory)
    : IQueryHandler<ListPlatformNotificationsQuery, IReadOnlyList<NotificationLogDto>>
{
    public async Task<IReadOnlyList<NotificationLogDto>> HandleAsync(ListPlatformNotificationsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        NotificationsWorkspace.EnsurePage(query.Skip, query.Take);
        return (await directory.ListRecentAsync(query.Channel, query.Status, query.Skip, query.Take, cancellationToken))
            .Select(NotificationLogDto.From)
            .ToList();
    }
}

/// <summary>Administration : les indicateurs d'envoi depuis <paramref name="SinceUtc"/> (défaut : début du mois civil UTC).</summary>
public sealed record GetNotificationsOverviewQuery(DateTimeOffset? SinceUtc = null) : IQuery<NotificationsOverviewDto>;

public sealed class GetNotificationsOverviewHandler(INotificationDirectory directory, IClock clock)
    : IQueryHandler<GetNotificationsOverviewQuery, NotificationsOverviewDto>
{
    /// <summary>Le délai moyen se calcule sur les derniers envois acceptés, pas sur tout le mois.</summary>
    public const int DelaySample = 1000;

    public async Task<NotificationsOverviewDto> HandleAsync(GetNotificationsOverviewQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var now = clock.UtcNow;
        var since = query.SinceUtc ?? new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var counts = await directory.CountSinceAsync(since, cancellationToken);
        var delays = await directory.SendDelaysSinceAsync(since, DelaySample, cancellationToken);

        static int Sum(IEnumerable<NotificationCount> rows, Func<NotificationStatus, bool> filter) =>
            rows.Where(row => filter(row.Status)).Sum(row => row.Count);

        var channels = counts
            .GroupBy(row => row.Channel)
            .OrderBy(group => group.Key)
            .Select(group => new NotificationChannelOverviewDto(
                group.Key, group.Sum(row => row.Count), Sum(group, status => status.IsHandedOver()), Sum(group, status => status == NotificationStatus.Failed)))
            .ToList();
        return new NotificationsOverviewDto(
            since,
            counts.Sum(row => row.Count),
            Sum(counts, status => status.IsHandedOver()),
            Sum(counts, status => status == NotificationStatus.Delivered),
            Sum(counts, status => status == NotificationStatus.Failed),
            Sum(counts, status => status is NotificationStatus.Pending or NotificationStatus.Scheduled or NotificationStatus.Processing),
            Sum(counts, status => status == NotificationStatus.Cancelled),
            delays.Count == 0 ? null : Math.Round(delays.Average(delay => Math.Max(0, delay.TotalSeconds)), 1),
            channels);
    }
}

public sealed record ListDeadLettersQuery(int Take = 50, int Skip = 0) : IQuery<IReadOnlyList<DeadLetterDto>>;

public sealed class ListDeadLettersHandler(INotificationDirectory directory) : IQueryHandler<ListDeadLettersQuery, IReadOnlyList<DeadLetterDto>>
{
    public async Task<IReadOnlyList<DeadLetterDto>> HandleAsync(ListDeadLettersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        NotificationsWorkspace.EnsurePage(query.Skip, query.Take);
        return (await directory.ListAbandonedAsync(query.Skip, query.Take, cancellationToken)).Select(DeadLetterDto.From).ToList();
    }
}
