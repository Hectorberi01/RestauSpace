using System.Reflection;
using Loyalty.Domain;

namespace Loyalty.Domain;

/// <summary>
/// Remplace l'Infrastructure dans les tests : la table du registre (insertion seule) derrière
/// <see cref="ILoyaltyLedgerLookup"/>. Elle ne reçoit que les écritures ajoutées par un compte, exactement comme la
/// vraie persistance.
/// </summary>
internal sealed class InMemoryLoyaltyLedger : ILoyaltyLedgerLookup
{
    private readonly List<LoyaltyTransaction> _rows = [];

    public IReadOnlyList<LoyaltyTransaction> Rows => _rows;

    /// <summary>Insère les écritures ajoutées depuis la dernière sauvegarde.</summary>
    public void Save(LoyaltyAccount account) =>
        _rows.AddRange(account.Transactions.Where(entry => !_rows.Contains(entry)));

    /// <summary>Sauvegarde puis vide la collection en mémoire, comme un rechargement (historique non réhydraté).</summary>
    public void SaveAndReload(LoyaltyAccount account)
    {
        Save(account);
        var loaded = (List<LoyaltyTransaction>)typeof(LoyaltyAccount)
            .GetField("_transactions", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(account)!;
        loaded.Clear();
    }

    public bool IsPosted(LoyaltyAccount account, LoyaltyTransactionType type, TransactionReference reference) =>
        IsReferencePostedAsync(account.TenantId, account.Id, type, reference).GetAwaiter().GetResult();

    public PostedTransaction? Find(LoyaltyAccount account, LoyaltyTransactionType type, TransactionReference reference) =>
        FindPostedAsync(account.TenantId, account.Id, type, reference).GetAwaiter().GetResult();

    public Task<bool> IsReferencePostedAsync(
        TenantId tenantId,
        LoyaltyAccountId accountId,
        LoyaltyTransactionType type,
        TransactionReference reference,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Row(tenantId, accountId, type, reference) is not null);

    public Task<PostedTransaction?> FindPostedAsync(
        TenantId tenantId,
        LoyaltyAccountId accountId,
        LoyaltyTransactionType type,
        TransactionReference reference,
        CancellationToken cancellationToken = default)
    {
        var row = Row(tenantId, accountId, type, reference);
        return Task.FromResult(row?.ToPostedReference(_rows.Any(other => other.ReversedTransactionId == row.Id)));
    }

    private LoyaltyTransaction? Row(TenantId tenantId, LoyaltyAccountId accountId, LoyaltyTransactionType type, TransactionReference reference) =>
        _rows.FirstOrDefault(row =>
            row.TenantId == tenantId && row.AccountId == accountId && row.Type == type && row.Reference == reference);
}
