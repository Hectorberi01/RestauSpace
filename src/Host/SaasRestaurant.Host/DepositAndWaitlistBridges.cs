using System.Globalization;
using Floor.Application.Commands;
using Locations.Application.Queries;
using Notifications.Application.Commands;
using Notifications.Domain;
using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Reservations.Application.Commands;
using Reservations.Application.Queries;
using SaasRestaurant.BuildingBlocks.Application;
using Storefront.Application.Reservations;
using Tenants.Application.Queries;
using PaymentsContracts = SaasRestaurant.Contracts.Payments;
using ReservationsContracts = SaasRestaurant.Contracts.Reservations;

namespace SaasRestaurant.Host;

// Acompte de réservation et liste d'attente : Reservations fixe le montant et l'état, Payments détient l'argent,
// Notifications prévient le client. Seul l'hôte les relie, par les ports et l'outbox.

/// <summary>Payments -> Reservations : l'acompte d'une réservation du tenant du contexte (fail-closed sinon).</summary>
internal sealed class PaymentReservationsFromReservations(
    ITenantContext tenantContext, IQueryHandler<GetReservationDepositQuery, ReservationDepositDue?> deposits) : IPaymentReservations
{
    public async Task<PaymentDeposit?> FindDepositAsync(TenantId tenantId, Guid reservationId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        var found = await deposits.HandleAsync(new GetReservationDepositQuery(reservationId), cancellationToken);
        return found is null
            ? null
            : new PaymentDeposit(found.ReservationId, LocationId.From(found.LocationId), Money.Of(found.Amount, Currency.FromCode(found.CurrencyCode)), found.AcceptsPayment);
    }
}

/// <summary>
/// Payments <-> Reservations, par l'outbox, pour le tenant du message : ce que détiennent les paiements d'un acompte
/// (payé, remboursé), sa déduction d'une addition, et le remboursement demandé par la réservation (annulation à temps,
/// paiement arrivé trop tard, geste de l'équipe).
/// </summary>
internal sealed class DepositFlow(IServiceScopeFactory scopes) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        switch (message.Module, message.Type)
        {
            case ("payments", "DepositSettlementChanged"):
            {
                var changed = message.Read<PaymentsContracts.DepositSettlementChanged>();
                await RunAsync(changed.TenantId, services => services.GetRequiredService<ICommandHandler<RecordDepositSettlementCommand>>().HandleAsync(
                    new RecordDepositSettlementCommand(changed.ReservationId, changed.SettledAmount.Amount, changed.SettledAmount.Currency), cancellationToken));
                break;
            }

            case ("payments", "DepositApplied"):
            {
                var applied = message.Read<PaymentsContracts.DepositApplied>();
                await RunAsync(applied.TenantId, services => services.GetRequiredService<ICommandHandler<MarkDepositAppliedCommand>>().HandleAsync(
                    new MarkDepositAppliedCommand(applied.ReservationId), cancellationToken));
                break;
            }

            case ("reservations", "ReservationDepositRefundRequested"):
            {
                var requested = message.Read<ReservationsContracts.ReservationDepositRefundRequested>();
                await RunAsync(requested.TenantId, services => services.GetRequiredService<ICommandHandler<RefundDepositPaymentsCommand>>().HandleAsync(
                    new RefundDepositPaymentsCommand(requested.ReservationId, null), cancellationToken));
                break;
            }
        }
    }

    private async Task RunAsync(Guid tenantId, Func<IServiceProvider, Task> action)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(tenantId));
        await action(scope.ServiceProvider);
    }
}

/// <summary>
/// Reservations, par l'outbox : des places se sont peut-être libérées (annulation, no-show, groupe réduit, réservation
/// déplacée, offre passée ou retirée) ; elles sont proposées à la liste d'attente de l'établissement.
/// </summary>
internal sealed class WaitlistCapacityFlow(IServiceScopeFactory scopes) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Module != "reservations")
        {
            return;
        }

        (Guid TenantId, Guid LocationId)? freed = message.Type switch
        {
            "ReservationCancelled" => Of(message.Read<ReservationsContracts.ReservationCancelled>()),
            "ReservationNoShow" => Of(message.Read<ReservationsContracts.ReservationNoShow>()),
            "ReservationRescheduled" => Of(message.Read<ReservationsContracts.ReservationRescheduled>()),
            "ReservationPartySizeChanged" => message.Read<ReservationsContracts.ReservationPartySizeChanged>() is var changed && changed.PartySize < changed.PreviousPartySize
                ? (changed.TenantId, changed.LocationId)
                : null,
            "WaitlistOfferReleased" => Of(message.Read<ReservationsContracts.WaitlistOfferReleased>()),
            "WaitlistEntryClosed" => Of(message.Read<ReservationsContracts.WaitlistEntryClosed>()),
            _ => null,
        };
        if (freed is not { } location)
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(location.TenantId));
        try
        {
            await scope.ServiceProvider.GetRequiredService<ICommandHandler<OfferFreedCapacityCommand, int>>()
                .HandleAsync(new OfferFreedCapacityCommand(location.LocationId), cancellationToken);
        }
        catch (NotFoundException)
        {
            // Établissement supprimé entre-temps : rien à proposer.
        }
    }

    private static (Guid, Guid) Of(ReservationsContracts.ReservationCancelled e) => (e.TenantId, e.LocationId);

    private static (Guid, Guid) Of(ReservationsContracts.ReservationNoShow e) => (e.TenantId, e.LocationId);

    private static (Guid, Guid) Of(ReservationsContracts.ReservationRescheduled e) => (e.TenantId, e.LocationId);

    private static (Guid, Guid) Of(ReservationsContracts.WaitlistOfferReleased e) => (e.TenantId, e.LocationId);

    private static (Guid, Guid) Of(ReservationsContracts.WaitlistEntryClosed e) => (e.TenantId, e.LocationId);
}

/// <summary>
/// Reservations et Floor -> Notifications, par l'outbox : la table proposée à la liste d'attente (<c>waitlist.offer</c>),
/// l'acompte à payer (<c>reservation.deposit_request</c>) et la table prête pour un groupe de la file à la porte
/// (<c>waitlist.table_ready</c>), par le compte WhatsApp du restaurant, sinon par SMS, au
/// numéro donné. Le lien mène à la page du site du restaurant ; sans site, rien ne part (l'équipe le voit dans la
/// console). Un événement rejoué n'envoie rien de plus (clé d'idempotence). Ni le numéro ni le nom ne sont journalisés.
/// </summary>
internal sealed class ReservationMessagesFlow(IServiceScopeFactory scopes, ILogger<ReservationMessagesFlow> logger) : IOutboxConsumer
{
    private static readonly NotificationChannel[] Channels = [NotificationChannel.WhatsApp, NotificationChannel.Sms];

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not (
            ("reservations", "WaitlistOfferMade") or ("reservations", "ReservationDepositRequested") or ("floor", "WaitingPartyNotified")))
        {
            return;
        }

        var tenantId = message.Type switch
        {
            "WaitlistOfferMade" => message.Read<ReservationsContracts.WaitlistOfferMade>().TenantId,
            "WaitingPartyNotified" => message.Read<SaasRestaurant.Contracts.Floor.WaitingPartyNotified>().TenantId,
            _ => message.Read<ReservationsContracts.ReservationDepositRequested>().TenantId,
        };
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(tenantId));
        try
        {
            var sent = message.Type switch
            {
                "WaitlistOfferMade" => await OfferAsync(services, message, cancellationToken),
                "WaitingPartyNotified" => await TableReadyAsync(services, message, cancellationToken),
                _ => await DepositAsync(services, message, cancellationToken),
            };
            logger.LogInformation("Outbox message {Module}/{MessageId} ({Type}): message {Outcome}.", message.Module, message.Id, message.Type, sent ? "sent" : "not sent");
        }
        catch (NotFoundException)
        {
            // Inscription ou réservation disparue, ou pas de site : rien à envoyer.
        }
    }

    private static async Task<bool> OfferAsync(IServiceProvider services, OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var offer = message.Read<ReservationsContracts.WaitlistOfferMade>();
        var link = await services.GetRequiredService<IQueryHandler<GetWaitlistLinkQuery, GuestLinkDto>>()
            .HandleAsync(new GetWaitlistLinkQuery(offer.EntryId), cancellationToken);
        var entry = (await services.GetRequiredService<IQueryHandler<ListWaitlistQuery, IReadOnlyList<WaitlistEntryDto>>>()
                .HandleAsync(new ListWaitlistQuery(offer.StartAtUtc.AddHours(-4), offer.StartAtUtc.AddHours(4), offer.LocationId), cancellationToken))
            .SingleOrDefault(item => item.Id == offer.EntryId);
        if (link.Url is null || entry?.GuestPhone is null)
        {
            return false;
        }

        var (variables, zone) = await VariablesAsync(services, offer.LocationId, cancellationToken);
        var start = Local(offer.StartAtUtc, zone);
        variables["reservation.date"] = start.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        variables["reservation.time"] = start.ToString("HH:mm", CultureInfo.InvariantCulture);
        variables["reservation.guestCount"] = entry.PartySize.ToString(CultureInfo.InvariantCulture);
        variables["offer.url"] = link.Url;
        variables["offer.expiresTime"] = Local(offer.ExpiresAtUtc, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
        return await SendAsync(services, NotificationTypes.WaitlistOffer, entry.GuestPhone, offer.LocationId, variables, $"waitlist-offer:{message.Id}", cancellationToken);
    }

    private static async Task<bool> DepositAsync(IServiceProvider services, OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var requested = message.Read<ReservationsContracts.ReservationDepositRequested>();
        var reservation = await services.GetRequiredService<IQueryHandler<GetReservationByIdQuery, ReservationDto>>()
            .HandleAsync(new GetReservationByIdQuery(requested.ReservationId), cancellationToken);
        if (reservation.GuestPhone is null)
        {
            return false;
        }

        var link = await services.GetRequiredService<IQueryHandler<GetReservationLinkQuery, GuestLinkDto>>()
            .HandleAsync(new GetReservationLinkQuery(requested.ReservationId), cancellationToken);
        if (link.Url is null)
        {
            return false;
        }

        var (variables, zone) = await VariablesAsync(services, requested.LocationId, cancellationToken);
        var start = Local(reservation.StartAtUtc, zone);
        variables["reservation.date"] = start.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        variables["reservation.time"] = start.ToString("HH:mm", CultureInfo.InvariantCulture);
        variables["reservation.guestCount"] = reservation.PartySize.ToString(CultureInfo.InvariantCulture);
        variables["deposit.amount"] = string.Create(CultureInfo.InvariantCulture, $"{requested.Amount.Amount:0.##} {requested.Amount.Currency}");
        variables["deposit.dueTime"] = Local(requested.DueBeforeUtc, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
        variables["reservation.url"] = link.Url;
        return await SendAsync(services, NotificationTypes.ReservationDepositRequest, reservation.GuestPhone, requested.LocationId, variables, $"deposit-request:{message.Id}", cancellationToken);
    }

    private static async Task<bool> TableReadyAsync(IServiceProvider services, OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var notified = message.Read<SaasRestaurant.Contracts.Floor.WaitingPartyNotified>();
        var party = await services.GetRequiredService<IQueryHandler<GetWaitingPartyContactQuery, WaitingPartyContactDto?>>()
            .HandleAsync(new GetWaitingPartyContactQuery(notified.PartyId), cancellationToken);
        if (party is null)
        {
            return false;
        }

        var (variables, _) = await VariablesAsync(services, notified.LocationId, cancellationToken);
        variables["customer.firstName"] = party.Name;
        return await SendAsync(services, NotificationTypes.WaitlistTableReady, party.Phone, notified.LocationId, variables, $"table-ready:{message.Id}", cancellationToken);
    }

    /// <summary>WhatsApp d'abord, SMS si WhatsApp n'a rien envoyé (pas de compte, pas de modèle validé...).</summary>
    private static async Task<bool> SendAsync(
        IServiceProvider services, NotificationTypeDefinition type, string phone, Guid locationId, Dictionary<string, string> variables, string key,
        CancellationToken cancellationToken)
    {
        var send = services.GetRequiredService<ICommandHandler<SendNotificationCommand, SendNotificationResult>>();
        foreach (var channel in Channels)
        {
            var result = await send.HandleAsync(
                new SendNotificationCommand(type.Key.Value, channel, null, null, phone, locationId, null, variables, null, $"{key}:{channel}"),
                cancellationToken);
            if (result.NotificationIds.Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<(Dictionary<string, string> Variables, TimeZoneInfo Zone)> VariablesAsync(
        IServiceProvider services, Guid locationId, CancellationToken cancellationToken)
    {
        var tenant = services.GetRequiredService<ITenantContext>().RequireTenant();
        var restaurant = await services.GetRequiredService<IQueryHandler<GetTenantByIdQuery, TenantDto>>()
            .HandleAsync(new GetTenantByIdQuery(tenant.Value), cancellationToken);
        var location = await services.GetRequiredService<IQueryHandler<GetLocationByIdQuery, LocationDto>>()
            .HandleAsync(new GetLocationByIdQuery(locationId), cancellationToken);
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(location.TimeZoneId);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
        }

        return (new Dictionary<string, string>(StringComparer.Ordinal) { ["restaurant.name"] = restaurant.Name, ["location.name"] = location.Name }, zone);
    }

    private static DateTimeOffset Local(DateTimeOffset utc, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(utc, zone);
}
