using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Application;

// Caisse d'un établissement : session ouverte avec un fond, mouvements d'espèces, clôture par comptage (ticket Z), et
// journal des encaissements sur une période. Le tenant vient toujours du contexte ; l'établissement est vérifié dans ce
// tenant. Seuls les moyens remis en main propre se comptent (espèces, terminal, mobile money reçu, titres) ; les autres figurent pour
// information.

/// <summary>Sérialise l'ouverture des sessions d'un même établissement jusqu'à la sauvegarde (numéro, session unique).</summary>
public interface ICashSessionLock
{
    Task LockAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);
}

public static class CashSessionsErrors
{
    public static readonly DomainError SessionNotFound = new("payments.cash_session.not_found", "The cash session does not exist.");
    public static readonly DomainError AlreadyOpen = new("payments.cash_session.already_open", "A cash session is already open for this location: close it first.");
    public static readonly DomainError CurrencyUnknown = new("payments.cash_session.currency_unknown", "The restaurant currency is unknown.");
    public static readonly DomainError PeriodInvalid = new("payments.journal.period_invalid", "The period must end after it starts and cover 93 days at most.");
    public static readonly DomainError LimitInvalid = new("payments.journal.limit_invalid", "The limit must be between 1 and 1,000.");
}

/// <summary>Services communs aux cas d'usage de la caisse.</summary>
public sealed class CashDesk(
    PaymentsWorkspace workspace,
    ICashSessionRepository sessions,
    IPaymentJournal journal,
    IPaymentLocations locations,
    ICashier cashier,
    IClock clock)
{
    public const int MaxPeriodDays = 93;

    public PaymentsWorkspace Workspace => workspace;

    public ICashSessionRepository Sessions => sessions;

    public IPaymentJournal Journal => journal;

    public IClock Clock => clock;

    public Guid? CurrentUser => cashier.CurrentUserId;

    public async Task<LocationId> RequireLocationAsync(TenantId tenantId, Guid locationId, CancellationToken cancellationToken)
    {
        var id = locationId == Guid.Empty ? (LocationId?)null : LocationId.From(locationId);
        return id is { } found && await locations.FindAsync(tenantId, found, cancellationToken) is not null
            ? found
            : throw new NotFoundException(PaymentsApplicationErrors.LocationNotFound);
    }

    public async Task<CashSession> LoadAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await sessions.FindAsync(tenantId, CashSessionId.From(id), cancellationToken))
        ?? throw new NotFoundException(CashSessionsErrors.SessionNotFound);

    /// <summary>Encaissements de la session : de son ouverture à sa clôture (ou à maintenant si elle est ouverte), bornes comprises.</summary>
    public Task<IReadOnlyList<CashSessionTakings>> TakingsAsync(CashSession session, DateTimeOffset untilUtc, CancellationToken cancellationToken) =>
        journal.TakingsAsync(session.TenantId, session.LocationId, session.Currency, session.OpenedAtUtc, untilUtc.AddTicks(1), cancellationToken);

    public static void EnsurePeriod(DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        DomainException.ThrowIf(toUtc <= fromUtc || toUtc - fromUtc > TimeSpan.FromDays(MaxPeriodDays), CashSessionsErrors.PeriodInvalid);
}

/// <summary>Ouvre la caisse d'un établissement avec un fond (zéro possible). Renvoie l'id de la session.</summary>
public sealed record OpenCashSessionCommand(Guid LocationId, decimal OpeningFloat) : ICommand<Guid>;

public sealed class OpenCashSessionHandler(CashDesk desk, ICashSessionLock sessionLock, IPaymentTenantCurrency currencies)
    : ICommandHandler<OpenCashSessionCommand, Guid>
{
    public async Task<Guid> HandleAsync(OpenCashSessionCommand command, CancellationToken cancellationToken = default)
    {
        await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = desk.Workspace.CurrentTenant;
        var location = await desk.RequireLocationAsync(tenant, command.LocationId, cancellationToken);
        var currency = await currencies.FindAsync(tenant, cancellationToken) ?? throw new DomainException(CashSessionsErrors.CurrencyUnknown);

        // Verrou AVANT les lectures : deux ouvertures simultanées prendraient le même numéro.
        await sessionLock.LockAsync(tenant, location, cancellationToken);
        DomainException.ThrowIf(await desk.Sessions.FindOpenAsync(tenant, location, cancellationToken) is not null, CashSessionsErrors.AlreadyOpen);
        var number = await desk.Sessions.LastNumberAsync(tenant, location, cancellationToken) + 1;

        var session = CashSession.Open(tenant, location, number, Money.Of(command.OpeningFloat, currency), desk.CurrentUser, desk.Clock);
        desk.Sessions.Add(session);
        await desk.Workspace.SaveAsync(cancellationToken);
        return session.Id.Value;
    }
}

/// <summary>Entrée ou sortie d'espèces hors vente, avec son motif.</summary>
public sealed record RecordCashMovementCommand(Guid CashSessionId, CashMovementKind Kind, decimal Amount, string? Reason) : ICommand;

public sealed class RecordCashMovementHandler(CashDesk desk) : ICommandHandler<RecordCashMovementCommand>
{
    public async Task HandleAsync(RecordCashMovementCommand command, CancellationToken cancellationToken = default)
    {
        await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var session = await desk.LoadAsync(desk.Workspace.CurrentTenant, command.CashSessionId, cancellationToken);
        session.RecordMovement(command.Kind, Money.Of(command.Amount, session.Currency), command.Reason, desk.CurrentUser, desk.Clock);
        await desk.Workspace.SaveAsync(cancellationToken);
    }
}

public sealed record CountedAmount(PaymentMethodType Method, decimal Amount);

public sealed record DenominationInput(decimal Value, int Count);

/// <summary>
/// Clôture : espèces (montant ou détail par coupure) et chaque moyen encaissé sur place pendant la session doivent être
/// comptés. Les écarts sont figés dans le ticket Z.
/// </summary>
public sealed record CloseCashSessionCommand(
    Guid CashSessionId, IReadOnlyList<CountedAmount>? Counted, IReadOnlyList<DenominationInput>? Denominations, string? Note) : ICommand;

public sealed class CloseCashSessionHandler(CashDesk desk) : ICommandHandler<CloseCashSessionCommand>
{
    public async Task HandleAsync(CloseCashSessionCommand command, CancellationToken cancellationToken = default)
    {
        await desk.Workspace.RequireWritableTenantAsync(cancellationToken);
        var session = await desk.LoadAsync(desk.Workspace.CurrentTenant, command.CashSessionId, cancellationToken);
        var counted = command.Counted ?? [];
        DomainException.ThrowIf(counted.GroupBy(item => item.Method).Any(group => group.Count() > 1), CashSessionErrors.TakingsDuplicate);

        var takings = (await desk.TakingsAsync(session, desk.Clock.UtcNow, cancellationToken))
            .Where(item => item.Method.IsCountedAtClose())
            .ToList();
        session.Close(
            takings,
            counted.ToDictionary(item => item.Method, item => item.Amount),
            command.Denominations?.Select(item => new CashDenominationCount(item.Value, item.Count)).ToList(),
            command.Note,
            desk.CurrentUser,
            desk.Clock);
        await desk.Workspace.SaveAsync(cancellationToken);
    }
}

public sealed record CashMovementDto(CashMovementKind Kind, decimal Amount, string Reason, DateTimeOffset AtUtc, Guid? By);

/// <param name="ToCount">Moyen compté à la clôture (espèces, terminal, mobile money reçu, titres) ; les autres sont pour information.</param>
public sealed record CashTakingsDto(PaymentMethodType Method, bool ToCount, decimal Collected, decimal Refunded, decimal Net);

public sealed record CashCountLineDto(PaymentMethodType Method, decimal Expected, decimal Counted, decimal Difference);

public sealed record CashDenominationDto(decimal Value, int Count);

/// <param name="ExpectedCash">Espèces attendues dans le tiroir : figées à la clôture, calculées à l'instant sinon.</param>
/// <param name="Takings">Encaissements de la session (jusqu'à maintenant si elle est ouverte).</param>
public sealed record CashSessionDto(
    Guid Id,
    Guid LocationId,
    int Number,
    CashSessionStatus Status,
    string CurrencyCode,
    decimal OpeningFloat,
    DateTimeOffset OpenedAtUtc,
    Guid? OpenedBy,
    DateTimeOffset? ClosedAtUtc,
    Guid? ClosedBy,
    string? ClosingNote,
    decimal PaidIn,
    decimal PaidOut,
    decimal ExpectedCash,
    decimal TotalDifference,
    IReadOnlyList<CashMovementDto> Movements,
    IReadOnlyList<CashTakingsDto> Takings,
    IReadOnlyList<CashCountLineDto> Lines,
    IReadOnlyList<CashDenominationDto> Denominations)
{
    public static CashSessionDto From(CashSession session, IReadOnlyList<CashSessionTakings> takings)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(takings);
        var cash = takings.FirstOrDefault(item => item.Method == PaymentMethodType.Cash)?.Net ?? Money.Zero(session.Currency);
        var frozen = session.Lines.FirstOrDefault(line => line.Method == PaymentMethodType.Cash)?.Expected;
        return new CashSessionDto(
            session.Id.Value,
            session.LocationId.Value,
            session.Number,
            session.Status,
            session.Currency.Code,
            session.OpeningFloat.Amount,
            session.OpenedAtUtc,
            session.OpenedBy,
            session.ClosedAtUtc,
            session.ClosedBy,
            session.ClosingNote,
            session.PaidIn.Amount,
            session.PaidOut.Amount,
            (frozen ?? session.ExpectedCash(cash)).Amount,
            session.TotalDifference.Amount,
            session.Movements.Select(movement => new CashMovementDto(movement.Kind, movement.Amount.Amount, movement.Reason, movement.AtUtc, movement.By)).ToList(),
            takings
                .OrderBy(item => item.Method == PaymentMethodType.Cash ? 0 : (int)item.Method)
                .Select(item => new CashTakingsDto(item.Method, item.Method.IsCountedAtClose(), item.Collected.Amount, item.Refunded.Amount, item.Net.Amount))
                .ToList(),
            session.Lines.Select(line => new CashCountLineDto(line.Method, line.Expected.Amount, line.Counted.Amount, line.Difference.Amount)).ToList(),
            session.Denominations.Select(coin => new CashDenominationDto(coin.Value, coin.Count)).ToList());
    }
}

public sealed record GetCashSessionQuery(Guid CashSessionId) : IQuery<CashSessionDto>;

public sealed class GetCashSessionHandler(CashDesk desk) : IQueryHandler<GetCashSessionQuery, CashSessionDto>
{
    public async Task<CashSessionDto> HandleAsync(GetCashSessionQuery query, CancellationToken cancellationToken = default)
    {
        var session = await desk.LoadAsync(desk.Workspace.CurrentTenant, query.CashSessionId, cancellationToken);
        var takings = await desk.TakingsAsync(session, session.ClosedAtUtc ?? desk.Clock.UtcNow, cancellationToken);
        return CashSessionDto.From(session, takings);
    }
}

/// <summary>La session ouverte de l'établissement, ou null.</summary>
public sealed record GetOpenCashSessionQuery(Guid LocationId) : IQuery<CashSessionDto?>;

public sealed class GetOpenCashSessionHandler(CashDesk desk) : IQueryHandler<GetOpenCashSessionQuery, CashSessionDto?>
{
    public async Task<CashSessionDto?> HandleAsync(GetOpenCashSessionQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = desk.Workspace.CurrentTenant;
        var location = await desk.RequireLocationAsync(tenant, query.LocationId, cancellationToken);
        var session = await desk.Sessions.FindOpenAsync(tenant, location, cancellationToken);
        return session is null ? null : CashSessionDto.From(session, await desk.TakingsAsync(session, desk.Clock.UtcNow, cancellationToken));
    }
}

public sealed record CashSessionSummaryDto(
    Guid Id,
    Guid LocationId,
    int Number,
    CashSessionStatus Status,
    string CurrencyCode,
    decimal OpeningFloat,
    DateTimeOffset OpenedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    decimal TotalDifference,
    decimal? CountedCash);

/// <summary>Historique des sessions, les plus récentes d'abord (ouvertes dans la période si elle est donnée).</summary>
public sealed record ListCashSessionsQuery(Guid? LocationId, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, int? Limit) : IQuery<IReadOnlyList<CashSessionSummaryDto>>;

public sealed class ListCashSessionsHandler(CashDesk desk) : IQueryHandler<ListCashSessionsQuery, IReadOnlyList<CashSessionSummaryDto>>
{
    public async Task<IReadOnlyList<CashSessionSummaryDto>> HandleAsync(ListCashSessionsQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = desk.Workspace.CurrentTenant;
        var limit = query.Limit ?? 50;
        DomainException.ThrowIf(limit is < 1 or > 1000, CashSessionsErrors.LimitInvalid);
        if (query.FromUtc is { } from && query.ToUtc is { } to)
        {
            CashDesk.EnsurePeriod(from, to);
        }

        var location = query.LocationId is { } id ? await desk.RequireLocationAsync(tenant, id, cancellationToken) : (LocationId?)null;
        var sessions = await desk.Sessions.ListAsync(tenant, location, query.FromUtc, query.ToUtc, limit, cancellationToken);
        return sessions
            .Select(session => new CashSessionSummaryDto(
                session.Id.Value,
                session.LocationId.Value,
                session.Number,
                session.Status,
                session.Currency.Code,
                session.OpeningFloat.Amount,
                session.OpenedAtUtc,
                session.ClosedAtUtc,
                session.TotalDifference.Amount,
                session.Lines.FirstOrDefault(line => line.Method == PaymentMethodType.Cash)?.Counted.Amount))
            .ToList();
    }
}

public sealed record PaymentJournalEntryDto(
    Guid PaymentId,
    Guid OrderId,
    Guid LocationId,
    PaymentJournalEntryKind Kind,
    string Provider,
    PaymentMethodType Method,
    bool ToCount,
    string CurrencyCode,
    decimal Amount,
    DateTimeOffset AtUtc,
    string? Reference);

/// <param name="Truncated">Plus de mouvements que la limite : les totaux portent seulement sur ceux renvoyés.</param>
public sealed record PaymentJournalDto(
    DateTimeOffset FromUtc, DateTimeOffset ToUtc, IReadOnlyList<PaymentJournalEntryDto> Entries, IReadOnlyList<CashTakingsDto> Totals, bool Truncated);

/// <summary>Journal des encaissements et remboursements d'une période (93 jours au plus), d'un établissement ou de tous.</summary>
public sealed record GetPaymentJournalQuery(Guid? LocationId, DateTimeOffset FromUtc, DateTimeOffset ToUtc, int? Limit) : IQuery<PaymentJournalDto>;

public sealed class GetPaymentJournalHandler(CashDesk desk) : IQueryHandler<GetPaymentJournalQuery, PaymentJournalDto>
{
    public async Task<PaymentJournalDto> HandleAsync(GetPaymentJournalQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = desk.Workspace.CurrentTenant;
        CashDesk.EnsurePeriod(query.FromUtc, query.ToUtc);
        var limit = query.Limit ?? 500;
        DomainException.ThrowIf(limit is < 1 or > 1000, CashSessionsErrors.LimitInvalid);
        var location = query.LocationId is { } id ? await desk.RequireLocationAsync(tenant, id, cancellationToken) : (LocationId?)null;

        var entries = await desk.Journal.ListAsync(tenant, location, query.FromUtc, query.ToUtc, limit + 1, cancellationToken);
        var kept = entries.Take(limit).ToList();
        var totals = kept
            .GroupBy(entry => entry.Method)
            .Select(group =>
            {
                var currency = group.First().Amount.Currency;
                var collected = group.Where(entry => entry.Kind == PaymentJournalEntryKind.Capture).Aggregate(Money.Zero(currency), (sum, entry) => sum + entry.Amount);
                var refunded = group.Where(entry => entry.Kind == PaymentJournalEntryKind.Refund).Aggregate(Money.Zero(currency), (sum, entry) => sum + entry.Amount);
                return new CashTakingsDto(group.Key, group.Key.IsCountedAtClose(), collected.Amount, refunded.Amount, (collected - refunded).Amount);
            })
            .OrderBy(item => item.Method == PaymentMethodType.Cash ? 0 : (int)item.Method)
            .ToList();

        return new PaymentJournalDto(
            query.FromUtc,
            query.ToUtc,
            kept.Select(entry => new PaymentJournalEntryDto(
                    entry.PaymentId.Value,
                    entry.OrderId.Value,
                    entry.LocationId.Value,
                    entry.Kind,
                    entry.Provider.Value,
                    entry.Method,
                    entry.Method.IsCountedAtClose(),
                    entry.Amount.Currency.Code,
                    entry.Amount.Amount,
                    entry.AtUtc,
                    entry.Reference))
                .ToList(),
            totals,
            entries.Count > limit);
    }
}
