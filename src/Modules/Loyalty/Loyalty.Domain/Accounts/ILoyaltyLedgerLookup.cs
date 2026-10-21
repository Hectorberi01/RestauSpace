namespace Loyalty.Domain;

/// <summary>
/// Port sur le registre stocké. Le compte ne charge jamais son historique : les deux questions qui en ont besoin sont
/// posées ici et les réponses transmises à l'agrégat. Les index uniques
/// (AccountId, Type, Reference) et (AccountId, ReversedTransactionId) restent les garde-fous ultimes.
/// </summary>
public interface ILoyaltyLedgerLookup
{
    /// <summary>
    /// « Existe-t-il déjà sur ce compte une écriture de ce type portant cette référence ? » — l'argument
    /// <c>referenceAlreadyPosted</c> de Earn, Redeem, RedeemReward, Adjust et ExpirePoints.
    /// </summary>
    Task<bool> IsReferencePostedAsync(
        TenantId tenantId,
        LoyaltyAccountId accountId,
        LoyaltyTransactionType type,
        TransactionReference reference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// L'écriture Earn ou Redeem portant cette référence, en indiquant si une contre-passation pointe déjà dessus
    /// (<see cref="LoyaltyTransaction.ToPostedReference"/>) ; null s'il n'y en a pas.
    /// </summary>
    Task<PostedTransaction?> FindPostedAsync(
        TenantId tenantId,
        LoyaltyAccountId accountId,
        LoyaltyTransactionType type,
        TransactionReference reference,
        CancellationToken cancellationToken = default);
}
