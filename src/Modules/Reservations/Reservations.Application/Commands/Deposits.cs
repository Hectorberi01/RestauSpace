using Reservations.Domain;

namespace Reservations.Application.Commands;

// Acompte de réservation. Le montant et le délai viennent de la règle de l'établissement, jamais de la requête ; l'argent
// est dans Payments, qui signale ce que les paiements de l'acompte détiennent (RecordDepositSettlement) : payé, la
// réservation est confirmée ; non payé à temps, elle est annulée (job) ; annulée à temps par le client, il est
// remboursé ; no-show, le restaurant le garde ; à l'arrivée, il est déduit de l'addition.

/// <summary>Ce que l'on montre de l'acompte d'une réservation (équipe et client).</summary>
public sealed record DepositDto(decimal Amount, string CurrencyCode, DepositStatus Status, DateTimeOffset? DueBeforeUtc, decimal Settled)
{
    public static DepositDto? From(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        return reservation.DepositAmount is { } amount
            ? new DepositDto(
                amount.Amount, amount.Currency.Code, reservation.DepositStatus!.Value, reservation.DepositDueBeforeUtc, reservation.DepositSettled?.Amount ?? 0m)
            : null;
    }
}

internal static class Deposits
{
    /// <summary>Demande l'acompte que la règle de l'établissement prévoit pour ce groupe ; vrai s'il y en a un.</summary>
    public static async Task<bool> RequireAsync(ReservationsWorkspace workspace, Reservation reservation, CancellationToken cancellationToken)
    {
        var rule = await workspace.Policies.GetDepositRuleAsync(reservation.TenantId, reservation.LocationId, cancellationToken);
        if (rule.AmountFor(reservation.PartySize) is not { } amount)
        {
            return false;
        }

        var now = workspace.Clock.UtcNow;
        // Jamais au-delà du début du repas : une réservation proche se paie avant d'arriver.
        var due = now + rule.PaymentWindow;
        if (due > reservation.StartAtUtc && reservation.StartAtUtc > now)
        {
            due = reservation.StartAtUtc;
        }

        reservation.RequireDeposit(amount, due, workspace.Clock);
        return true;
    }
}

/// <param name="Mode">Off, FromPartySize ou Always.</param>
/// <param name="Basis">PerGuest (par couvert) ou PerReservation.</param>
public sealed record DepositRuleDto(DepositMode Mode, DepositBasis Basis, decimal? Amount, string? CurrencyCode, int MinPartySize, int PaymentWindowMinutes)
{
    public static DepositRuleDto From(DepositRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return new DepositRuleDto(
            rule.Mode, rule.Basis, rule.Amount?.Amount, rule.Amount?.Currency.Code, rule.MinPartySize, (int)rule.PaymentWindow.TotalMinutes);
    }
}

public sealed record GetDepositRuleQuery(Guid LocationId) : IQuery<DepositRuleDto>;

public sealed class GetDepositRuleHandler(ReservationsWorkspace workspace) : IQueryHandler<GetDepositRuleQuery, DepositRuleDto>
{
    public async Task<DepositRuleDto> HandleAsync(GetDepositRuleQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, query.LocationId, cancellationToken);
        return DepositRuleDto.From(await workspace.Policies.GetDepositRuleAsync(tenant, location.LocationId, cancellationToken));
    }
}

/// <summary>Règle d'acompte d'un établissement, dans la devise du restaurant. Ne touche pas les réservations existantes.</summary>
public sealed record SetDepositRuleCommand(
    Guid LocationId, DepositMode Mode, DepositBasis Basis = DepositBasis.PerGuest, decimal? Amount = null, int MinPartySize = 1,
    int PaymentWindowMinutes = 30) : ICommand;

public sealed class SetDepositRuleHandler(ReservationsWorkspace workspace) : ICommandHandler<SetDepositRuleCommand>
{
    public async Task HandleAsync(SetDepositRuleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, command.LocationId, cancellationToken);
        var currency = await workspace.CurrencyAsync(tenant, cancellationToken);
        var window = TimeSpan.FromMinutes(Math.Clamp(command.PaymentWindowMinutes, 0, (int)DepositRule.MaxPaymentWindow.TotalMinutes + 1));
        var rule = DepositRule.Create(
            command.Mode, command.Basis, command.Amount is { } amount ? Money.Of(amount, currency) : null, command.MinPartySize, window);
        await workspace.Policies.SetDepositRuleAsync(tenant, location.LocationId, rule, cancellationToken);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>L'équipe renonce à l'acompte attendu (client connu, réservation par téléphone…).</summary>
public sealed record WaiveDepositCommand(Guid ReservationId) : ICommand;

public sealed class WaiveDepositHandler(ReservationsWorkspace workspace) : ICommandHandler<WaiveDepositCommand>
{
    public async Task HandleAsync(WaiveDepositCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var reservation = await workspace.LoadReservationAsync(workspace.CurrentTenant, command.ReservationId, cancellationToken);
        reservation.WaiveDeposit(workspace.Clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Rembourse un acompte gardé (no-show, annulation tardive) ou payé d'une réservation terminée : geste de l'équipe.</summary>
public sealed record RefundDepositCommand(Guid ReservationId) : ICommand;

public sealed class RefundDepositHandler(ReservationsWorkspace workspace) : ICommandHandler<RefundDepositCommand>
{
    public async Task HandleAsync(RefundDepositCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var reservation = await workspace.LoadReservationAsync(workspace.CurrentTenant, command.ReservationId, cancellationToken);
        reservation.RequestDepositRefund(workspace.Clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Pour l'hôte (Payments, par l'outbox) : ce que les paiements de l'acompte détiennent maintenant. Une réservation sans
/// acompte ou inconnue est ignorée. Possible même pour un restaurant suspendu : l'argent a bougé.
/// </summary>
public sealed record RecordDepositSettlementCommand(Guid ReservationId, decimal Settled, string CurrencyCode) : ICommand;

public sealed class RecordDepositSettlementHandler(ReservationsWorkspace workspace) : ICommandHandler<RecordDepositSettlementCommand>
{
    public async Task HandleAsync(RecordDepositSettlementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        var reservation = command.ReservationId == Guid.Empty
            ? null
            : await workspace.Reservations.FindAsync(tenant, ReservationId.From(command.ReservationId), cancellationToken);
        if (reservation is not { HasDeposit: true })
        {
            return;
        }

        reservation.RecordDepositSettlement(Money.Of(command.Settled, Currency.FromCode(command.CurrencyCode)), workspace.Clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Pour l'hôte : l'acompte a été déduit d'une addition (Payments). Idempotent.</summary>
public sealed record MarkDepositAppliedCommand(Guid ReservationId) : ICommand;

public sealed class MarkDepositAppliedHandler(ReservationsWorkspace workspace) : ICommandHandler<MarkDepositAppliedCommand>
{
    public async Task HandleAsync(MarkDepositAppliedCommand command, CancellationToken cancellationToken = default)
    {
        var reservation = await workspace.LoadReservationAsync(workspace.CurrentTenant, command.ReservationId, cancellationToken);
        if (!reservation.HasDeposit)
        {
            return;
        }

        reservation.MarkDepositApplied(workspace.Clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Pour le job : annule une réservation dont l'acompte n'a pas été payé à temps. Sans effet sinon.</summary>
public sealed record ExpireUnpaidDepositCommand(Guid ReservationId) : ICommand;

public sealed class ExpireUnpaidDepositHandler(ReservationsWorkspace workspace) : ICommandHandler<ExpireUnpaidDepositCommand>
{
    public async Task HandleAsync(ExpireUnpaidDepositCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var reservation = await workspace.LoadReservationAsync(tenant, command.ReservationId, cancellationToken);
        if (reservation.Status != ReservationStatus.Pending
            || reservation.DepositStatus != DepositStatus.AwaitingPayment
            || reservation.DepositDueBeforeUtc > workspace.Clock.UtcNow)
        {
            return;
        }

        var policy = await workspace.Policies.GetAsync(tenant, reservation.LocationId, cancellationToken);
        reservation.ExpireUnpaidDeposit(policy, workspace.Clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Ce que Payments doit savoir pour encaisser un acompte : jamais un montant venu de la requête.</summary>
/// <param name="AcceptsPayment">Un paiement est attendu : acompte en attente (ou dispensé) d'une réservation non terminée.</param>
public sealed record ReservationDepositDue(Guid ReservationId, Guid LocationId, decimal Amount, string CurrencyCode, bool AcceptsPayment, DepositStatus Status);

public sealed record GetReservationDepositQuery(Guid ReservationId) : IQuery<ReservationDepositDue?>;

public sealed class GetReservationDepositHandler(ReservationsWorkspace workspace) : IQueryHandler<GetReservationDepositQuery, ReservationDepositDue?>
{
    public async Task<ReservationDepositDue?> HandleAsync(GetReservationDepositQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var reservation = query.ReservationId == Guid.Empty
            ? null
            : await workspace.Reservations.FindAsync(workspace.CurrentTenant, ReservationId.From(query.ReservationId), cancellationToken);
        if (reservation?.DepositAmount is not { } amount)
        {
            return null;
        }

        var accepts = reservation.DepositStatus is DepositStatus.AwaitingPayment or DepositStatus.Waived
            && reservation.Status is ReservationStatus.Pending or ReservationStatus.Confirmed or ReservationStatus.Seated;
        return new ReservationDepositDue(
            reservation.Id.Value, reservation.LocationId.Value, amount.Amount, amount.Currency.Code, accepts, reservation.DepositStatus!.Value);
    }
}
