using Loyalty.Domain;

namespace Loyalty.Infrastructure.Persistence;

/// <summary>
/// Implémentation de <see cref="ILoyaltyLedgerLookup"/> et de <see cref="ILoyaltyAccountUniquenessChecker"/> sur les
/// tables du module. Les écritures ajoutées dans la même unité de travail (pas encore écrites) comptent aussi : deux
/// postages de la même référence dans une requête sont refusés par le domaine, pas seulement par l'index unique.
/// </summary>
internal sealed class LoyaltyLedgerLookup(LoyaltyDbContext context) : ILoyaltyLedgerLookup, ILoyaltyAccountUniquenessChecker
{
    public async Task<bool> IsReferencePostedAsync(
        TenantId tenantId,
        LoyaltyAccountId accountId,
        LoyaltyTransactionType type,
        TransactionReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (Pending(tenantId, accountId).Any(entry => entry.Type == type && reference.Equals(entry.Reference)))
        {
            return true;
        }

        return await context.Transactions.AnyAsync(
            entry => entry.TenantId == tenantId && entry.AccountId == accountId && entry.Type == type && entry.Reference == reference,
            cancellationToken);
    }

    public async Task<PostedTransaction?> FindPostedAsync(
        TenantId tenantId,
        LoyaltyAccountId accountId,
        LoyaltyTransactionType type,
        TransactionReference reference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var original = Pending(tenantId, accountId).FirstOrDefault(entry => entry.Type == type && reference.Equals(entry.Reference))
            ?? await context.Transactions.AsNoTracking().FirstOrDefaultAsync(
                entry => entry.TenantId == tenantId && entry.AccountId == accountId && entry.Type == type && entry.Reference == reference,
                cancellationToken);
        if (original is null)
        {
            return null;
        }

        var originalId = original.Id;
        var isAlreadyReversed = Pending(tenantId, accountId).Any(entry => entry.ReversedTransactionId == originalId)
            || await context.Transactions.AnyAsync(
                entry => entry.TenantId == tenantId && entry.ReversedTransactionId == originalId, cancellationToken);
        return original.ToPostedReference(isAlreadyReversed);
    }

    public async Task<bool> IsUniqueAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
        !context.Accounts.Local.Any(account => account.TenantId == tenantId && account.CustomerId == customerId)
        && !await context.Accounts.AnyAsync(account => account.TenantId == tenantId && account.CustomerId == customerId, cancellationToken);

    /// <summary>Écritures ajoutées et pas encore écrites pour ce compte.</summary>
    private IEnumerable<LoyaltyTransaction> Pending(TenantId tenantId, LoyaltyAccountId accountId) =>
        context.ChangeTracker.Entries<LoyaltyTransaction>()
            .Where(entry => entry.State == EntityState.Added)
            .Select(entry => entry.Entity)
            .Where(entry => entry.TenantId == tenantId && entry.AccountId == accountId);
}
