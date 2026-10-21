using Loyalty.Application.Abstractions;
using Loyalty.Domain;

namespace Loyalty.Application.Commands;

// Le compte de fidélité d'un client, vu par le personnel. Les règles (solde, découvert, statut, idempotence) sont celles
// de l'agrégat ; ici, d'où viennent les réponses du registre et l'ordre des étapes.

/// <summary>Ouvre le compte d'un client du restaurant (un seul par client). Renvoie l'id du compte.</summary>
public sealed record OpenLoyaltyAccountCommand(Guid CustomerId) : ICommand<Guid>;

public sealed class OpenLoyaltyAccountHandler(LoyaltyWorkspace workspace, ILoyaltyCustomers customers, IClock clock)
    : ICommandHandler<OpenLoyaltyAccountCommand, Guid>
{
    public async Task<Guid> HandleAsync(OpenLoyaltyAccountCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var customer = (command.CustomerId == Guid.Empty ? null : await customers.FindAsync(tenant, CustomerId.From(command.CustomerId), cancellationToken))
            ?? throw new NotFoundException(LoyaltyApplicationErrors.CustomerNotFound);

        var account = await LoyaltyAccountOpening.OpenAsync(workspace, customer, clock, cancellationToken);
        await workspace.SaveAsync(cancellationToken);
        return account.Id.Value;
    }
}

/// <summary>Ouverture partagée par le personnel et l'inscription automatique à la première commande.</summary>
internal static class LoyaltyAccountOpening
{
    public static async Task<LoyaltyAccount> OpenAsync(
        LoyaltyWorkspace workspace, CustomerReference customer, IClock clock, CancellationToken cancellationToken)
    {
        var tenant = workspace.CurrentTenant;
        var isFirst = await workspace.Uniqueness.IsUniqueAsync(tenant, customer.CustomerId, cancellationToken);
        var account = LoyaltyAccount.Open(tenant, customer, isFirst, clock);
        workspace.Accounts.Add(account);
        return account;
    }
}

/// <summary>
/// Correction manuelle d'un solde (geste commercial, reprise après fraude). Motif obligatoire. Avec
/// <paramref name="Reference"/>, l'ajustement est posté une seule fois (rejeu sans risque) ; un second envoi est refusé
/// (<c>duplicate_transaction</c>). Un débit ne peut pas rendre le solde négatif.
/// </summary>
public sealed record AdjustLoyaltyPointsCommand(Guid AccountId, int Points, LedgerDirection Direction, string? Reason, string? Reference = null)
    : ICommand;

public sealed class AdjustLoyaltyPointsHandler(LoyaltyWorkspace workspace, IClock clock) : ICommandHandler<AdjustLoyaltyPointsCommand>
{
    public Task HandleAsync(AdjustLoyaltyPointsCommand command, CancellationToken cancellationToken = default)
    {
        // Validés AVANT de charger : une saisie invalide n'ouvre pas de transaction.
        var points = Points.Of(command.Points);
        var reference = LoyaltyWorkspace.OptionalReference(command.Reference);
        return workspace.EditAccountAsync(command.AccountId, async account =>
        {
            if (reference is null)
            {
                account.Adjust(points, command.Direction, command.Reason ?? string.Empty, clock);
                return;
            }

            var posted = await workspace.Ledger.IsReferencePostedAsync(
                account.TenantId, account.Id, LoyaltyTransactionType.Adjust, reference, cancellationToken);
            account.Adjust(points, command.Direction, command.Reason ?? string.Empty, reference, posted, clock);
        }, cancellationToken);
    }
}

public sealed record SuspendLoyaltyAccountCommand(Guid AccountId) : ICommand;

public sealed class SuspendLoyaltyAccountHandler(LoyaltyWorkspace workspace, IClock clock) : ICommandHandler<SuspendLoyaltyAccountCommand>
{
    public Task HandleAsync(SuspendLoyaltyAccountCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditAccountAsync(command.AccountId, account => account.Suspend(clock), cancellationToken);
}

public sealed record ReactivateLoyaltyAccountCommand(Guid AccountId) : ICommand;

public sealed class ReactivateLoyaltyAccountHandler(LoyaltyWorkspace workspace, IClock clock) : ICommandHandler<ReactivateLoyaltyAccountCommand>
{
    public Task HandleAsync(ReactivateLoyaltyAccountCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditAccountAsync(command.AccountId, account => account.Reactivate(clock), cancellationToken);
}

/// <summary>Clôture définitive : le solde restant est perdu (écriture de clôture au registre). Motif obligatoire.</summary>
public sealed record CloseLoyaltyAccountCommand(Guid AccountId, string? Reason) : ICommand;

public sealed class CloseLoyaltyAccountHandler(LoyaltyWorkspace workspace, IClock clock) : ICommandHandler<CloseLoyaltyAccountCommand>
{
    public Task HandleAsync(CloseLoyaltyAccountCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditAccountAsync(command.AccountId, account => account.Close(command.Reason ?? string.Empty, clock), cancellationToken);
}

/// <summary>Remise accordée par une récompense : ce que la commande recevra (par l'outbox, voir l'hôte).</summary>
public sealed record RewardRedemptionDto(Guid AccountId, Guid RewardId, Guid OrderId, int Points, decimal DiscountAmount, string CurrencyCode, long BalanceAfter);

/// <summary>
/// Au comptoir : le client utilise une récompense sur sa commande en cours. Vérifie que la commande est la sienne,
/// qu'elle accepte encore une remise et qu'elle est dans la devise de la récompense ; débite le coût en points
/// (référence = id de la commande : une seule récompense par commande, annulée avec elle). La remise est ensuite
/// posée sur la commande par l'hôte (<c>RewardRedeemed</c>, outbox) ; si elle ne peut plus l'être, les points sont rendus.
/// </summary>
public sealed record RedeemRewardForOrderCommand(Guid AccountId, Guid RewardId, Guid OrderId) : ICommand<RewardRedemptionDto>;

public sealed class RedeemRewardForOrderHandler(LoyaltyWorkspace workspace, ILoyaltyOrders orders, IClock clock)
    : ICommandHandler<RedeemRewardForOrderCommand, RewardRedemptionDto>
{
    public async Task<RewardRedemptionDto> HandleAsync(RedeemRewardForOrderCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var account = await workspace.LoadAccountAsync(command.AccountId, cancellationToken);
        var reward = await workspace.LoadRewardAsync(command.RewardId, cancellationToken);
        var order = (command.OrderId == Guid.Empty ? null : await orders.FindAsync(tenant, OrderId.From(command.OrderId), cancellationToken))
            ?? throw new NotFoundException(LoyaltyApplicationErrors.OrderNotFound);

        DomainException.ThrowIf(order.CustomerId != account.CustomerId, LoyaltyApplicationErrors.OrderOfAnotherCustomer);
        DomainException.ThrowIf(!order.AcceptsDiscounts, LoyaltyApplicationErrors.OrderNotEditable);
        DomainException.ThrowIf(
            !string.Equals(reward.DiscountValue.Currency.Code, order.CurrencyCode, StringComparison.OrdinalIgnoreCase),
            LoyaltyApplicationErrors.RewardCurrencyMismatch);

        var reference = LoyaltyWorkspace.OrderReference(command.OrderId);
        var posted = await workspace.Ledger.IsReferencePostedAsync(tenant, account.Id, LoyaltyTransactionType.Redeem, reference, cancellationToken);
        account.RedeemReward(reward.ToReference(), reference, posted, clock);
        await workspace.SaveAsync(cancellationToken);

        return new RewardRedemptionDto(
            account.Id.Value, reward.Id.Value, command.OrderId, reward.PointCost.Value, reward.DiscountValue.Amount,
            reward.DiscountValue.Currency.Code, account.Balance);
    }
}
