using Payments.Application.Abstractions;
using Payments.Domain;
using Payments.Infrastructure.Persistence.Configurations;

namespace Payments.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte : deux verrous sur la même porte.

internal sealed class PaymentRepository(PaymentsDbContext context) : IPaymentRepository
{
    // Tentatives et remboursements toujours chargés dans leur ordre d'arrivée.
    private IQueryable<Payment> Payments => context.Payments
        .Include(payment => payment.Attempts.OrderBy(attempt => EF.Property<int>(attempt, PaymentAttemptConfiguration.PositionProperty)))
        .Include(payment => payment.Refunds.OrderBy(refund => EF.Property<int>(refund, RefundConfiguration.PositionProperty)))
        .AsSplitQuery();

    public Task<Payment?> FindAsync(TenantId tenantId, PaymentId id, CancellationToken cancellationToken = default) =>
        Payments.SingleOrDefaultAsync(payment => payment.TenantId == tenantId && payment.Id == id, cancellationToken);

    public async Task<Payment?> FindByProviderReferenceAsync(
        TenantId tenantId, PaymentProviderCode provider, ProviderReference reference, CancellationToken cancellationToken = default)
    {
        // Une référence de prestataire en ligne est unique chez lui ; deux paiements pour la même serait une incohérence :
        // on ne choisit pas au hasard.
        var matches = await Payments
            .Where(payment => payment.TenantId == tenantId
                && payment.Provider == provider
                && payment.Attempts.Any(attempt => attempt.ProviderReference == reference))
            .OrderBy(payment => payment.Id)
            .Take(2)
            .ToListAsync(cancellationToken);
        return matches.Count == 1 ? matches[0] : null;
    }

    public async Task<IReadOnlyList<Payment>> ListByOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default) =>
        await Payments.AsNoTracking()
            .Where(payment => payment.TenantId == tenantId && payment.OrderId == orderId)
            .OrderBy(payment => payment.CreatedAtUtc)
            .ThenBy(payment => payment.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Payment>> ListByReservationAsync(TenantId tenantId, Guid reservationId, CancellationToken cancellationToken = default) =>
        await Payments.AsNoTracking()
            .Where(payment => payment.TenantId == tenantId && payment.ReservationId == reservationId)
            .OrderBy(payment => payment.CreatedAtUtc)
            .ThenBy(payment => payment.Id)
            .ToListAsync(cancellationToken);

    public void Add(Payment payment) => context.Payments.Add(payment);
}

/// <summary>
/// Pour l'Infrastructure seulement (relais vers le prestataire, après la sauvegarde) : lire un paiement et la configuration
/// du restaurant SANS tenant courant. Le résultat sert à agir ensuite POUR ce tenant.
/// </summary>
internal sealed class PaymentDirectory(PaymentsDbContext context)
{
    public Task<Payment?> FindAsync(PaymentId id, CancellationToken cancellationToken = default) =>
        context.Payments.IgnoreQueryFilters().AsNoTracking()
            .Include(payment => payment.Attempts)
            .Include(payment => payment.Refunds)
            .AsSplitQuery()
            .SingleOrDefaultAsync(payment => payment.Id == id, cancellationToken);

    public Task<RestaurantPaymentConfiguration?> FindConfigurationAsync(
        TenantId tenantId, PaymentProviderCode provider, CancellationToken cancellationToken = default) =>
        context.Configurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(configuration => configuration.TenantId == tenantId && configuration.Provider == provider, cancellationToken);

}

internal sealed class PaymentProviderRepository(PaymentsDbContext context) : IPaymentProviderRepository
{
    public Task<PaymentProvider?> FindAsync(PaymentProviderId id, CancellationToken cancellationToken = default) =>
        context.Providers.SingleOrDefaultAsync(provider => provider.Id == id, cancellationToken);

    public Task<PaymentProvider?> FindByCodeAsync(PaymentProviderCode code, CancellationToken cancellationToken = default) =>
        context.Providers.SingleOrDefaultAsync(provider => provider.Code == code, cancellationToken);

    public async Task<IReadOnlyList<PaymentProvider>> ListAsync(CancellationToken cancellationToken = default) =>
        await context.Providers.AsNoTracking()
            .OrderBy(provider => provider.DisplayName)
            .ThenBy(provider => provider.Id)
            .ToListAsync(cancellationToken);

    public void Add(PaymentProvider provider) => context.Providers.Add(provider);
}

internal sealed class RestaurantPaymentConfigurationRepository(PaymentsDbContext context) : IRestaurantPaymentConfigurationRepository
{
    // Affectations aux établissements toujours chargées dans leur ordre d'ajout.
    private IQueryable<RestaurantPaymentConfiguration> Configurations => context.Configurations
        .Include(configuration => configuration.Assignments.OrderBy(
            assignment => EF.Property<int>(assignment, PaymentMethodAssignmentConfiguration.PositionProperty)));

    public Task<RestaurantPaymentConfiguration?> FindAsync(
        TenantId tenantId, RestaurantPaymentConfigurationId id, CancellationToken cancellationToken = default) =>
        Configurations.SingleOrDefaultAsync(configuration => configuration.TenantId == tenantId && configuration.Id == id, cancellationToken);

    public Task<RestaurantPaymentConfiguration?> FindByProviderAsync(
        TenantId tenantId, PaymentProviderCode provider, CancellationToken cancellationToken = default) =>
        Configurations.SingleOrDefaultAsync(
            configuration => configuration.TenantId == tenantId && configuration.Provider == provider, cancellationToken);

    public async Task<IReadOnlyList<RestaurantPaymentConfiguration>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        await Configurations.AsNoTracking()
            .Where(configuration => configuration.TenantId == tenantId)
            .OrderBy(configuration => configuration.CreatedAtUtc)
            .ThenBy(configuration => configuration.Id)
            .ToListAsync(cancellationToken);

    public void Add(RestaurantPaymentConfiguration configuration) => context.Configurations.Add(configuration);
}

internal sealed class CashSessionRepository(PaymentsDbContext context) : ICashSessionRepository
{
    public Task<CashSession?> FindAsync(TenantId tenantId, CashSessionId id, CancellationToken cancellationToken = default) =>
        context.CashSessions.SingleOrDefaultAsync(session => session.TenantId == tenantId && session.Id == id, cancellationToken);

    public Task<CashSession?> FindOpenAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        context.CashSessions.SingleOrDefaultAsync(
            session => session.TenantId == tenantId && session.LocationId == locationId && session.Status == CashSessionStatus.Open,
            cancellationToken);

    public async Task<int> LastNumberAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        await context.CashSessions
            .Where(session => session.TenantId == tenantId && session.LocationId == locationId)
            .MaxAsync(session => (int?)session.Number, cancellationToken) ?? 0;

    public async Task<IReadOnlyList<CashSession>> ListAsync(
        TenantId tenantId, LocationId? locationId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int limit, CancellationToken cancellationToken = default)
    {
        var query = context.CashSessions.AsNoTracking().Where(session => session.TenantId == tenantId);
        if (locationId is { } location)
        {
            query = query.Where(session => session.LocationId == location);
        }

        if (fromUtc is { } from)
        {
            query = query.Where(session => session.OpenedAtUtc >= from);
        }

        if (toUtc is { } to)
        {
            query = query.Where(session => session.OpenedAtUtc < to);
        }

        return await query
            .OrderByDescending(session => session.OpenedAtUtc)
            .ThenByDescending(session => session.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public void Add(CashSession session) => context.CashSessions.Add(session);
}

/// <summary>
/// Mouvements d'argent lus directement dans les paiements : captures (hors paiements annulés, dont une capture tardive
/// ne paie rien) et remboursements réussis, datés par leur capture ou leur aboutissement.
/// </summary>
internal sealed class PaymentJournal(PaymentsDbContext context) : IPaymentJournal
{
    public async Task<IReadOnlyList<PaymentJournalEntry>> ListAsync(
        TenantId tenantId, LocationId? locationId, DateTimeOffset fromUtc, DateTimeOffset toUtc, int limit, CancellationToken cancellationToken = default)
    {
        var payments = Scope(tenantId, locationId);

        var captures = await payments
            .Where(payment => payment.Status != PaymentStatus.Cancelled
                && payment.CapturedAtUtc >= fromUtc && payment.CapturedAtUtc < toUtc
                && payment.CapturedAmount.Amount > 0)
            .OrderByDescending(payment => payment.CapturedAtUtc)
            .Take(limit)
            .Select(payment => new
            {
                payment.Id,
                payment.OrderId,
                payment.LocationId,
                payment.Provider,
                payment.Method,
                payment.CapturedAmount.Amount,
                payment.CapturedAmount.Currency,
                At = payment.CapturedAtUtc!.Value,
                Reference = payment.Attempts
                    .Where(attempt => attempt.Status == PaymentAttemptStatus.Succeeded)
                    .Select(attempt => attempt.ProviderReference)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var refunds = await payments
            .SelectMany(payment => payment.Refunds
                .Where(refund => refund.Status == RefundStatus.Succeeded && refund.CompletedAtUtc >= fromUtc && refund.CompletedAtUtc < toUtc)
                .Select(refund => new
                {
                    payment.Id,
                    payment.OrderId,
                    payment.LocationId,
                    payment.Provider,
                    payment.Method,
                    refund.Amount.Amount,
                    refund.Amount.Currency,
                    At = refund.CompletedAtUtc!.Value,
                    Reference = refund.ProviderReference,
                }))
            .OrderByDescending(refund => refund.At)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return captures
            .Select(item => new PaymentJournalEntry(
                item.Id, item.OrderId, item.LocationId, PaymentJournalEntryKind.Capture, item.Provider, item.Method,
                Money.Of(item.Amount, item.Currency), item.At, item.Reference?.Value))
            .Concat(refunds.Select(item => new PaymentJournalEntry(
                item.Id, item.OrderId, item.LocationId, PaymentJournalEntryKind.Refund, item.Provider, item.Method,
                Money.Of(item.Amount, item.Currency), item.At, item.Reference?.Value)))
            .OrderByDescending(entry => entry.AtUtc)
            .Take(limit)
            .ToList();
    }

    public async Task<IReadOnlyList<CashSessionTakings>> TakingsAsync(
        TenantId tenantId, LocationId locationId, Currency currency, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var payments = Scope(tenantId, locationId).Where(payment => payment.Amount.Currency == currency);

        var collected = await payments
            .Where(payment => payment.Status != PaymentStatus.Cancelled && payment.CapturedAtUtc >= fromUtc && payment.CapturedAtUtc < toUtc)
            .GroupBy(payment => payment.Method)
            .Select(group => new { Method = group.Key, Amount = group.Sum(payment => payment.CapturedAmount.Amount) })
            .ToListAsync(cancellationToken);

        var refunded = await payments
            .SelectMany(payment => payment.Refunds
                .Where(refund => refund.Status == RefundStatus.Succeeded && refund.CompletedAtUtc >= fromUtc && refund.CompletedAtUtc < toUtc)
                .Select(refund => new { payment.Method, refund.Amount.Amount }))
            .GroupBy(item => item.Method)
            .Select(group => new { Method = group.Key, Amount = group.Sum(item => item.Amount) })
            .ToListAsync(cancellationToken);

        return collected.Select(item => item.Method)
            .Union(refunded.Select(item => item.Method))
            .Select(method => new CashSessionTakings(
                method,
                Money.Of(collected.Where(item => item.Method == method).Sum(item => item.Amount), currency),
                Money.Of(refunded.Where(item => item.Method == method).Sum(item => item.Amount), currency)))
            .ToList();
    }

    private IQueryable<Payment> Scope(TenantId tenantId, LocationId? locationId)
    {
        var payments = context.Payments.AsNoTracking().Where(payment => payment.TenantId == tenantId);
        return locationId is { } location ? payments.Where(payment => payment.LocationId == location) : payments;
    }
}
