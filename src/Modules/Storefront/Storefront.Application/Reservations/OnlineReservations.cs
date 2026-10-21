using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using Storefront.Application.Abstractions;
using Storefront.Application.Checkout;
using Storefront.Application.Queries;
using Storefront.Domain;

namespace Storefront.Application.Reservations;

public static class OnlineReservationErrors
{
    // Même réponse pour un lien faux, d'un autre restaurant ou une réservation inconnue.
    public static readonly DomainError ReservationNotFound = new("storefront.reservation.not_found", "This reservation link is not valid.");

    public static readonly DomainError Disabled = new("storefront.reservation.disabled", "This location does not take reservations online.");

    public static readonly DomainError DateInvalid = new("storefront.reservation.date_invalid", "Choose a date within the booking window.");

    public static readonly DomainError KioskNotAllowed = new("storefront.reservation.kiosk_not_allowed", "A table cannot be booked from a kiosk.");

    public static readonly DomainError DepositNotDue = new("storefront.reservation.deposit_not_due", "No deposit is waiting for a payment on this reservation.");

    // Même réponse pour un lien faux, d'un autre restaurant ou une inscription inconnue.
    public static readonly DomainError WaitlistNotFound = new("storefront.waitlist.not_found", "This waiting list link is not valid.");
}

/// <summary>
/// Lien de gestion d'une réservation remis au client : restaurant et réservation signés en HMAC-SHA256 (même clé que le
/// suivi des commandes, autre usage), rien d'autre. Il ouvre la consultation et l'annulation de CETTE réservation.
/// </summary>
public sealed class ReservationLinks
{
    private const int IdLength = 16;
    private const int SignatureLength = 16;
    private static readonly byte[] Purpose = "storefront.reservation.v1"u8.ToArray();
    private static readonly byte[] WaitlistPurpose = "storefront.waitlist.v1"u8.ToArray();

    private readonly byte[] _key;

    public ReservationLinks(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32)
        {
            throw new InvalidOperationException("The reservation link key must be at least 32 bytes.");
        }

        _key = key;
    }

    public string Issue(TenantId tenantId, Guid reservationId) => Issue(tenantId, reservationId, Purpose);

    /// <summary>La réservation du lien, s'il est authentique et de ce restaurant ; null sinon.</summary>
    public Guid? Read(string? token, TenantId tenantId) => Read(token, tenantId, Purpose);

    /// <summary>Lien d'une inscription en liste d'attente (autre usage : il n'ouvre jamais une réservation).</summary>
    public string IssueWaitlist(TenantId tenantId, Guid entryId) => Issue(tenantId, entryId, WaitlistPurpose);

    public Guid? ReadWaitlist(string? token, TenantId tenantId) => Read(token, tenantId, WaitlistPurpose);

    private string Issue(TenantId tenantId, Guid id, byte[] purpose)
    {
        var token = new byte[IdLength * 2 + SignatureLength];
        tenantId.Value.TryWriteBytes(token.AsSpan(0, IdLength), bigEndian: true, out _);
        id.TryWriteBytes(token.AsSpan(IdLength, IdLength), bigEndian: true, out _);
        Sign(token.AsSpan(0, IdLength * 2), purpose).AsSpan(0, SignatureLength).CopyTo(token.AsSpan(IdLength * 2));
        return Base64Url.EncodeToString(token);
    }

    private Guid? Read(string? token, TenantId tenantId, byte[] purpose)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128)
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(token);
        }
        catch (FormatException)
        {
            return null;
        }

        if (bytes.Length != IdLength * 2 + SignatureLength
            || !CryptographicOperations.FixedTimeEquals(Sign(bytes.AsSpan(0, IdLength * 2), purpose).AsSpan(0, SignatureLength), bytes.AsSpan(IdLength * 2)))
        {
            return null;
        }

        var tenant = new Guid(bytes.AsSpan(0, IdLength), bigEndian: true);
        var reservation = new Guid(bytes.AsSpan(IdLength, IdLength), bigEndian: true);
        return tenant == tenantId.Value && reservation != Guid.Empty ? reservation : null;
    }

    private byte[] Sign(ReadOnlySpan<byte> ids, byte[] purpose)
    {
        var message = new byte[purpose.Length + ids.Length];
        purpose.CopyTo(message, 0);
        ids.CopyTo(message.AsSpan(purpose.Length));
        return HMACSHA256.HashData(_key, message);
    }
}

public sealed record BookingTimeDto(DateTimeOffset StartAtUtc, bool IsAvailable);

/// <param name="AcceptsWaitlist">Un créneau complet propose l'inscription en liste d'attente.</param>
/// <param name="Deposit">Règle d'acompte de l'établissement, annoncée avant de réserver ; null sans acompte.</param>
public sealed record BookingTimesDto(
    Guid LocationId, string TimeZoneId, DateOnly Date, int DurationMinutes, int MaxPartySize, int CancellationCutoffMinutes, IReadOnlyList<BookingTimeDto> Times,
    bool AcceptsWaitlist = false, StorefrontDepositRule? Deposit = null);

/// <summary>
/// Les heures proposées un jour donné (heure locale de l'établissement), toutes les 30 minutes pendant les horaires de la
/// semaine, le repas entier devant tenir avant la fermeture ; chacune dite disponible ou non pour ce groupe.
/// </summary>
public sealed record GetBookingTimesQuery(Guid LocationId, DateOnly Date, int PartySize) : IQuery<BookingTimesDto>;

public sealed class GetBookingTimesHandler(StorefrontWorkspace workspace, IStorefrontClient client, IStorefrontReservations reservations, IClock clock)
    : IQueryHandler<GetBookingTimesQuery, BookingTimesDto>
{
    public const int StepMinutes = 30;

    public async Task<BookingTimesDto> HandleAsync(GetBookingTimesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var app = await StorefrontReader.AppAsync(workspace, client, cancellationToken);
        DomainException.ThrowIf(app.Kind == SalesAppKind.Kiosk, OnlineReservationErrors.KioskNotAllowed);
        var tenant = workspace.CurrentTenant;
        var now = clock.UtcNow;
        var location = (await workspace.Locations.ListAsync(tenant, now, cancellationToken))
            .SingleOrDefault(item => item.LocationId.Value == query.LocationId && app.Serves(item.LocationId))
            ?? throw new NotFoundException(StorefrontApplicationErrors.LocationNotFound);
        var rules = await reservations.RulesAsync(tenant, location.LocationId, cancellationToken);
        DomainException.ThrowIf(!rules.AcceptsOnlineBookings, OnlineReservationErrors.Disabled);

        var zone = Zone(location.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).Date);
        DomainException.ThrowIf(query.Date < today || query.Date > today.AddDays(rules.MaxAdvanceDays), OnlineReservationErrors.DateInvalid);

        var duration = TimeSpan.FromMinutes(rules.DurationMinutes);
        var starts = new SortedSet<DateTimeOffset>();
        foreach (var range in location.WeeklyHours.Where(range => range.Day == query.Date.DayOfWeek))
        {
            if (!TimeOnly.TryParse(range.Start, CultureInfo.InvariantCulture, out var start)
                || !TimeOnly.TryParse(range.End, CultureInfo.InvariantCulture, out var end))
            {
                continue;
            }

            var opens = Utc(query.Date.ToDateTime(start), zone);
            var closes = Utc((range.EndsNextDay ? query.Date.AddDays(1) : query.Date).ToDateTime(end), zone);
            for (var slot = opens; slot + duration <= closes; slot = slot.AddMinutes(StepMinutes))
            {
                if (slot >= now.AddMinutes(rules.MinLeadTimeMinutes))
                {
                    starts.Add(slot);
                }
            }
        }

        var checkedTimes = starts.Count == 0
            ? []
            : await reservations.CheckAsync(tenant, location.LocationId, query.PartySize, starts.ToList(), cancellationToken);
        return new BookingTimesDto(
            location.LocationId.Value,
            location.TimeZoneId,
            query.Date,
            rules.DurationMinutes,
            rules.MaxPartySize,
            rules.CancellationCutoffMinutes,
            checkedTimes.Select(time => new BookingTimeDto(time.StartAtUtc, time.IsAvailable)).ToList(),
            rules.AcceptsWaitlist,
            rules.Deposit);
    }

    private static DateTimeOffset Utc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            unspecified = unspecified.AddHours(1);
        }

        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    private static TimeZoneInfo Zone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}

/// <param name="Token">Lien de gestion à garder : consultation, paiement de l'acompte et annulation.</param>
/// <param name="Deposit">L'acompte à payer pour confirmer la réservation ; null sans acompte (réservation confirmée).</param>
public sealed record BookedTableDto(string Token, DateTimeOffset StartAtUtc, int PartySize, GuestDepositDto? Deposit = null);

/// <param name="Status">AwaitingPayment, Paid, Applied, RefundRequested, Refunded, Forfeited ou Waived.</param>
/// <param name="CanPay">Un paiement en ligne est attendu (avant <paramref name="DueBeforeUtc"/>).</param>
/// <param name="PaymentOptions">Moyens en ligne proposés pour ce montant (vide si rien à payer).</param>
public sealed record GuestDepositDto(
    decimal Amount, string CurrencyCode, string Status, DateTimeOffset? DueBeforeUtc, bool CanPay, bool HasPendingPayment,
    IReadOnlyList<OnlinePaymentOption> PaymentOptions);

internal static class GuestDeposits
{
    public static async Task<GuestDepositDto?> DescribeAsync(
        StorefrontWorkspace workspace, IStorefrontPayments payments, StorefrontReservation reservation, IClock clock, CancellationToken cancellationToken)
    {
        if (reservation.Deposit is not { } deposit)
        {
            return null;
        }

        var canPay = deposit.Status == "AwaitingPayment" && reservation.Status == "Pending" && deposit.DueBeforeUtc > clock.UtcNow;
        var options = canPay
            ? await payments.OptionsAsync(workspace.CurrentTenant, reservation.LocationId, deposit.CurrencyCode, deposit.Amount, cancellationToken)
            : [];
        return new GuestDepositDto(deposit.Amount, deposit.CurrencyCode, deposit.Status, deposit.DueBeforeUtc, canPay, deposit.HasPendingPayment, options);
    }
}

/// <summary>
/// Réservation d'une table par le client (téléphone obligatoire pour la confirmation). Un client connecté y est rattaché.
/// La réservation est confirmée tout de suite.
/// </summary>
public sealed record BookTableCommand(
    Guid LocationId, DateTimeOffset StartAt, int PartySize, string? Name, string? Phone, string? Email = null, string? Notes = null) : ICommand<BookedTableDto>
{
    public override string ToString() => $"BookTableCommand ({LocationId}, {StartAt:O}, {PartySize}, ***)";
}

public sealed class BookTableHandler(
    StorefrontWorkspace workspace, IStorefrontClient client, IStorefrontReservations reservations, IStorefrontPayments payments, ReservationLinks links, IClock clock)
    : ICommandHandler<BookTableCommand, BookedTableDto>
{
    public async Task<BookedTableDto> HandleAsync(BookTableCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var app = await StorefrontReader.AppAsync(workspace, client, cancellationToken);
        DomainException.ThrowIf(app.Kind == SalesAppKind.Kiosk, OnlineReservationErrors.KioskNotAllowed);
        var tenant = workspace.CurrentTenant;
        if (command.LocationId == Guid.Empty || !app.Serves(LocationId.From(command.LocationId)))
        {
            throw new NotFoundException(StorefrontApplicationErrors.LocationNotFound);
        }
        var id = await reservations.BookAsync(
            tenant, LocationId.From(command.LocationId), command.StartAt, command.PartySize, command.Name, command.Phone, command.Email, command.Notes,
            client.CustomerId, cancellationToken);
        var booked = await reservations.FindAsync(tenant, id, cancellationToken);
        var deposit = booked is null ? null : await GuestDeposits.DescribeAsync(workspace, payments, booked, clock, cancellationToken);
        return new BookedTableDto(links.Issue(tenant, id), command.StartAt.ToUniversalTime(), command.PartySize, deposit);
    }
}

public sealed record GuestBookingDto(
    string Status, Guid LocationId, string LocationName, DateTimeOffset StartAtUtc, int DurationMinutes, int PartySize, string GuestName,
    bool CanCancel, DateTimeOffset? CancelBeforeUtc, GuestDepositDto? Deposit = null);

public sealed record GetGuestBookingQuery(string? Token) : IQuery<GuestBookingDto>
{
    public override string ToString() => "GetGuestBookingQuery (***)";
}

public sealed class GetGuestBookingHandler(
    StorefrontWorkspace workspace, IStorefrontReservations reservations, IStorefrontPayments payments, ReservationLinks links, IClock clock)
    : IQueryHandler<GetGuestBookingQuery, GuestBookingDto>
{
    public async Task<GuestBookingDto> HandleAsync(GetGuestBookingQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var reservation = links.Read(query.Token, tenant) is { } id ? await reservations.FindAsync(tenant, id, cancellationToken) : null;
        if (reservation is null)
        {
            throw new NotFoundException(OnlineReservationErrors.ReservationNotFound);
        }

        var location = (await workspace.Locations.ListAsync(tenant, clock.UtcNow, cancellationToken)).SingleOrDefault(item => item.LocationId == reservation.LocationId);
        return new GuestBookingDto(
            reservation.Status, reservation.LocationId.Value, location?.Name ?? string.Empty, reservation.StartAtUtc, reservation.DurationMinutes,
            reservation.PartySize, reservation.GuestName, reservation.CanCancel, reservation.CancelBeforeUtc,
            await GuestDeposits.DescribeAsync(workspace, payments, reservation, clock, cancellationToken));
    }
}

public sealed record CancelGuestBookingCommand(string? Token) : ICommand
{
    public override string ToString() => "CancelGuestBookingCommand (***)";
}

public sealed class CancelGuestBookingHandler(StorefrontWorkspace workspace, IStorefrontReservations reservations, ReservationLinks links)
    : ICommandHandler<CancelGuestBookingCommand>
{
    public async Task HandleAsync(CancelGuestBookingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        var id = links.Read(command.Token, tenant) ?? throw new NotFoundException(OnlineReservationErrors.ReservationNotFound);
        await reservations.CancelAsync(tenant, id, cancellationToken);
    }
}

/// <summary>
/// Le client paie l'acompte de sa réservation (lien de gestion). <paramref name="ReturnUrl"/> : page de retour déclarée
/// pour l'application, sinon refusée.
/// </summary>
public sealed record PayGuestDepositCommand(string? Token, string? Provider, string? Method = null, string? PayerPhone = null, string? ReturnUrl = null)
    : ICommand<GuestPaymentStartedDto>
{
    public override string ToString() => $"PayGuestDepositCommand (***, {Provider})";
}

public sealed class PayGuestDepositHandler(
    StorefrontWorkspace workspace, IStorefrontClient client, IStorefrontReservations reservations, ReservationLinks links, IClock clock)
    : ICommandHandler<PayGuestDepositCommand, GuestPaymentStartedDto>
{
    public async Task<GuestPaymentStartedDto> HandleAsync(PayGuestDepositCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var app = await StorefrontReader.AppAsync(workspace, client, cancellationToken);
        var tenant = workspace.CurrentTenant;
        var reservation = links.Read(command.Token, tenant) is { } id ? await reservations.FindAsync(tenant, id, cancellationToken) : null;
        if (reservation is null)
        {
            throw new NotFoundException(OnlineReservationErrors.ReservationNotFound);
        }

        DomainException.ThrowIf(
            reservation.Deposit is not { Status: "AwaitingPayment" } deposit || reservation.Status != "Pending" || deposit.DueBeforeUtc <= clock.UtcNow,
            OnlineReservationErrors.DepositNotDue);
        Uri? returnUrl = null;
        if (!string.IsNullOrWhiteSpace(command.ReturnUrl))
        {
            DomainException.ThrowIf(
                !app.AllowsReturnUrl(command.ReturnUrl) || !Uri.TryCreate(command.ReturnUrl, UriKind.Absolute, out returnUrl),
                CheckoutErrors.ReturnUrlNotAllowed);
        }

        var started = await reservations.StartDepositAsync(
            tenant, reservation.Id, new OnlinePaymentRequest(command.Provider ?? string.Empty, command.Method, command.PayerPhone, returnUrl), cancellationToken);
        return new GuestPaymentStartedDto(started.PaymentId, started.CheckoutUrl);
    }
}

// ---- Liste d'attente : un créneau complet, le client s'inscrit et est prévenu quand une table se libère.

/// <param name="Token">Lien de l'inscription : suivi, acceptation de l'offre, retrait.</param>
public sealed record JoinedWaitlistDto(string Token, DateTimeOffset DesiredStartUtc, int PartySize);

/// <summary>Inscription en liste d'attente (téléphone obligatoire : l'offre part par WhatsApp ou SMS).</summary>
public sealed record JoinWaitlistOnlineCommand(
    Guid LocationId, DateTimeOffset DesiredStart, int PartySize, int FlexibilityMinutes, string? Name, string? Phone, string? Email = null)
    : ICommand<JoinedWaitlistDto>
{
    public override string ToString() => $"JoinWaitlistOnlineCommand ({LocationId}, {DesiredStart:O}, {PartySize}, ***)";
}

public sealed class JoinWaitlistOnlineHandler(StorefrontWorkspace workspace, IStorefrontClient client, IStorefrontReservations reservations, ReservationLinks links)
    : ICommandHandler<JoinWaitlistOnlineCommand, JoinedWaitlistDto>
{
    public async Task<JoinedWaitlistDto> HandleAsync(JoinWaitlistOnlineCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var app = await StorefrontReader.AppAsync(workspace, client, cancellationToken);
        DomainException.ThrowIf(app.Kind == SalesAppKind.Kiosk, OnlineReservationErrors.KioskNotAllowed);
        var tenant = workspace.CurrentTenant;
        if (command.LocationId == Guid.Empty || !app.Serves(LocationId.From(command.LocationId)))
        {
            throw new NotFoundException(StorefrontApplicationErrors.LocationNotFound);
        }

        var id = await reservations.JoinWaitlistAsync(
            tenant, LocationId.From(command.LocationId), command.DesiredStart, command.PartySize, command.FlexibilityMinutes, command.Name, command.Phone,
            command.Email, client.CustomerId, cancellationToken);
        return new JoinedWaitlistDto(links.IssueWaitlist(tenant, id), command.DesiredStart.ToUniversalTime(), command.PartySize);
    }
}

/// <param name="Status">Waiting, Offered, Booked, Expired ou Cancelled.</param>
/// <param name="ReservationToken">Une fois la place acceptée : le lien de gestion de la réservation.</param>
public sealed record GuestWaitlistDto(
    string Status, Guid LocationId, string LocationName, DateTimeOffset DesiredStartUtc, int FlexibilityMinutes, int PartySize, string GuestName,
    DateTimeOffset? OfferedStartUtc, DateTimeOffset? OfferExpiresAtUtc, string? ReservationToken);

public sealed record GetGuestWaitlistQuery(string? Token) : IQuery<GuestWaitlistDto>
{
    public override string ToString() => "GetGuestWaitlistQuery (***)";
}

public sealed class GetGuestWaitlistHandler(StorefrontWorkspace workspace, IStorefrontReservations reservations, ReservationLinks links, IClock clock)
    : IQueryHandler<GetGuestWaitlistQuery, GuestWaitlistDto>
{
    public async Task<GuestWaitlistDto> HandleAsync(GetGuestWaitlistQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var entry = await GuestWaitlist.LoadAsync(reservations, links, tenant, query.Token, cancellationToken);
        var location = (await workspace.Locations.ListAsync(tenant, clock.UtcNow, cancellationToken)).SingleOrDefault(item => item.LocationId == entry.LocationId);
        return new GuestWaitlistDto(
            entry.Status, entry.LocationId.Value, location?.Name ?? string.Empty, entry.DesiredStartUtc, entry.FlexibilityMinutes, entry.PartySize,
            entry.GuestName, entry.OfferedStartUtc, entry.OfferExpiresAtUtc,
            entry.ReservationId is { } reservation ? links.Issue(tenant, reservation) : null);
    }
}

/// <summary>Le client accepte la table proposée : sa réservation est créée (acompte à payer si l'établissement en demande).</summary>
public sealed record AcceptGuestWaitlistOfferCommand(string? Token) : ICommand<BookedTableDto>
{
    public override string ToString() => "AcceptGuestWaitlistOfferCommand (***)";
}

public sealed class AcceptGuestWaitlistOfferHandler(
    StorefrontWorkspace workspace, IStorefrontReservations reservations, IStorefrontPayments payments, ReservationLinks links, IClock clock)
    : ICommandHandler<AcceptGuestWaitlistOfferCommand, BookedTableDto>
{
    public async Task<BookedTableDto> HandleAsync(AcceptGuestWaitlistOfferCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        var entry = await GuestWaitlist.LoadAsync(reservations, links, tenant, command.Token, cancellationToken);
        var id = await reservations.AcceptWaitlistOfferAsync(tenant, entry.Id, cancellationToken);
        var booked = await reservations.FindAsync(tenant, id, cancellationToken);
        var deposit = booked is null ? null : await GuestDeposits.DescribeAsync(workspace, payments, booked, clock, cancellationToken);
        return new BookedTableDto(links.Issue(tenant, id), booked?.StartAtUtc ?? entry.OfferedStartUtc ?? entry.DesiredStartUtc, entry.PartySize, deposit);
    }
}

/// <summary>Le client retire son inscription (ou refuse la table proposée).</summary>
public sealed record CancelGuestWaitlistCommand(string? Token) : ICommand
{
    public override string ToString() => "CancelGuestWaitlistCommand (***)";
}

public sealed class CancelGuestWaitlistHandler(StorefrontWorkspace workspace, IStorefrontReservations reservations, ReservationLinks links)
    : ICommandHandler<CancelGuestWaitlistCommand>
{
    public async Task HandleAsync(CancelGuestWaitlistCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        var entry = await GuestWaitlist.LoadAsync(reservations, links, tenant, command.Token, cancellationToken);
        await reservations.CancelWaitlistAsync(tenant, entry.Id, cancellationToken);
    }
}

internal static class GuestWaitlist
{
    public static async Task<StorefrontWaitlistEntry> LoadAsync(
        IStorefrontReservations reservations, ReservationLinks links, TenantId tenant, string? token, CancellationToken cancellationToken) =>
        (links.ReadWaitlist(token, tenant) is { } id ? await reservations.FindWaitlistAsync(tenant, id, cancellationToken) : null)
        ?? throw new NotFoundException(OnlineReservationErrors.WaitlistNotFound);
}

// ---- Liens envoyés par message (console, notifications) : la page du site du restaurant.

/// <param name="Url">Null quand le restaurant n'a pas de site actif.</param>
public sealed record GuestLinkDto(string? Url);

/// <summary>Lien de gestion d'une réservation (paiement de l'acompte compris), pour l'envoyer au client.</summary>
public sealed record GetReservationLinkQuery(Guid ReservationId) : IQuery<GuestLinkDto>;

public sealed class GetReservationLinkHandler(StorefrontWorkspace workspace, IStorefrontReservations reservations, ReservationLinks links)
    : IQueryHandler<GetReservationLinkQuery, GuestLinkDto>
{
    public const string PagePath = "/reservation";

    public async Task<GuestLinkDto> HandleAsync(GetReservationLinkQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var reservation = (query.ReservationId == Guid.Empty ? null : await reservations.FindAsync(tenant, query.ReservationId, cancellationToken))
            ?? throw new NotFoundException(OnlineReservationErrors.ReservationNotFound);
        var site = await GuestSite.OriginAsync(workspace, cancellationToken);
        return new GuestLinkDto(site is null ? null : $"{site}{PagePath}?jeton={Uri.EscapeDataString(links.Issue(tenant, reservation.Id))}");
    }
}

/// <summary>Lien d'une inscription en liste d'attente (offre à accepter), pour l'envoyer au client.</summary>
public sealed record GetWaitlistLinkQuery(Guid EntryId) : IQuery<GuestLinkDto>;

public sealed class GetWaitlistLinkHandler(StorefrontWorkspace workspace, IStorefrontReservations reservations, ReservationLinks links)
    : IQueryHandler<GetWaitlistLinkQuery, GuestLinkDto>
{
    public const string PagePath = "/reservation/attente";

    public async Task<GuestLinkDto> HandleAsync(GetWaitlistLinkQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var entry = (query.EntryId == Guid.Empty ? null : await reservations.FindWaitlistAsync(tenant, query.EntryId, cancellationToken))
            ?? throw new NotFoundException(OnlineReservationErrors.WaitlistNotFound);
        var site = await GuestSite.OriginAsync(workspace, cancellationToken);
        return new GuestLinkDto(site is null ? null : $"{site}{PagePath}?jeton={Uri.EscapeDataString(links.IssueWaitlist(tenant, entry.Id))}");
    }
}

internal static class GuestSite
{
    /// <summary>L'origine du site actif du restaurant (https d'abord) ; null sans site.</summary>
    public static async Task<string?> OriginAsync(StorefrontWorkspace workspace, CancellationToken cancellationToken) =>
        (await workspace.Apps.ListAsync(workspace.CurrentTenant, cancellationToken))
            .Where(app => app.Kind == SalesAppKind.Web && app.Status == SalesAppStatus.Active)
            .SelectMany(app => app.AllowedOrigins)
            .OrderBy(origin => origin.StartsWith("https://", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(origin => origin, StringComparer.Ordinal)
            .FirstOrDefault();
}
