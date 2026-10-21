namespace Loyalty.Domain;

/// <summary>
/// Une écriture du grand livre déjà stockée, telle que le compte doit la voir pour l'annuler. Le compte ne charge pas
/// son historique : l'Application lit l'écriture via <see cref="ILoyaltyLedgerLookup"/>. Obtenable uniquement depuis
/// une vraie écriture via <see cref="LoyaltyTransaction.ToPostedReference"/> : points, type et propriétaire ne
/// peuvent donc pas être falsifiés.
/// </summary>
public sealed record PostedTransaction
{
    internal PostedTransaction(
        LoyaltyAccountId accountId,
        LoyaltyTransactionId transactionId,
        LoyaltyTransactionType type,
        Points points,
        TransactionReference? reference,
        bool isAlreadyReversed)
    {
        AccountId = accountId;
        TransactionId = transactionId;
        Type = type;
        Points = points;
        Reference = reference;
        IsAlreadyReversed = isAlreadyReversed;
    }

    public LoyaltyAccountId AccountId { get; }

    public LoyaltyTransactionId TransactionId { get; }

    public LoyaltyTransactionType Type { get; }

    public Points Points { get; }

    public TransactionReference? Reference { get; }

    /// <summary>True quand une écriture <see cref="LoyaltyTransactionType.Reversal"/> pointe déjà vers
    /// celle-ci.</summary>
    public bool IsAlreadyReversed { get; }
}
