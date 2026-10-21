namespace Loyalty.Domain;

/// <summary>
/// Une écriture immuable du registre. Elle est écrite une fois par <see cref="LoyaltyAccount"/> et jamais modifiée :
/// une erreur se corrige par une nouvelle écriture (Adjust, Reversal), pas en modifiant ou supprimant celle-ci.
/// Elle porte son tenant et son compte pour pouvoir être stockée et lue seule, sans l'historique du compte.
/// </summary>
public sealed class LoyaltyTransaction : Entity<LoyaltyTransactionId>
{
    private LoyaltyTransaction(
        LoyaltyTransactionId id,
        TenantId tenantId,
        LoyaltyAccountId accountId,
        LoyaltyTransactionType type,
        LedgerDirection direction,
        Points points,
        TransactionReference? reference,
        string? reason,
        LoyaltyTransactionId? reversedTransactionId,
        DateTimeOffset occurredAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        AccountId = accountId;
        ReversedTransactionId = reversedTransactionId;
        Type = type;
        Direction = direction;
        Points = points;
        Reference = reference;
        Reason = reason;
        OccurredAtUtc = occurredAtUtc;
    }

    // Réservé à l'ORM.
    private LoyaltyTransaction() => Points = null!;

    public TenantId TenantId { get; }

    public LoyaltyAccountId AccountId { get; }

    public LoyaltyTransactionType Type { get; }

    public LedgerDirection Direction { get; }

    /// <summary>Toujours strictement positif ; voir <see cref="SignedPoints"/> pour l'effet sur le solde.</summary>
    public Points Points { get; }

    /// <summary>
    /// Origine de l'écriture (commande, utilisation de points...). Clé d'idempotence facultative pour les ajustements
    /// et expirations ; une contre-passation porte la référence de l'écriture qu'elle annule ; null pour une clôture.
    /// </summary>
    public TransactionReference? Reference { get; }

    /// <summary>Obligatoire pour les ajustements, contre-passations et clôtures, null sinon.</summary>
    public string? Reason { get; }

    /// <summary>Pour une <see cref="LoyaltyTransactionType.Reversal"/> seulement : l'écriture Earn ou Redeem annulée.</summary>
    public LoyaltyTransactionId? ReversedTransactionId { get; }

    public DateTimeOffset OccurredAtUtc { get; }

    public long SignedPoints => Direction == LedgerDirection.Credit ? Points.Value : -(long)Points.Value;

    /// <summary>
    /// La vue de cette écriture stockée dont un compte a besoin pour la contre-passer.
    /// <paramref name="isAlreadyReversed"/> = « une écriture Reversal pointe déjà sur celle-ci », lu dans le registre
    /// par <see cref="ILoyaltyLedgerLookup"/>.
    /// </summary>
    public PostedTransaction ToPostedReference(bool isAlreadyReversed) =>
        new(AccountId, Id, Type, Points, Reference, isAlreadyReversed);

    internal static LoyaltyTransaction Create(
        TenantId tenantId,
        LoyaltyAccountId accountId,
        LoyaltyTransactionType type,
        LedgerDirection direction,
        Points points,
        TransactionReference? reference,
        string? reason,
        DateTimeOffset occurredAtUtc,
        LoyaltyTransactionId? reversedTransactionId = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        DomainException.ThrowIf(points.IsZero, LoyaltyAccountErrors.PointsNotPositive);
        DomainException.ThrowIf(!Enum.IsDefined(direction), LoyaltyAccountErrors.DirectionInvalid);

        return new LoyaltyTransaction(
            LoyaltyTransactionId.New(), tenantId, accountId, type, direction, points, reference, reason, reversedTransactionId, occurredAtUtc);
    }
}
