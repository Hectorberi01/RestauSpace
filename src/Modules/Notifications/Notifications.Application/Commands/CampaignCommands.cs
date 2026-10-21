using Notifications.Application.Abstractions;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Application.Commands;

// Campagnes marketing : brouillon, programmation, envoi par lots par le worker, arrêt ; règle de fréquence du restaurant.

public sealed record CampaignDto(
    Guid Id,
    string Name,
    string Type,
    NotificationChannel Channel,
    string? Tag,
    Guid? LocationId,
    IReadOnlyDictionary<string, string> Variables,
    CampaignStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ScheduledForUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    int Processed,
    int Created,
    int Skipped,
    int SkippedForFrequency,
    bool OnlyLocationCustomers = false)
{
    public static CampaignDto From(Campaign campaign) => new(
        campaign.Id.Value,
        campaign.Name,
        campaign.TypeKey,
        campaign.Channel,
        campaign.Tag,
        campaign.LocationId?.Value,
        campaign.Variables,
        campaign.Status,
        campaign.CreatedAtUtc,
        campaign.ScheduledForUtc,
        campaign.StartedAtUtc,
        campaign.CompletedAtUtc,
        campaign.Processed,
        campaign.Created,
        campaign.Skipped,
        campaign.SkippedForFrequency,
        campaign.OnlyLocationCustomers);
}

/// <summary>Crée (sans id) ou modifie un brouillon. Renvoie son id.</summary>
public sealed record SaveCampaignCommand(
    Guid? CampaignId,
    string? Name,
    string Type,
    NotificationChannel Channel,
    string? Tag,
    Guid? LocationId,
    IReadOnlyDictionary<string, string>? Variables,
    bool OnlyLocationCustomers = false) : ICommand<Guid>;

public sealed class SaveCampaignHandler(NotificationsWorkspace workspace, ICampaignRepository campaigns, INotificationLocations locations, IClock clock)
    : ICommandHandler<SaveCampaignCommand, Guid>
{
    public async Task<Guid> HandleAsync(SaveCampaignCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        LocationId? location = null;
        if (command.LocationId is { } id)
        {
            location = (id == Guid.Empty ? null : await locations.FindAsync(tenant, LocationId.From(id), cancellationToken))?.LocationId
                ?? throw new NotFoundException(NotificationsApplicationErrors.LocationNotFound);
        }

        if (command.CampaignId is { } existingId)
        {
            var existing = await campaigns.LoadAsync(tenant, existingId, cancellationToken);
            existing.Change(command.Name, command.Type, command.Channel, command.Tag, location, command.Variables, command.OnlyLocationCustomers);
            await workspace.SaveAsync(cancellationToken);
            return existing.Id.Value;
        }

        var campaign = Campaign.Create(tenant, command.Name, command.Type, command.Channel, command.Tag, location, command.Variables, clock, command.OnlyLocationCustomers);
        campaigns.Add(campaign);
        await workspace.SaveAsync(cancellationToken);
        return campaign.Id.Value;
    }
}

/// <summary>Programme l'envoi (<paramref name="At"/> null : dès que possible).</summary>
public sealed record ScheduleCampaignCommand(Guid CampaignId, DateTimeOffset? At) : ICommand;

public sealed class ScheduleCampaignHandler(NotificationsWorkspace workspace, ICampaignRepository campaigns, IClock clock) : ICommandHandler<ScheduleCampaignCommand>
{
    public async Task HandleAsync(ScheduleCampaignCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var campaign = await campaigns.LoadAsync(workspace.CurrentTenant, command.CampaignId, cancellationToken);
        campaign.Schedule(command.At, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record UnscheduleCampaignCommand(Guid CampaignId) : ICommand;

public sealed class UnscheduleCampaignHandler(NotificationsWorkspace workspace, ICampaignRepository campaigns) : ICommandHandler<UnscheduleCampaignCommand>
{
    public async Task HandleAsync(UnscheduleCampaignCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var campaign = await campaigns.LoadAsync(workspace.CurrentTenant, command.CampaignId, cancellationToken);
        campaign.Unschedule();
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record CancelCampaignCommand(Guid CampaignId) : ICommand;

public sealed class CancelCampaignHandler(NotificationsWorkspace workspace, ICampaignRepository campaigns, IClock clock) : ICommandHandler<CancelCampaignCommand>
{
    public async Task HandleAsync(CancelCampaignCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var campaign = await campaigns.LoadAsync(workspace.CurrentTenant, command.CampaignId, cancellationToken);
        campaign.Cancel(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record ListCampaignsQuery : IQuery<IReadOnlyList<CampaignDto>>;

public sealed class ListCampaignsHandler(NotificationsWorkspace workspace, ICampaignRepository campaigns) : IQueryHandler<ListCampaignsQuery, IReadOnlyList<CampaignDto>>
{
    public async Task<IReadOnlyList<CampaignDto>> HandleAsync(ListCampaignsQuery query, CancellationToken cancellationToken = default) =>
        (await campaigns.ListAsync(workspace.CurrentTenant, 100, cancellationToken)).Select(CampaignDto.From).ToList();
}

public sealed record GetCampaignQuery(Guid CampaignId) : IQuery<CampaignDto>;

public sealed class GetCampaignHandler(NotificationsWorkspace workspace, ICampaignRepository campaigns) : IQueryHandler<GetCampaignQuery, CampaignDto>
{
    public async Task<CampaignDto> HandleAsync(GetCampaignQuery query, CancellationToken cancellationToken = default) =>
        CampaignDto.From(await campaigns.LoadAsync(workspace.CurrentTenant, query.CampaignId, cancellationToken));
}

/// <summary>Nombre de clients joignables : consentement accordé sur le canal, adresse connue, tag éventuel.</summary>
public sealed record CampaignAudienceDto(int Count);

/// <param name="CustomersOf">Seulement les clients de cet établissement ; null : tous.</param>
public sealed record PreviewCampaignAudienceQuery(NotificationChannel Channel, string? Tag, Guid? CustomersOf = null) : IQuery<CampaignAudienceDto>;

public sealed class PreviewCampaignAudienceHandler(NotificationsWorkspace workspace, ICampaignAudience audience)
    : IQueryHandler<PreviewCampaignAudienceQuery, CampaignAudienceDto>
{
    public async Task<CampaignAudienceDto> HandleAsync(PreviewCampaignAudienceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        DomainException.ThrowIf(
            query.Channel is not (NotificationChannel.Email or NotificationChannel.Sms or NotificationChannel.WhatsApp), CampaignErrors.ChannelInvalid);
        var tag = string.IsNullOrWhiteSpace(query.Tag) ? null : query.Tag.Trim().ToLowerInvariant();
        var customersOf = query.CustomersOf is { } location && location != Guid.Empty ? LocationId.From(location) : (LocationId?)null;
        return new CampaignAudienceDto(
            await audience.CountAsync(workspace.CurrentTenant, query.Channel.ToConsentChannel()!.Value, tag, customersOf, cancellationToken));
    }
}

/// <summary>
/// Un lot d'envoi d'une campagne due, POUR le tenant du contexte (worker). Chaque client de l'audience devient une
/// demande d'envoi ordinaire : la politique revérifie consentement, désinscription, fréquence, quota et modèle. Clé
/// d'idempotence par campagne et client : un lot rejoué ne crée rien deux fois. Renvoie vrai s'il reste des clients.
/// </summary>
public sealed record ProcessCampaignCommand(Guid CampaignId) : ICommand<bool>;

public sealed class ProcessCampaignHandler(
    NotificationsWorkspace workspace,
    ICampaignRepository campaigns,
    ICampaignAudience audience,
    INotificationTenants tenants,
    NotificationRequests requests,
    NotificationsSettings settings,
    IClock clock) : ICommandHandler<ProcessCampaignCommand, bool>
{
    public async Task<bool> HandleAsync(ProcessCampaignCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        var campaign = await campaigns.LoadAsync(tenant, command.CampaignId, cancellationToken);
        if (!campaign.IsDue(clock.UtcNow) || !await tenants.AcceptsBusinessWritesAsync(tenant, cancellationToken))
        {
            return false;
        }

        var batchSize = Math.Clamp(settings.CampaignBatchSize, 1, 1000);
        var customers = await audience.ListAsync(
            tenant, campaign.Channel.ToConsentChannel()!.Value, campaign.Tag, campaign.Processed, batchSize,
            campaign.OnlyLocationCustomers ? campaign.LocationId : null, cancellationToken);
        var variables = new Dictionary<string, string>(campaign.Variables, StringComparer.Ordinal);
        if (!variables.ContainsKey("restaurant.name") && await tenants.DisplayNameAsync(tenant, cancellationToken) is { } name)
        {
            variables["restaurant.name"] = name;
        }

        int created = 0, skipped = 0, frequency = 0;
        foreach (var customer in customers)
        {
            SendNotificationResult result;
            try
            {
                result = await requests.CreateAsync(
                    tenant,
                    new SendNotificationCommand(
                        campaign.TypeKey, campaign.Channel, customer.Value, LocationId: campaign.LocationId?.Value, Variables: variables,
                        IdempotencyKey: $"campaign:{campaign.Id.Value:N}:{customer.Value:N}"),
                    cancellationToken);
            }
            catch (NotFoundException)
            {
                // Fiche anonymisée ou sans adresse depuis la lecture de l'audience.
                skipped++;
                continue;
            }

            if (result.NotificationIds.Count > 0)
            {
                created++;
            }
            else
            {
                skipped++;
                if (result.Skipped == NotificationSkipReason.FrequencyLimited)
                {
                    frequency++;
                }
            }
        }

        var exhausted = customers.Count < batchSize;
        campaign.RecordBatch(customers.Count, created, skipped, frequency, exhausted, clock);
        await workspace.SaveAsync(cancellationToken);
        return !exhausted;
    }
}

public sealed record MarketingRulesDto(int MinimumIntervalDays);

public sealed record GetMarketingRulesQuery : IQuery<MarketingRulesDto>;

public sealed class GetMarketingRulesHandler(NotificationsWorkspace workspace, IMarketingRulesRepository rules) : IQueryHandler<GetMarketingRulesQuery, MarketingRulesDto>
{
    public async Task<MarketingRulesDto> HandleAsync(GetMarketingRulesQuery query, CancellationToken cancellationToken = default) =>
        new((await rules.FindAsync(workspace.CurrentTenant, cancellationToken))?.MinimumIntervalDays ?? MarketingRules.DefaultIntervalDays);
}

public sealed record SetMarketingRulesCommand(int MinimumIntervalDays) : ICommand;

public sealed class SetMarketingRulesHandler(NotificationsWorkspace workspace, IMarketingRulesRepository rules, IClock clock) : ICommandHandler<SetMarketingRulesCommand>
{
    public async Task HandleAsync(SetMarketingRulesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        if (await rules.FindAsync(tenant, cancellationToken) is { } existing)
        {
            existing.Change(command.MinimumIntervalDays, clock);
        }
        else
        {
            rules.Add(MarketingRules.Create(tenant, command.MinimumIntervalDays, clock));
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

internal static class CampaignLoading
{
    public static async Task<Campaign> LoadAsync(this ICampaignRepository campaigns, TenantId tenant, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await campaigns.FindAsync(tenant, CampaignId.From(id), cancellationToken))
        ?? throw new NotFoundException(NotificationsApplicationErrors.CampaignNotFound);
}
