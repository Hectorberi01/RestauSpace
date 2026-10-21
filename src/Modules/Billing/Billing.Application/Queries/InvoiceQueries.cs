using Billing.Application.Abstractions;
using Billing.Domain;

namespace Billing.Application.Queries;

/// <summary>
/// La facture de la plateforme pour un paiement d'abonnement : émetteur (réglages de l'hôte), client, lignes recopiées au
/// paiement. <see cref="Number"/> est null pour un paiement enregistré avant la numérotation : c'est alors un reçu.
/// </summary>
public sealed record InvoiceDto(
    Guid PaymentId,
    string? Number,
    DateTimeOffset IssuedAtUtc,
    InvoiceIssuer Issuer,
    string CustomerName,
    string PlanName,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    IReadOnlyList<ChargeLineDto> Lines,
    decimal Total,
    string CurrencyCode,
    string? CollectedBy);

/// <summary>Une facture du restaurant du contexte ; celle d'un autre restaurant est introuvable.</summary>
public sealed record GetMyInvoiceQuery(Guid PaymentId) : IQuery<InvoiceDto>;

public sealed class GetMyInvoiceHandler(BillingWorkspace workspace, IInvoiceIssuer issuer) : IQueryHandler<GetMyInvoiceQuery, InvoiceDto>
{
    public async Task<InvoiceDto> HandleAsync(GetMyInvoiceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var payment = (query.PaymentId == Guid.Empty
                ? null
                : await workspace.Payments.FindAsync(tenant, SubscriptionPaymentId.From(query.PaymentId), cancellationToken))
            ?? throw new NotFoundException(BillingApplicationErrors.InvoiceNotFound);
        var customer = await workspace.Tenants.NameAsync(tenant, cancellationToken) ?? string.Empty;
        var lines = payment.Lines.Count > 0
            ? payment.Lines.Select(ChargeLineDto.From).ToList()
            : [new ChargeLineDto(payment.PlanId.Value, PlanKind.Base, payment.PlanName, 1, payment.Amount.Amount, payment.Amount.Amount)];
        return new InvoiceDto(
            payment.Id.Value,
            payment.InvoiceNumber,
            payment.RecordedAtUtc,
            issuer.Current,
            customer,
            payment.PlanName,
            payment.PaidPeriod.StartUtc,
            payment.PaidPeriod.EndUtc,
            lines,
            payment.Amount.Amount,
            payment.Amount.Currency.Code,
            payment.CollectedBy);
    }
}
