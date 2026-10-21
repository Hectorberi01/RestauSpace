using System.Globalization;
using Identity.Application.Queries;
using Locations.Application.Queries;
using Notifications.Application.Commands;
using Notifications.Domain;
using SaasRestaurant.BuildingBlocks.Application;
using Staff.Application;
using Staff.Application.Abstractions;
using Staff.Application.Queries;
using Staff.Application.Scheduling;
using Staff.Domain;
using Tenants.Application.Queries;
using StaffContracts = SaasRestaurant.Contracts.Staff;

namespace SaasRestaurant.Host;

/// <summary>Staff (planning) -> Locations : nom et fuseau d'un établissement du tenant de la requête.</summary>
internal sealed class ScheduleLocationsFromLocations(
    ITenantContext tenantContext, IQueryHandler<GetLocationByIdQuery, LocationDto> locations) : IScheduleLocations
{
    public async Task<ScheduleLocation?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var location = await locations.HandleAsync(new GetLocationByIdQuery(locationId.Value), cancellationToken);
            return new ScheduleLocation(locationId, location.Name, location.TimeZoneId);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>
/// Le planning prévient l'équipe (Notifications) :
/// <list type="bullet">
/// <item>publication : chaque membre dont les créneaux ont changé, dans la console puis WhatsApp, à défaut SMS, sur
/// le téléphone de son compte ;</item>
/// <item>absence demandée : les gérants du planning, dans la console ; réponse : le membre ;</item>
/// <item>remplacement : le collègue sollicité, puis les gérants quand il accepte, puis les deux membres à la décision.</item>
/// </list>
/// Les messages ne citent jamais l'horaire d'un autre membre. Clés d'idempotence : un rejeu n'envoie rien de plus.
/// </summary>
internal sealed class ScheduleNoticesFlow(IServiceScopeFactory scopes, IConfiguration configuration) : IOutboxConsumer
{
    private static readonly NotificationChannel[] PhoneChannels = [NotificationChannel.WhatsApp, NotificationChannel.Sms];

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Module != "staff"
            || message.Type is not ("ScheduleWeekPublished" or "TimeOffRequested" or "TimeOffDecided" or "ShiftSwapRequested" or "ShiftSwapAccepted" or "ShiftSwapDecided"))
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var key = $"staff:{message.Id.ToString(CultureInfo.InvariantCulture)}";
        switch (message.Type)
        {
            case "ScheduleWeekPublished":
                var published = message.Read<StaffContracts.ScheduleWeekPublished>();
                services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(published.TenantId));
                await PublishedAsync(services, published, key, cancellationToken);
                break;
            case "TimeOffRequested":
                var requested = message.Read<StaffContracts.TimeOffRequested>();
                services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(requested.TenantId));
                await TimeOffAsync(services, requested.RequestId, toManagers: true, key, cancellationToken);
                break;
            case "TimeOffDecided":
                var decided = message.Read<StaffContracts.TimeOffDecided>();
                services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(decided.TenantId));
                await TimeOffAsync(services, decided.RequestId, toManagers: false, key, cancellationToken);
                break;
            case "ShiftSwapRequested":
                var asked = message.Read<StaffContracts.ShiftSwapRequested>();
                services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(asked.TenantId));
                await SwapAsync(services, asked.RequestId, SwapAudience.Colleague, key, cancellationToken);
                break;
            case "ShiftSwapAccepted":
                var accepted = message.Read<StaffContracts.ShiftSwapAccepted>();
                services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(accepted.TenantId));
                await SwapAsync(services, accepted.RequestId, SwapAudience.Managers, key, cancellationToken);
                break;
            default:
                var closed = message.Read<StaffContracts.ShiftSwapDecided>();
                services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(closed.TenantId));
                await SwapAsync(services, closed.RequestId, SwapAudience.Both, key, cancellationToken);
                break;
        }
    }

    private enum SwapAudience
    {
        Colleague,
        Managers,
        Both,
    }

    private async Task PublishedAsync(IServiceProvider services, StaffContracts.ScheduleWeekPublished published, string key, CancellationToken cancellationToken)
    {
        var publication = await services.GetRequiredService<IQueryHandler<GetSchedulePublicationQuery, SchedulePublicationDto?>>()
            .HandleAsync(new GetSchedulePublicationQuery(published.WeekId, published.AffectedMembers), cancellationToken);
        if (publication is null || publication.Recipients.Count == 0)
        {
            return;
        }

        var place = await PlaceAsync(services, publication.LocationId, cancellationToken);
        var url = $"{ConsoleBaseUrl()}/planning?vue=moi&semaine={publication.WeekStart:yyyy-MM-dd}";
        var send = services.GetRequiredService<ICommandHandler<SendNotificationCommand, SendNotificationResult>>();
        foreach (var recipient in publication.Recipients)
        {
            var user = await UserAsync(services, recipient.UserId, cancellationToken);
            if (user is null)
            {
                continue;
            }

            var french = IsFrench(user.Language);
            var variables = new Dictionary<string, string>(place, StringComparer.Ordinal)
            {
                ["schedule.week"] = publication.WeekStart.ToString(french ? "dd/MM/yyyy" : "yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["schedule.shifts"] = recipient.ShiftCount.ToString(CultureInfo.InvariantCulture),
                ["schedule.hours"] = Hours(recipient.PlannedMinutes),
                ["schedule.url"] = url,
            };
            var each = $"{key}:{recipient.StaffMemberId:N}";
            _ = await send.HandleAsync(
                new SendNotificationCommand(
                    NotificationTypes.StaffSchedulePublished.Key.Value, NotificationChannel.InApp, UserId: recipient.UserId, LocationId: publication.LocationId,
                    Variables: variables, IdempotencyKey: $"{each}:InApp"),
                cancellationToken);
            if (string.IsNullOrWhiteSpace(user.Phone))
            {
                continue;
            }

            foreach (var channel in PhoneChannels)
            {
                var result = await send.HandleAsync(
                    new SendNotificationCommand(
                        NotificationTypes.StaffSchedulePublished.Key.Value, channel, UserId: recipient.UserId, Address: user.Phone, LocationId: publication.LocationId,
                        Variables: variables, IdempotencyKey: $"{each}:{channel}"),
                    cancellationToken);
                if (result.NotificationIds.Count > 0)
                {
                    break;
                }
            }
        }
    }

    private static async Task TimeOffAsync(IServiceProvider services, Guid requestId, bool toManagers, string key, CancellationToken cancellationToken)
    {
        var notice = await services.GetRequiredService<IQueryHandler<GetTimeOffNoticeQuery, TimeOffNoticeDto?>>()
            .HandleAsync(new GetTimeOffNoticeQuery(requestId), cancellationToken);
        if (notice is null)
        {
            return;
        }

        var place = await PlaceAsync(services, null, cancellationToken);
        var send = services.GetRequiredService<ICommandHandler<SendNotificationCommand, SendNotificationResult>>();
        var period = notice.FirstDay == notice.LastDay
            ? notice.FirstDay.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{notice.FirstDay:dd/MM/yyyy} - {notice.LastDay:dd/MM/yyyy}");
        if (toManagers)
        {
            var variables = new Dictionary<string, string>(place, StringComparer.Ordinal) { ["staff.name"] = notice.Member.Name, ["timeoff.period"] = period };
            foreach (var manager in await ManagersAsync(services, cancellationToken))
            {
                if (manager == notice.Member.UserId)
                {
                    continue;
                }

                _ = await send.HandleAsync(
                    new SendNotificationCommand(
                        NotificationTypes.StaffTimeOffRequested.Key.Value, NotificationChannel.InApp, UserId: manager, Variables: variables,
                        IdempotencyKey: $"{key}:{manager:N}"),
                    cancellationToken);
            }

            return;
        }

        var user = await UserAsync(services, notice.Member.UserId, cancellationToken);
        var french = IsFrench(user?.Language);
        var status = notice.Status switch
        {
            TimeOffStatus.Approved => french ? "acceptée" : "approved",
            TimeOffStatus.Rejected => french ? "refusée" : "declined",
            _ => french ? "annulée" : "cancelled",
        };
        _ = await send.HandleAsync(
            new SendNotificationCommand(
                NotificationTypes.StaffTimeOffDecided.Key.Value, NotificationChannel.InApp, UserId: notice.Member.UserId,
                Variables: new Dictionary<string, string>(place, StringComparer.Ordinal) { ["timeoff.period"] = period, ["timeoff.status"] = status },
                IdempotencyKey: key),
            cancellationToken);
    }

    private static async Task SwapAsync(IServiceProvider services, Guid requestId, SwapAudience audience, string key, CancellationToken cancellationToken)
    {
        var notice = await services.GetRequiredService<IQueryHandler<GetShiftSwapNoticeQuery, ShiftSwapNoticeDto?>>()
            .HandleAsync(new GetShiftSwapNoticeQuery(requestId), cancellationToken);
        if (notice is null)
        {
            return;
        }

        var place = await PlaceAsync(services, notice.LocationId, cancellationToken);
        var send = services.GetRequiredService<ICommandHandler<SendNotificationCommand, SendNotificationResult>>();

        async Task NotifyAsync(Guid userId, string otherName, string status)
        {
            var variables = new Dictionary<string, string>(place, StringComparer.Ordinal)
            {
                ["staff.name"] = otherName,
                ["shift.date"] = notice.Date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "-",
                ["shift.time"] = notice.Start is { } start && notice.End is { } end
                    ? string.Create(CultureInfo.InvariantCulture, $"{start:HH\\:mm}-{end:HH\\:mm}")
                    : "-",
                ["swap.status"] = status,
            };
            _ = await send.HandleAsync(
                new SendNotificationCommand(
                    NotificationTypes.StaffShiftSwap.Key.Value, NotificationChannel.InApp, UserId: userId, LocationId: notice.LocationId, Variables: variables,
                    IdempotencyKey: $"{key}:{userId:N}"),
                cancellationToken);
        }

        switch (audience)
        {
            case SwapAudience.Colleague:
                await NotifyAsync(notice.To.UserId, notice.From.Name, "vous est proposé, à accepter dans Mon planning");
                break;
            case SwapAudience.Managers:
                foreach (var manager in await ManagersAsync(services, cancellationToken))
                {
                    await NotifyAsync(manager, $"{notice.From.Name} → {notice.To.Name}", "accepté par le collègue, à valider dans Planning");
                }

                break;
            default:
                var status = notice.Status switch
                {
                    ShiftSwapStatus.Approved => "validé",
                    ShiftSwapStatus.Declined => "refusé par le collègue",
                    ShiftSwapStatus.Rejected => "refusé par le gérant",
                    _ => "clos",
                };
                await NotifyAsync(notice.From.UserId, notice.To.Name, status);
                if (notice.Status != ShiftSwapStatus.Declined)
                {
                    await NotifyAsync(notice.To.UserId, notice.From.Name, status);
                }

                break;
        }
    }

    private static async Task<Dictionary<string, string>> PlaceAsync(IServiceProvider services, Guid? locationId, CancellationToken cancellationToken)
    {
        var tenant = services.GetRequiredService<ITenantContext>().RequireTenant();
        var restaurant = await services.GetRequiredService<IQueryHandler<GetTenantByIdQuery, TenantDto>>()
            .HandleAsync(new GetTenantByIdQuery(tenant.Value), cancellationToken);
        var place = new Dictionary<string, string>(StringComparer.Ordinal) { ["restaurant.name"] = restaurant.Name, ["location.name"] = string.Empty };
        if (locationId is { } id)
        {
            try
            {
                place["location.name"] = (await services.GetRequiredService<IQueryHandler<GetLocationByIdQuery, LocationDto>>()
                    .HandleAsync(new GetLocationByIdQuery(id), cancellationToken)).Name;
            }
            catch (NotFoundException)
            {
            }
        }

        return place;
    }

    private static async Task<UserDto?> UserAsync(IServiceProvider services, Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            var user = await services.GetRequiredService<IQueryHandler<GetUserByIdQuery, UserDto>>().HandleAsync(new GetUserByIdQuery(userId), cancellationToken);
            return user.ErasedAtUtc is null ? user : null;
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    /// <summary>Les utilisateurs actifs du restaurant qui tiennent le planning.</summary>
    private static async Task<List<Guid>> ManagersAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var tenantId = services.GetRequiredService<ITenantContext>().RequireTenant();
        var list = services.GetRequiredService<IQueryHandler<ListStaffQuery, PagedResult<StaffMemberDto>>>();
        var permissions = services.GetRequiredService<IPermissionChecker>();
        var managers = new List<Guid>();
        for (var page = 1; ; page++)
        {
            var members = await list.HandleAsync(new ListStaffQuery(page, ListStaffQuery.MaxPageSize), cancellationToken);
            foreach (var member in members.Items.Where(member => member.Status == StaffStatus.Active))
            {
                if (await permissions.HasTenantPermissionAsync(UserId.From(member.UserId), tenantId, StaffPermissions.Schedule, cancellationToken))
                {
                    managers.Add(member.UserId);
                }
            }

            if (!members.HasMore || members.Items.Count == 0)
            {
                return managers;
            }
        }
    }

    private string ConsoleBaseUrl()
    {
        if (configuration["Staff:ConsoleBaseUrl"] is { Length: > 0 } configured)
        {
            return configured.TrimEnd('/');
        }

        // À défaut, l'origine des liens d'invitation : c'est la console.
        return Uri.TryCreate(configuration["Identity:Emails:Links:AcceptInvitation"], UriKind.Absolute, out var link)
            ? link.GetLeftPart(UriPartial.Authority)
            : string.Empty;
    }

    private static bool IsFrench(string? language) => language is null || language.StartsWith("fr", StringComparison.OrdinalIgnoreCase);

    private static string Hours(int minutes) =>
        minutes % 60 == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes / 60} h")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes / 60} h {minutes % 60:00}");
}
