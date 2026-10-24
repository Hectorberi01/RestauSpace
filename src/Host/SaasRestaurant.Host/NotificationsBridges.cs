using System.Globalization;
using System.Text.Json;
using Customers.Application.Queries;
using Locations.Application.Queries;
using Notifications.Application.Abstractions;
using Notifications.Application.Commands;
using Notifications.Domain;
using Orders.Application.Queries;
using Reservations.Application.Queries;
using SaasRestaurant.BuildingBlocks.Application;
using Tenants.Application.Queries;

namespace SaasRestaurant.Host;

// Les ports déclarés par Notifications, servis par les cas d'usage des autres modules, et la traduction de leurs
// événements en notifications (§ 17). Aucun module n'en référence un autre : seul le host les relie. Les queries
// répondent pour le tenant du contexte (requête ou BackgroundTenant) : jamais pour un autre.

/// <summary>Notifications -> Tenants : le tenant accepte-t-il les écritures métier ? Inconnu = non.</summary>
internal sealed class NotificationTenantsFromTenants(CachedTenants tenants) : INotificationTenants
{
    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);

    public async Task<string?> DisplayNameAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await tenants.FindAsync(tenantId, cancellationToken) is { } tenant
            ? string.IsNullOrWhiteSpace(tenant.Branding.DisplayName) ? tenant.Name : tenant.Branding.DisplayName
            : null;
}

/// <summary>Notifications -> Locations : l'établissement du tenant et son pays (choix d'un prestataire, pays d'un envoi anonyme).</summary>
internal sealed class NotificationLocationsFromLocations(
    ITenantContext tenantContext,
    IQueryHandler<GetLocationByIdQuery, LocationDto> locations,
    IQueryHandler<ListLocationsQuery, PagedResult<LocationSummaryDto>> list)
    : INotificationLocations
{
    public async Task<IReadOnlyList<CountryCode>> CountriesAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return [];
        }

        var countries = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 1; page <= ListLocationsQuery.MaxPage; page++)
        {
            var found = await list.HandleAsync(new ListLocationsQuery(page, ListLocationsQuery.MaxPageSize), cancellationToken);
            countries.UnionWith(found.Items.Select(location => location.CountryCode));
            if (found.Items.Count < ListLocationsQuery.MaxPageSize)
            {
                break;
            }
        }

        return countries.Select(CountryCode.Create).ToList();
    }

    public async Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var location = await locations.HandleAsync(new GetLocationByIdQuery(locationId.Value), cancellationToken);
            return new LocationReference(tenantId, locationId, CountryCode.Create(location.Address.CountryCode));
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Notifications -> Customers : coordonnées minimales et langue d'un client (rien d'autre ne traverse).</summary>
internal sealed class NotificationContactsFromCustomers(ITenantContext tenantContext, IQueryHandler<GetCustomerByIdQuery, CustomerDto> customers)
    : INotificationContacts
{
    public async Task<CustomerContact?> FindCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var customer = await customers.HandleAsync(new GetCustomerByIdQuery(customerId.Value), cancellationToken);
            return new CustomerContact(
                customerId,
                customer.Email is { Length: > 0 } email ? Email.Create(email) : null,
                customer.Phone is { Length: > 0 } phone ? PhoneNumber.Create(phone) : null,
                customer.IsAnonymized ? null : customer.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
                customer.Preferences?.Language is { Length: >= 2 } language ? language[..2] : null,
                customer.IsAnonymized);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Notifications -> Customers : la preuve de consentement marketing d'un client pour un canal.</summary>
internal sealed class NotificationConsentsFromCustomers(
    ITenantContext tenantContext, IQueryHandler<GetMarketingPermissionQuery, MarketingPermissionDto> permissions) : INotificationConsents
{
    public async Task<ConsentProof?> FindAsync(TenantId tenantId, CustomerId customerId, CommunicationChannel channel, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var permission = await permissions.HandleAsync(new GetMarketingPermissionQuery(customerId.Value, channel), cancellationToken);
            return permission.CanSend ? ConsentProof.Granted(customerId, channel) : ConsentProof.Denied(customerId, channel);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>
/// Notifications -> Customers : l'audience d'une campagne (ids des clients joignables en marketing). Pour les clients
/// d'un établissement, Orders donne ceux qui y ont au moins une commande non annulée.
/// </summary>
internal sealed class CampaignAudienceFromCustomers(
    ITenantContext tenantContext,
    IQueryHandler<CountMarketingAudienceQuery, int> count,
    IQueryHandler<ListMarketingAudienceQuery, IReadOnlyList<Guid>> list,
    IQueryHandler<Orders.Application.Queries.ListLocationCustomersQuery, IReadOnlyList<Guid>> locationCustomers) : ICampaignAudience
{
    public async Task<int> CountAsync(
        TenantId tenantId, CommunicationChannel channel, string? tag, LocationId? customersOf = null, CancellationToken cancellationToken = default) =>
        tenantContext.CurrentTenantId == tenantId
            ? await count.HandleAsync(new CountMarketingAudienceQuery(channel, tag, await AmongAsync(customersOf, cancellationToken)), cancellationToken)
            : 0;

    public async Task<IReadOnlyList<CustomerId>> ListAsync(
        TenantId tenantId, CommunicationChannel channel, string? tag, int skip, int take, LocationId? customersOf = null,
        CancellationToken cancellationToken = default) =>
        tenantContext.CurrentTenantId == tenantId
            ? (await list.HandleAsync(new ListMarketingAudienceQuery(channel, tag, skip, take, await AmongAsync(customersOf, cancellationToken)), cancellationToken))
                .Select(CustomerId.From).ToList()
            : [];

    private async Task<IReadOnlyCollection<Guid>?> AmongAsync(LocationId? customersOf, CancellationToken cancellationToken) =>
        customersOf is { } location
            ? await locationCustomers.HandleAsync(new Orders.Application.Queries.ListLocationCustomersQuery(location.Value), cancellationToken)
            : null;
}

/// <summary>
/// Événements des autres modules -> intentions de notification (§ 17-18), par l'outbox (après commit, au moins une
/// fois). La politique d'envoi décide ensuite (préférences, consentement, template, prestataire, quota) ; l'inbox du
/// module garantit qu'un événement reçu deux fois ne crée rien de plus. Chaque message est traité dans son propre scope,
/// pour le tenant du message. Un refus métier part en file morte ; les autres erreurs sont retentées. Journal : ids
/// seulement.
/// </summary>
internal sealed class NotificationTriggersFlow(IServiceScopeFactory scopes, ILogger<NotificationTriggersFlow> logger) : IOutboxConsumer
{
    public const string Consumer = "notifications.triggers";

    /// <summary>Canaux essayés pour un client, dans l'ordre : le premier possible est le seul utilisé.</summary>
    private static readonly NotificationChannel[] CustomerChannels = [NotificationChannel.Email, NotificationChannel.WhatsApp, NotificationChannel.Sms];

    /// <summary>Une réservation ne dit pas qui l'a prise (site ou équipe) : toujours traitée comme anonyme.</summary>
    internal const bool ReservationIsAnonymous = true;

    /// <summary>Rappel de réservation : quelques heures avant l'arrivée.</summary>
    private static readonly TimeSpan ReminderBefore = TimeSpan.FromHours(3);

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not (
            ("orders", "OrderPlaced") or ("orders", "OrderReady") or ("orders", "OrderCancelled")
            or ("reservations", "ReservationConfirmed") or ("reservations", "ReservationCancelled")
            or ("payments", "PaymentFailed")))
        {
            return;
        }

        var (tenantGuid, subjectId) = message.Type switch
        {
            "OrderPlaced" => Of(message.Read<SaasRestaurant.Contracts.Orders.OrderPlaced>()),
            "OrderReady" => Of(message.Read<SaasRestaurant.Contracts.Orders.OrderReady>()),
            "OrderCancelled" => Of(message.Read<SaasRestaurant.Contracts.Orders.OrderCancelled>()),
            "PaymentFailed" => Of(message.Read<SaasRestaurant.Contracts.Payments.PaymentFailed>()),
            "ReservationConfirmed" => Of(message.Read<SaasRestaurant.Contracts.Reservations.ReservationConfirmed>()),
            _ => Of(message.Read<SaasRestaurant.Contracts.Reservations.ReservationCancelled>()),
        };
        var tenantId = TenantId.From(tenantGuid);
        var eventId = $"{message.Module}:{message.Id}";

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(tenantId);

        var count = message.Type switch
        {
            "OrderPlaced" => await OrderAsync(services, subjectId, NotificationTypes.OrderConfirmed, message, eventId, cancellationToken),
            "OrderReady" => await OrderAsync(services, subjectId, NotificationTypes.OrderReady, message, eventId, cancellationToken),
            "OrderCancelled" => await OrderAsync(services, subjectId, NotificationTypes.OrderCancelled, message, eventId, cancellationToken),
            "PaymentFailed" => await OrderAsync(services, subjectId, NotificationTypes.PaymentFailed, message, eventId, cancellationToken),
            "ReservationConfirmed" => await ReservationConfirmedAsync(services, subjectId, message, eventId, cancellationToken),
            _ => await ReservationCancelledAsync(services, subjectId, message, eventId, cancellationToken),
        };
        logger.LogInformation("Outbox message {Module}/{MessageId} ({Type}): {Count} notification change(s).", message.Module, message.Id, message.Type, count);
    }

    private static (Guid TenantId, Guid SubjectId) Of(SaasRestaurant.Contracts.Orders.OrderPlaced placed) => (placed.TenantId, placed.OrderId);

    private static (Guid TenantId, Guid SubjectId) Of(SaasRestaurant.Contracts.Orders.OrderReady ready) => (ready.TenantId, ready.OrderId);

    private static (Guid TenantId, Guid SubjectId) Of(SaasRestaurant.Contracts.Orders.OrderCancelled cancelled) => (cancelled.TenantId, cancelled.OrderId);

    private static (Guid TenantId, Guid SubjectId) Of(SaasRestaurant.Contracts.Payments.PaymentFailed failed) => (failed.TenantId, failed.OrderId);

    private static (Guid TenantId, Guid SubjectId) Of(SaasRestaurant.Contracts.Reservations.ReservationConfirmed confirmed) =>
        (confirmed.TenantId, confirmed.ReservationId);

    private static (Guid TenantId, Guid SubjectId) Of(SaasRestaurant.Contracts.Reservations.ReservationCancelled cancelled) =>
        (cancelled.TenantId, cancelled.ReservationId);

    private static async Task<int> OrderAsync(
        IServiceProvider services, Guid orderId, NotificationTypeDefinition type, OutboxEnvelope message, string eventId, CancellationToken cancellationToken)
    {
        var order = await services.GetRequiredService<IQueryHandler<GetOrderByIdQuery, OrderDto>>()
            .HandleAsync(new GetOrderByIdQuery(orderId), cancellationToken);
        if (order.CustomerId is not { } customerId)
        {
            return 0;
        }

        var variables = await CommonVariablesAsync(services, order.LocationId, cancellationToken);
        variables["order.number"] = order.Number;
        variables["order.total"] = string.Create(CultureInfo.InvariantCulture, $"{order.Total:0.##} {order.CurrencyCode}");
        return await TriggerAsync(
            services, message, eventId,
            [new NotificationTrigger(type.Key.Value, CustomerChannels, customerId, null, order.LocationId, variables, Anonymous: IsSelfService(order.Channel))],
            cancellationToken);
    }

    /// <summary>Commande passée par le client lui-même (site, QR code, application, borne) : SMS/WhatsApp restreints.</summary>
    internal static bool IsSelfService(Orders.Domain.OrderChannel channel) =>
        channel is Orders.Domain.OrderChannel.Web or Orders.Domain.OrderChannel.QrCode or Orders.Domain.OrderChannel.MobileApp
            or Orders.Domain.OrderChannel.Kiosk;

    private static async Task<int> ReservationConfirmedAsync(
        IServiceProvider services, Guid reservationId, OutboxEnvelope message, string eventId, CancellationToken cancellationToken)
    {
        var reservation = await services.GetRequiredService<IQueryHandler<GetReservationByIdQuery, ReservationDto>>()
            .HandleAsync(new GetReservationByIdQuery(reservationId), cancellationToken);
        if (reservation.CustomerId is not { } customerId)
        {
            return 0;
        }

        var variables = await CommonVariablesAsync(services, reservation.LocationId, cancellationToken);
        var location = await services.GetRequiredService<IQueryHandler<GetLocationByIdQuery, LocationDto>>()
            .HandleAsync(new GetLocationByIdQuery(reservation.LocationId), cancellationToken);
        AddReservationVariables(variables, reservation, location.TimeZoneId);

        var triggers = new List<NotificationTrigger>
        {
            new(NotificationTypes.ReservationConfirmed.Key.Value, CustomerChannels, customerId, null, reservation.LocationId, variables, Anonymous: ReservationIsAnonymous),
        };
        var reminderAt = reservation.StartAtUtc - ReminderBefore;
        if (reminderAt > services.GetRequiredService<IClock>().UtcNow.AddMinutes(5))
        {
            triggers.Add(new NotificationTrigger(
                NotificationTypes.ReservationReminder.Key.Value, CustomerChannels, customerId, null, reservation.LocationId, variables, reminderAt,
                ReminderKey(reservationId), ReservationIsAnonymous));
        }

        return await TriggerAsync(services, message, eventId, triggers, cancellationToken);
    }

    private static async Task<int> ReservationCancelledAsync(
        IServiceProvider services, Guid reservationId, OutboxEnvelope message, string eventId, CancellationToken cancellationToken)
    {
        // Le rappel encore programmé n'a plus lieu d'être.
        var cancelled = await services.GetRequiredService<ICommandHandler<CancelTriggeredNotificationsCommand, int>>()
            .HandleAsync(new CancelTriggeredNotificationsCommand(Consumer + ".cancel", eventId, message.Type, [ReminderKey(reservationId)]), cancellationToken);

        var reservation = await services.GetRequiredService<IQueryHandler<GetReservationByIdQuery, ReservationDto>>()
            .HandleAsync(new GetReservationByIdQuery(reservationId), cancellationToken);
        if (reservation.CustomerId is not { } customerId)
        {
            return cancelled;
        }

        var variables = await CommonVariablesAsync(services, reservation.LocationId, cancellationToken);
        var location = await services.GetRequiredService<IQueryHandler<GetLocationByIdQuery, LocationDto>>()
            .HandleAsync(new GetLocationByIdQuery(reservation.LocationId), cancellationToken);
        AddReservationVariables(variables, reservation, location.TimeZoneId);
        return cancelled + await TriggerAsync(
            services, message, eventId,
            [new NotificationTrigger(
                NotificationTypes.ReservationCancelled.Key.Value, CustomerChannels, customerId, null, reservation.LocationId, variables,
                Anonymous: ReservationIsAnonymous)],
            cancellationToken);
    }

    /// <summary>Le fuseau du restaurant sert UNIQUEMENT à afficher l'heure locale ; tout est calculé en UTC.</summary>
    private static void AddReservationVariables(Dictionary<string, string> variables, ReservationDto reservation, string timeZoneId)
    {
        DateTimeOffset local;
        try
        {
            local = TimeZoneInfo.ConvertTime(reservation.StartAtUtc, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            local = reservation.StartAtUtc;
        }

        variables["reservation.date"] = local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        variables["reservation.time"] = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        variables["reservation.guestCount"] = reservation.PartySize.ToString(CultureInfo.InvariantCulture);
    }

    private static string ReminderKey(Guid reservationId) => $"reservations:reminder:{reservationId:N}";

    /// <summary>Nom du restaurant et de l'établissement (rendus seulement si le template les utilise).</summary>
    private static async Task<Dictionary<string, string>> CommonVariablesAsync(IServiceProvider services, Guid locationId, CancellationToken cancellationToken)
    {
        var tenant = services.GetRequiredService<ITenantContext>().RequireTenant();
        var restaurant = await services.GetRequiredService<IQueryHandler<GetTenantByIdQuery, TenantDto>>()
            .HandleAsync(new GetTenantByIdQuery(tenant.Value), cancellationToken);
        var location = await services.GetRequiredService<IQueryHandler<GetLocationByIdQuery, LocationDto>>()
            .HandleAsync(new GetLocationByIdQuery(locationId), cancellationToken);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["restaurant.name"] = restaurant.Name,
            ["location.name"] = location.Name,
        };
    }

    private static Task<int> TriggerAsync(
        IServiceProvider services, OutboxEnvelope message, string eventId, IReadOnlyList<NotificationTrigger> triggers, CancellationToken cancellationToken) =>
        services.GetRequiredService<ICommandHandler<TriggerNotificationsCommand, int>>()
            .HandleAsync(new TriggerNotificationsCommand(Consumer, eventId, message.Type, triggers), cancellationToken);
}
