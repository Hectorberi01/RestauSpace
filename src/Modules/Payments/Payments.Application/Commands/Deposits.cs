using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Application.Commands;

// Acompte de réservation : encaissé sur le compte du restaurant comme un paiement de commande, mais rattaché à la
// réservation (montant fixé par Reservations, jamais par la requête). Ce qu'il détient est signalé à la réservation
// (DepositSettlementChanged) ; à l'arrivée, il est déduit de l'addition (ApplyDeposit) et règle alors la commande.

internal static class DepositCollection
{
    public static async Task<(DepositReference Deposit, RestaurantPaymentConfiguration Configuration)> PrepareAsync(
        PaymentsWorkspace workspace,
        IPaymentReservations reservations,
        IPaymentOrderLock orderLock,
        Guid reservationId,
        PaymentProviderCode provider,
        PaymentProviderType type,
        CancellationToken cancellationToken)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var deposit = (reservationId == Guid.Empty ? null : await reservations.FindDepositAsync(tenant, reservationId, cancellationToken))
            ?? throw new NotFoundException(PaymentsApplicationErrors.DepositNotFound);
        DomainException.ThrowIf(!deposit.AcceptsPayment, PaymentsApplicationErrors.DepositNotPayable);
        var currency = deposit.Amount.Currency;
        var (configuration, _) = await workspace.RequireAcceptedConfigurationAsync(tenant, provider, type, currency, cancellationToken);

        // Verrou AVANT la lecture des paiements existants, sous l'id de la réservation.
        await orderLock.LockAsync(tenant, OrderId.From(reservationId), cancellationToken);
        var existing = await workspace.Payments.ListByReservationAsync(tenant, reservationId, cancellationToken);
        var due = deposit.Amount - OrderBalance.Committed(existing, currency);
        DomainException.ThrowIf(!due.IsPositive, PaymentsApplicationErrors.NothingDue);
        return (DepositReference.Create(tenant, reservationId, deposit.LocationId, due), configuration);
    }
}

/// <summary>
/// Le client paie l'acompte en ligne (site du restaurant, via l'hôte) : tout ce qui reste dû. <paramref name="ReturnUrl"/>
/// est posé par le serveur, jamais par une route du personnel.
/// </summary>
public sealed record StartDepositPaymentCommand(
    Guid ReservationId, string? Provider, PaymentMethodType? PaymentMethod = null, string? PayerPhone = null, Uri? ReturnUrl = null)
    : ICommand<OnlinePaymentStarted>
{
    public override string ToString() => $"StartDepositPaymentCommand ({ReservationId}, {Provider}, ***)";
}

public sealed class StartDepositPaymentHandler(PaymentsWorkspace workspace, IPaymentReservations reservations, IPaymentOrderLock orderLock, IClock clock)
    : ICommandHandler<StartDepositPaymentCommand, OnlinePaymentStarted>
{
    public async Task<OnlinePaymentStarted> HandleAsync(StartDepositPaymentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var provider = PaymentProviderCode.Create(command.Provider);
        var (deposit, configuration) = await DepositCollection.PrepareAsync(
            workspace, reservations, orderLock, command.ReservationId, provider, PaymentProviderType.Online, cancellationToken);
        var tenant = workspace.CurrentTenant;
        var payment = Payment.CreateDeposit(configuration, Collection.MethodOf(configuration, command.PaymentMethod), deposit, deposit.AmountDue, clock);
        var gateway = await workspace.GatewayForAsync(configuration, cancellationToken);
        workspace.Payments.Add(payment);
        await workspace.SaveAsync(cancellationToken);

        GatewayCheckout checkout;
        try
        {
            checkout = await Gateways.StartAsync(
                gateway,
                new GatewayPaymentRequest(
                    tenant, payment.Id, payment.OrderId, payment.Amount, configuration.Id.Value, PayerPhones.Normalize(command.PayerPhone), command.ReturnUrl),
                cancellationToken);
        }
        catch (Exception)
        {
            payment.Cancel(StartOnlinePaymentHandler.GatewayUnavailableReason, clock);
            await workspace.SaveAsync(CancellationToken.None);
            throw;
        }

        payment.RegisterAttempt(checkout.Reference, clock);
        await workspace.SaveAsync(CancellationToken.None);
        return new OnlinePaymentStarted(payment.Id.Value, checkout.Reference.Value, checkout.CheckoutUrl);
    }
}

/// <summary>
/// L'équipe a reçu l'acompte (espèces, mobile money au téléphone, terminal…) : tout ce qui reste dû, encaissé d'un coup.
/// <paramref name="Reference"/> : n° de ticket ou de transaction. Pas de carte cadeau ni d'avoir. Renvoie l'id du paiement.
/// </summary>
public sealed record RecordOfflineDepositCommand(Guid ReservationId, string? Provider, string? Reference, PaymentMethodType? PaymentMethod = null) : ICommand<Guid>;

public sealed class RecordOfflineDepositHandler(PaymentsWorkspace workspace, IPaymentReservations reservations, IPaymentOrderLock orderLock, IClock clock)
    : ICommandHandler<RecordOfflineDepositCommand, Guid>
{
    public async Task<Guid> HandleAsync(RecordOfflineDepositCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var provider = PaymentProviderCode.Create(command.Provider);
        DomainException.ThrowIf(
            command.PaymentMethod == PaymentMethodType.GiftCard || provider.Value == GiftCards.ProviderCode, PaymentErrors.DepositMethodNotAllowed);
        var reference = ProviderReference.Create(command.Reference);
        var (deposit, configuration) = await DepositCollection.PrepareAsync(
            workspace, reservations, orderLock, command.ReservationId, provider, PaymentProviderType.OnSite, cancellationToken);
        var payment = Payment.RecordOfflineDeposit(
            configuration, Collection.MethodOf(configuration, command.PaymentMethod), deposit, deposit.AmountDue, reference, clock);
        workspace.Payments.Add(payment);
        await workspace.SaveAsync(cancellationToken);
        return payment.Id.Value;
    }
}

/// <summary>
/// Déduit l'acompte encaissé de la réservation de l'addition du repas (même établissement). S'il dépasse le reste à payer,
/// la différence doit d'abord être remboursée. Renvoie le montant déduit.
/// </summary>
public sealed record ApplyDepositCommand(Guid ReservationId, Guid OrderId) : ICommand<decimal>;

public sealed class ApplyDepositHandler(PaymentsWorkspace workspace, IPaymentOrderLock orderLock, IClock clock) : ICommandHandler<ApplyDepositCommand, decimal>
{
    public async Task<decimal> HandleAsync(ApplyDepositCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var order = await workspace.RequireOrderAsync(tenant, command.OrderId, cancellationToken);
        DomainException.ThrowIf(!order.AcceptsPayments, PaymentsApplicationErrors.OrderNotPayable);
        await orderLock.LockAsync(tenant, order.OrderId, cancellationToken);
        var deposits = (command.ReservationId == Guid.Empty ? [] : await workspace.Payments.ListByReservationAsync(tenant, command.ReservationId, cancellationToken))
            .Where(payment => payment.IsUnappliedDeposit && payment.SettledAmount.IsPositive)
            .ToList();
        DomainException.ThrowIf(deposits.Count == 0, PaymentsApplicationErrors.NoDepositToApply);

        var currency = order.Total.Currency;
        var existing = await workspace.Payments.ListByOrderAsync(tenant, order.OrderId, cancellationToken);
        var due = order.Total - OrderBalance.Committed(existing, currency);
        var applied = Money.Zero(currency);
        foreach (var listed in deposits)
        {
            // Chargé suivi pour l'écriture ; le reste à payer diminue à chaque acompte déduit.
            var payment = await workspace.LoadAsync(tenant, listed.Id.Value, cancellationToken);
            DomainException.ThrowIf(payment.SettledAmount > due, PaymentErrors.DepositAboveAmountDue);
            payment.ApplyToOrder(OrderReference.Create(tenant, order.OrderId, order.LocationId, due), clock);
            due -= payment.SettledAmount;
            applied += payment.SettledAmount;
        }

        await workspace.SaveAsync(cancellationToken);
        return applied.Amount;
    }
}

/// <summary>
/// Rend l'acompte d'une réservation (annulée à temps, payé trop tard, geste de l'équipe) : les paiements en attente sont
/// annulés, ce qui a été encaissé et pas déduit d'une addition est remboursé. Sans effet s'il n'y a rien à rendre.
/// </summary>
public sealed record RefundDepositPaymentsCommand(Guid ReservationId, string? Reason) : ICommand;

public sealed class RefundDepositPaymentsHandler(PaymentsWorkspace workspace, ICommandHandler<RequestRefundCommand, Guid> refund, IClock clock)
    : ICommandHandler<RefundDepositPaymentsCommand>
{
    public const string DefaultReason = "Acompte de réservation rendu";

    public async Task HandleAsync(RefundDepositPaymentsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        if (command.ReservationId == Guid.Empty)
        {
            return;
        }

        var reason = string.IsNullOrWhiteSpace(command.Reason) ? DefaultReason : command.Reason.Trim();
        foreach (var listed in await workspace.Payments.ListByReservationAsync(tenant, command.ReservationId, cancellationToken))
        {
            if (!listed.IsUnappliedDeposit)
            {
                continue;
            }

            if (listed.Status is PaymentStatus.Pending or PaymentStatus.Authorized or PaymentStatus.Failed)
            {
                var pending = await workspace.LoadAsync(tenant, listed.Id.Value, cancellationToken);
                pending.Cancel(reason, clock);
                await workspace.SaveAsync(cancellationToken);
            }
            else if (listed.RefundableAmount.IsPositive)
            {
                await refund.HandleAsync(new RequestRefundCommand(listed.Id.Value, reason), cancellationToken);
            }
        }
    }
}

/// <summary>L'état des paiements d'un acompte, pour l'équipe et le client.</summary>
/// <param name="Settled">Encaissé et pas remboursé, déduit ou non.</param>
/// <param name="HasPending">Un paiement en ligne attend la réponse du prestataire.</param>
public sealed record DepositPaymentsDto(decimal Settled, bool HasPending, bool IsApplied, IReadOnlyList<DepositPaymentDto> Payments);

public sealed record DepositPaymentDto(
    Guid Id, string Provider, PaymentMethodType Method, PaymentStatus Status, decimal Amount, decimal CapturedAmount, decimal RefundedAmount,
    decimal RefundableAmount, Guid? AppliedToOrderId, DateTimeOffset CreatedAtUtc);

public sealed record GetDepositPaymentsQuery(Guid ReservationId) : IQuery<DepositPaymentsDto>;

public sealed class GetDepositPaymentsHandler(PaymentsWorkspace workspace) : IQueryHandler<GetDepositPaymentsQuery, DepositPaymentsDto>
{
    public async Task<DepositPaymentsDto> HandleAsync(GetDepositPaymentsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var payments = query.ReservationId == Guid.Empty
            ? []
            : await workspace.Payments.ListByReservationAsync(workspace.CurrentTenant, query.ReservationId, cancellationToken);
        return new DepositPaymentsDto(
            payments.Sum(payment => payment.SettledAmount.Amount),
            payments.Any(payment => payment.Status is PaymentStatus.Pending or PaymentStatus.Authorized),
            payments.Any(payment => payment.AppliedAtUtc is not null),
            payments.Select(payment => new DepositPaymentDto(
                payment.Id.Value, payment.Provider.Value, payment.Method, payment.Status, payment.Amount.Amount, payment.CapturedAmount.Amount,
                payment.RefundedAmount.Amount, payment.RefundableAmount.Amount, payment.AppliedAtUtc is null ? null : payment.OrderId.Value,
                payment.CreatedAtUtc)).ToList());
    }
}
