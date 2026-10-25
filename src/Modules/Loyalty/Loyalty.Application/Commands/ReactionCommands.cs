using Loyalty.Application.Abstractions;
using Loyalty.Domain;

namespace Loyalty.Application.Commands;

// Réactions aux faits des autres modules, déclenchées par l'hôte (outbox, au moins une fois, pour le tenant du message).
// Toutes sont IDEMPOTENTES : un message rejoué ne change rien de plus, et ce qui n'a pas lieu d'être (pas de client,
// programme désactivé, compte clos) est ignoré sans erreur pour ne pas bloquer l'outbox de l'autre module.

/// <summary>Ce qu'a donné une réaction : rien, ou ce qui a été écrit au registre.</summary>
public enum LoyaltyReactionOutcome
{
    Ignored = 1,
    AlreadyDone = 2,
    Done = 3,
}

/// <summary>
/// orders.OrderCompleted : la commande d'un client rapporte des points selon le barème du restaurant (référence = id de
/// la commande, une seule fois). Sans compte, il en est ouvert un si le barème le prévoit (et que le client n'est pas
/// anonymisé). Un compte suspendu ou clos ne gagne rien, ni un restaurant dont le plan n'a plus la fidélité.
/// </summary>
public sealed record EarnPointsForOrderCommand(Guid OrderId, Guid? CustomerId, decimal Total, string CurrencyCode) : ICommand<LoyaltyReactionOutcome>;

public sealed class EarnPointsForOrderHandler(LoyaltyWorkspace workspace, ILoyaltyCustomers customers, IClock clock)
    : ICommandHandler<EarnPointsForOrderCommand, LoyaltyReactionOutcome>
{
    public async Task<LoyaltyReactionOutcome> HandleAsync(EarnPointsForOrderCommand command, CancellationToken cancellationToken = default)
    {
        if (command.CustomerId is not { } customerGuid || customerGuid == Guid.Empty || !await workspace.AcceptsWritesAsync(cancellationToken)
            || !await workspace.AllowsAsync(PlanFeatures.Loyalty, cancellationToken))
        {
            return LoyaltyReactionOutcome.Ignored;
        }

        var tenant = workspace.CurrentTenant;
        var program = await workspace.Program.GetAsync(tenant, cancellationToken);
        var points = program.PointsFor(Money.Of(command.Total, Currency.FromCode(command.CurrencyCode)));
        if (points.IsZero)
        {
            return LoyaltyReactionOutcome.Ignored;
        }

        var customerId = CustomerId.From(customerGuid);
        var account = await workspace.Accounts.FindByCustomerAsync(tenant, customerId, cancellationToken);
        if (account is null)
        {
            if (!program.AutoEnroll || await customers.FindAsync(tenant, customerId, cancellationToken) is not { IsAnonymized: false } customer)
            {
                return LoyaltyReactionOutcome.Ignored;
            }

            account = await LoyaltyAccountOpening.OpenAsync(workspace, customer, clock, cancellationToken);
        }

        if (account.Status != LoyaltyAccountStatus.Active)
        {
            return LoyaltyReactionOutcome.Ignored;
        }

        var reference = LoyaltyWorkspace.OrderReference(command.OrderId);
        if (await workspace.Ledger.IsReferencePostedAsync(tenant, account.Id, LoyaltyTransactionType.Earn, reference, cancellationToken))
        {
            return LoyaltyReactionOutcome.AlreadyDone;
        }

        account.Earn(points, reference, referenceAlreadyPosted: false, clock);
        await workspace.SaveAsync(cancellationToken);
        return LoyaltyReactionOutcome.Done;
    }
}

/// <summary>
/// orders.OrderCancelled : reprend les points gagnés par la commande et rend ceux d'une récompense utilisée dessus.
/// Ce qui n'existe pas ou a déjà été annulé est ignoré ; un compte clos n'est plus modifié. La reprise peut rendre le
/// solde négatif (points déjà dépensés) : c'est voulu par le domaine.
/// </summary>
public sealed record ReverseOrderLoyaltyCommand(Guid OrderId, Guid? CustomerId) : ICommand<LoyaltyReactionOutcome>;

public sealed class ReverseOrderLoyaltyHandler(LoyaltyWorkspace workspace, IClock clock)
    : ICommandHandler<ReverseOrderLoyaltyCommand, LoyaltyReactionOutcome>
{
    public async Task<LoyaltyReactionOutcome> HandleAsync(ReverseOrderLoyaltyCommand command, CancellationToken cancellationToken = default)
    {
        if (command.CustomerId is not { } customerGuid || customerGuid == Guid.Empty)
        {
            return LoyaltyReactionOutcome.Ignored;
        }

        var tenant = workspace.CurrentTenant;
        var account = await workspace.Accounts.FindByCustomerAsync(tenant, CustomerId.From(customerGuid), cancellationToken);
        if (account is null || account.Status == LoyaltyAccountStatus.Closed)
        {
            return LoyaltyReactionOutcome.Ignored;
        }

        return await LoyaltyReversal.ReverseAsync(workspace, account, command.OrderId, reverseEarn: true, LoyaltyReasons.OrderCancelled, clock, cancellationToken);
    }
}

/// <summary>
/// Compensation : la remise d'une récompense n'a pas pu être posée sur la commande (elle n'accepte plus de remise) ; les
/// points de l'échange sont rendus. Rejouable.
/// </summary>
public sealed record CancelRewardRedemptionCommand(Guid AccountId, Guid OrderId) : ICommand<LoyaltyReactionOutcome>;

public sealed class CancelRewardRedemptionHandler(LoyaltyWorkspace workspace, IClock clock)
    : ICommandHandler<CancelRewardRedemptionCommand, LoyaltyReactionOutcome>
{
    public async Task<LoyaltyReactionOutcome> HandleAsync(CancelRewardRedemptionCommand command, CancellationToken cancellationToken = default)
    {
        var account = command.AccountId == Guid.Empty
            ? null
            : await workspace.Accounts.FindAsync(workspace.CurrentTenant, LoyaltyAccountId.From(command.AccountId), cancellationToken);
        if (account is null || account.Status == LoyaltyAccountStatus.Closed)
        {
            return LoyaltyReactionOutcome.Ignored;
        }

        return await LoyaltyReversal.ReverseAsync(
            workspace, account, command.OrderId, reverseEarn: false, LoyaltyReasons.DiscountNotApplied, clock, cancellationToken);
    }
}

internal static class LoyaltyReversal
{
    public static async Task<LoyaltyReactionOutcome> ReverseAsync(
        LoyaltyWorkspace workspace,
        LoyaltyAccount account,
        Guid orderId,
        bool reverseEarn,
        string reason,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var reference = LoyaltyWorkspace.OrderReference(orderId);
        var changed = false;
        var found = false;

        if (reverseEarn)
        {
            var earned = await workspace.Ledger.FindPostedAsync(account.TenantId, account.Id, LoyaltyTransactionType.Earn, reference, cancellationToken);
            found |= earned is not null;
            if (earned is { IsAlreadyReversed: false })
            {
                account.ReverseEarn(earned, reason, clock);
                changed = true;
            }
        }

        var redeemed = await workspace.Ledger.FindPostedAsync(account.TenantId, account.Id, LoyaltyTransactionType.Redeem, reference, cancellationToken);
        found |= redeemed is not null;
        if (redeemed is { IsAlreadyReversed: false })
        {
            account.ReverseRedeem(redeemed, reason, clock);
            changed = true;
        }

        if (!changed)
        {
            return found ? LoyaltyReactionOutcome.AlreadyDone : LoyaltyReactionOutcome.Ignored;
        }

        await workspace.SaveAsync(cancellationToken);
        return LoyaltyReactionOutcome.Done;
    }
}

/// <summary>customers.CustomerAnonymized : le compte du client est clos (solde perdu), rien de personnel n'y reste.</summary>
public sealed record CloseCustomerLoyaltyAccountCommand(Guid CustomerId) : ICommand<LoyaltyReactionOutcome>;

public sealed class CloseCustomerLoyaltyAccountHandler(LoyaltyWorkspace workspace, IClock clock)
    : ICommandHandler<CloseCustomerLoyaltyAccountCommand, LoyaltyReactionOutcome>
{
    public async Task<LoyaltyReactionOutcome> HandleAsync(CloseCustomerLoyaltyAccountCommand command, CancellationToken cancellationToken = default)
    {
        var account = command.CustomerId == Guid.Empty
            ? null
            : await workspace.Accounts.FindByCustomerAsync(workspace.CurrentTenant, CustomerId.From(command.CustomerId), cancellationToken);
        if (account is null)
        {
            return LoyaltyReactionOutcome.Ignored;
        }

        if (account.Status == LoyaltyAccountStatus.Closed)
        {
            return LoyaltyReactionOutcome.AlreadyDone;
        }

        account.Close(LoyaltyReasons.CustomerAnonymized, clock);
        await workspace.SaveAsync(cancellationToken);
        return LoyaltyReactionOutcome.Done;
    }
}

/// <summary>
/// Expiration des points (job, pour le tenant du contexte) : le solde d'un compte sans gain ni échange depuis le délai du
/// barème expire en entier. Référence = date de la dernière activité : rejouer le job ne fait rien de plus, et une
/// nouvelle activité ouvre une nouvelle période. Au plus <paramref name="BatchSize"/> comptes par passage ; renvoie le
/// nombre de comptes expirés.
/// </summary>
public sealed record ExpireInactivePointsCommand(int BatchSize = 200) : ICommand<int>;

public sealed class ExpireInactivePointsHandler(LoyaltyWorkspace workspace, ILoyaltyExpiryCandidates candidates, IClock clock)
    : ICommandHandler<ExpireInactivePointsCommand, int>
{
    public async Task<int> HandleAsync(ExpireInactivePointsCommand command, CancellationToken cancellationToken = default)
    {
        if (!await workspace.AcceptsWritesAsync(cancellationToken))
        {
            return 0;
        }

        var tenant = workspace.CurrentTenant;
        var cutoff = (await workspace.Program.GetAsync(tenant, cancellationToken)).InactivityCutoff(clock.UtcNow);
        if (cutoff is not { } before)
        {
            return 0;
        }

        var expired = 0;
        foreach (var candidate in await candidates.ListAsync(tenant, before, Math.Clamp(command.BatchSize, 1, 1000), cancellationToken))
        {
            var account = await workspace.Accounts.FindAsync(tenant, candidate.AccountId, cancellationToken);
            if (account is null || account.Status == LoyaltyAccountStatus.Closed || account.Balance <= 0)
            {
                continue;
            }

            var reference = ExpiryReference(candidate.LastActivityUtc);
            if (await workspace.Ledger.IsReferencePostedAsync(tenant, account.Id, LoyaltyTransactionType.Expire, reference, cancellationToken))
            {
                continue;
            }

            account.ExpirePoints(Points.Of((int)Math.Min(account.Balance, int.MaxValue)), reference, referenceAlreadyPosted: false, clock);
            expired++;
        }

        if (expired > 0)
        {
            await workspace.SaveAsync(cancellationToken);
        }

        return expired;
    }

    /// <summary>« expiry: » + instant de la dernière activité : une expiration par période d'inactivité.</summary>
    public static TransactionReference ExpiryReference(DateTimeOffset lastActivityUtc) =>
        TransactionReference.Create("expiry:" + lastActivityUtc.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

/// <summary>
/// customers.CustomerMerged : le solde du doublon passe sur le compte de la fiche qui l'absorbe (ouvert au besoin), puis
/// le compte du doublon est clos. Rejouable : un compte de doublon déjà clos ne fait plus rien.
/// </summary>
public sealed record MergeCustomerLoyaltyAccountsCommand(Guid FromCustomerId, Guid ToCustomerId) : ICommand<LoyaltyReactionOutcome>;

public sealed class MergeCustomerLoyaltyAccountsHandler(LoyaltyWorkspace workspace, ILoyaltyCustomers customers, IClock clock)
    : ICommandHandler<MergeCustomerLoyaltyAccountsCommand, LoyaltyReactionOutcome>
{
    public async Task<LoyaltyReactionOutcome> HandleAsync(MergeCustomerLoyaltyAccountsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.FromCustomerId == Guid.Empty || command.ToCustomerId == Guid.Empty || command.FromCustomerId == command.ToCustomerId)
        {
            return LoyaltyReactionOutcome.Ignored;
        }

        var tenant = workspace.CurrentTenant;
        var duplicate = await workspace.Accounts.FindByCustomerAsync(tenant, CustomerId.From(command.FromCustomerId), cancellationToken);
        if (duplicate is null)
        {
            return LoyaltyReactionOutcome.Ignored;
        }

        if (duplicate.Status == LoyaltyAccountStatus.Closed)
        {
            return LoyaltyReactionOutcome.AlreadyDone;
        }

        var balance = duplicate.Balance;
        if (balance > 0)
        {
            var target = await workspace.Accounts.FindByCustomerAsync(tenant, CustomerId.From(command.ToCustomerId), cancellationToken);
            if (target is null)
            {
                var customer = await customers.FindAsync(tenant, CustomerId.From(command.ToCustomerId), cancellationToken)
                    ?? throw new NotFoundException(LoyaltyApplicationErrors.CustomerNotFound);
                target = await LoyaltyAccountOpening.OpenAsync(workspace, customer, clock, cancellationToken);
            }

            if (target.Status != LoyaltyAccountStatus.Closed)
            {
                target.Adjust(Points.Of((int)Math.Min(balance, int.MaxValue)), LedgerDirection.Credit, LoyaltyReasons.CustomerMerged, clock);
            }
        }

        duplicate.Close(LoyaltyReasons.CustomerMerged, clock);
        await workspace.SaveAsync(cancellationToken);
        return LoyaltyReactionOutcome.Done;
    }
}
