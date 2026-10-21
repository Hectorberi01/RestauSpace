using Payments.Domain;

namespace Payments.Application.Queries;

// Lectures du tenant courant, ouvertes à tout membre et possibles même quand le tenant est suspendu. Un paiement d'un
// autre tenant n'est jamais trouvé.

public sealed record PaymentAttemptDto(
    Guid Id,
    string ProviderReference,
    PaymentAttemptStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? AuthorizedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? FailureCode,
    string? FailureReason,
    DateTimeOffset? CapturedAfterCancellationAtUtc);

public sealed record RefundDto(
    Guid Id, decimal Amount, string Reason, RefundStatus Status, string? ProviderReference, DateTimeOffset RequestedAtUtc, DateTimeOffset? CompletedAtUtc);

/// <param name="SettledAmount">Ce que ce paiement apporte au règlement de la commande.</param>
/// <param name="HasUnexpectedCapture">Argent pris par le prestataire après annulation : à rembourser, ne règle rien.</param>
public sealed record PaymentDto(
    Guid Id,
    Guid OrderId,
    Guid LocationId,
    string Provider,
    PaymentMethodType Method,
    bool IsOffline,
    PaymentStatus Status,
    string CurrencyCode,
    decimal Amount,
    decimal CapturedAmount,
    decimal RefundedAmount,
    decimal PendingRefundAmount,
    decimal RefundableAmount,
    decimal SettledAmount,
    bool HasUnexpectedCapture,
    IReadOnlyList<PaymentAttemptDto> Attempts,
    IReadOnlyList<RefundDto> Refunds,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AuthorizedAtUtc,
    DateTimeOffset? CapturedAtUtc,
    DateTimeOffset? FailedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    string? CancellationReason,
    IReadOnlyList<PaymentItemShareDto>? CoveredItems = null,
    PaymentPurpose Purpose = PaymentPurpose.Order,
    Guid? ReservationId = null)
{
    /// <param name="isOffline">Nature du moyen, lue au catalogue de la plateforme.</param>
    public static PaymentDto From(Payment payment, bool isOffline)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return new PaymentDto(
            payment.Id.Value,
            payment.OrderId.Value,
            payment.LocationId.Value,
            payment.Provider.Value,
            payment.Method,
            isOffline,
            payment.Status,
            payment.Currency.Code,
            payment.Amount.Amount,
            payment.CapturedAmount.Amount,
            payment.RefundedAmount.Amount,
            payment.PendingRefundAmount.Amount,
            payment.RefundableAmount.Amount,
            payment.SettledAmount.Amount,
            payment.HasUnexpectedCapture,
            payment.Attempts
                .Select(attempt => new PaymentAttemptDto(
                    attempt.Id.Value,
                    attempt.ProviderReference.Value,
                    attempt.Status,
                    attempt.StartedAtUtc,
                    attempt.AuthorizedAtUtc,
                    attempt.CompletedAtUtc,
                    attempt.FailureCode?.Value,
                    attempt.FailureReason,
                    attempt.CapturedAfterCancellationAtUtc))
                .ToList(),
            payment.Refunds
                .Select(refund => new RefundDto(
                    refund.Id.Value,
                    refund.Amount.Amount,
                    refund.Reason,
                    refund.Status,
                    refund.ProviderReference?.Value,
                    refund.RequestedAtUtc,
                    refund.CompletedAtUtc))
                .ToList(),
            payment.CreatedAtUtc,
            payment.AuthorizedAtUtc,
            payment.CapturedAtUtc,
            payment.FailedAtUtc,
            payment.CancelledAtUtc,
            payment.CancellationReason,
            payment.CoveredItems.Select(item => new PaymentItemShareDto(item.ItemId, item.Quantity)).ToList(),
            payment.Purpose,
            payment.ReservationId);
    }
}

public sealed record PaymentItemShareDto(Guid ItemId, int Quantity);

/// <param name="SettledAmount">Somme réglée (celle que vérifie la clôture de la commande).</param>
/// <param name="AmountDue">Ce qu'un nouveau paiement peut encore demander (paiements en cours réservés).</param>
public sealed record OrderPaymentsDto(
    Guid OrderId, string CurrencyCode, decimal Total, decimal SettledAmount, decimal AmountDue, IReadOnlyList<PaymentDto> Payments);

public sealed record GetPaymentByIdQuery(Guid PaymentId) : IQuery<PaymentDto>;

public sealed class GetPaymentByIdHandler(PaymentsWorkspace workspace) : IQueryHandler<GetPaymentByIdQuery, PaymentDto>
{
    public async Task<PaymentDto> HandleAsync(GetPaymentByIdQuery query, CancellationToken cancellationToken = default)
    {
        var payment = await workspace.LoadAsync(workspace.CurrentTenant, query.PaymentId, cancellationToken);
        var offline = await OfflineProviders.ListAsync(workspace, cancellationToken);
        return PaymentDto.From(payment, offline.Contains(payment.Provider));
    }
}

/// <summary>Écran de caisse : total de la commande, réglé, reste à payer et paiements (les plus anciens d'abord).</summary>
public sealed record GetOrderPaymentsQuery(Guid OrderId) : IQuery<OrderPaymentsDto>;

public sealed class GetOrderPaymentsHandler(PaymentsWorkspace workspace) : IQueryHandler<GetOrderPaymentsQuery, OrderPaymentsDto>
{
    public async Task<OrderPaymentsDto> HandleAsync(GetOrderPaymentsQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var order = await workspace.RequireOrderAsync(tenant, query.OrderId, cancellationToken);
        var payments = await workspace.Payments.ListByOrderAsync(tenant, order.OrderId, cancellationToken);
        var offline = await OfflineProviders.ListAsync(workspace, cancellationToken);
        var currency = order.Total.Currency;
        var due = order.Total - OrderBalance.Committed(payments, currency);

        return new OrderPaymentsDto(
            order.OrderId.Value,
            currency.Code,
            order.Total.Amount,
            OrderBalance.Settled(payments, currency).Amount,
            order.AcceptsPayments && due.IsPositive ? due.Amount : 0m,
            payments.Select(payment => PaymentDto.From(payment, offline.Contains(payment.Provider))).ToList());
    }
}

/// <summary>
/// Pour Orders (via l'hôte), avant de clôturer une commande : la somme des <see cref="Payment.SettledAmount"/> de ses
/// paiements, dans sa devise. Zéro sans paiement. Ne lit pas la commande elle-même.
/// </summary>
public sealed record GetOrderSettledAmountQuery(Guid OrderId, string CurrencyCode) : IQuery<decimal>;

public sealed class GetOrderSettledAmountHandler(PaymentsWorkspace workspace) : IQueryHandler<GetOrderSettledAmountQuery, decimal>
{
    public async Task<decimal> HandleAsync(GetOrderSettledAmountQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var currency = Currency.FromCode(query.CurrencyCode);
        if (query.OrderId == Guid.Empty)
        {
            return 0m;
        }

        var payments = await workspace.Payments.ListByOrderAsync(tenant, OrderId.From(query.OrderId), cancellationToken);
        // Un paiement dans une autre devise ne peut pas exister (le domaine l'interdit) : on l'écarte plutôt que d'échouer.
        return OrderBalance.Settled(payments.Where(payment => payment.Currency == currency), currency).Amount;
    }
}

internal static class OfflineProviders
{
    public static async Task<HashSet<PaymentProviderCode>> ListAsync(PaymentsWorkspace workspace, CancellationToken cancellationToken) =>
        (await workspace.Providers.ListAsync(cancellationToken))
            .Where(provider => provider.Type == PaymentProviderType.OnSite)
            .Select(provider => provider.Code)
            .ToHashSet();
}
