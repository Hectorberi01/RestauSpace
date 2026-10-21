namespace Loyalty.Domain;

/// <summary>
/// Compte de points d'un client. Le registre fait foi et est en ajout seul ; <see cref="Balance"/> en est la
/// projection stockée, modifiée exactement par les écritures que cet agrégat ajoute. Le compte n'a jamais besoin de
/// son historique en mémoire : ce qu'il faut savoir des écritures stockées (référence en double, écriture à
/// contrepasser) est lu via <see cref="ILoyaltyLedgerLookup"/> et transmis aux méthodes.
/// </summary>
public sealed class LoyaltyAccount : AggregateRoot<LoyaltyAccountId>, ITenantOwned
{
    public const int MaxReasonLength = 250;

    private readonly List<LoyaltyTransaction> _transactions = [];

    private LoyaltyAccount(LoyaltyAccountId id, TenantId tenantId, CustomerId customerId, DateTimeOffset openedAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        CustomerId = customerId;
        Status = LoyaltyAccountStatus.Active;
        OpenedAtUtc = openedAtUtc;
    }

    // Réservé à l'ORM.
    private LoyaltyAccount()
    {
    }

    public TenantId TenantId { get; }

    public CustomerId CustomerId { get; private set; }

    /// <summary>Signé : seule une <see cref="OverdraftPolicy"/> explicite peut le faire passer sous zéro.</summary>
    public long Balance { get; private set; }

    public LoyaltyAccountStatus Status { get; private set; }

    public DateTimeOffset OpenedAtUtc { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    /// <summary>
    /// Les écritures ajoutées depuis le chargement de cet agrégat — PAS l'historique du compte, qui n'est jamais
    /// réhydraté. L'Infrastructure les insère ; aucune règle ne relit cette collection.
    /// </summary>
    public IReadOnlyCollection<LoyaltyTransaction> Transactions => _transactions.AsReadOnly();

    public static LoyaltyAccount Open(TenantId tenantId, CustomerReference customer, bool isFirstAccountForCustomer, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);

        var now = clock.UtcNow;
        var account = new LoyaltyAccount(LoyaltyAccountId.New(), tenantId, customer.CustomerId, now);
        account.EnsureSameTenant(customer.TenantId);
        DomainException.ThrowIf(customer.IsAnonymized, LoyaltyAccountErrors.CustomerAnonymized);
        DomainException.ThrowIf(!isFirstAccountForCustomer, LoyaltyAccountErrors.AlreadyExists);

        account.Raise(new LoyaltyAccountOpened(tenantId, account.Id, customer.CustomerId, now));
        return account;
    }

    /// <summary><paramref name="referenceAlreadyPosted"/> provient de <see cref="ILoyaltyLedgerLookup"/> (type Earn).</summary>
    public void Earn(Points points, TransactionReference reference, bool referenceAlreadyPosted, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureActive();

        var now = clock.UtcNow;
        var entry = NewEntry(LoyaltyTransactionType.Earn, LedgerDirection.Credit, points, reference, null, now);
        EnsureNotPosted(referenceAlreadyPosted, LoyaltyTransactionType.Earn, reference);

        Post(entry);
        Raise(new PointsEarned(TenantId, Id, CustomerId, entry.Id, points, reference, Balance, now));
    }

    /// <summary><paramref name="referenceAlreadyPosted"/> provient de <see cref="ILoyaltyLedgerLookup"/> (type Redeem).</summary>
    public void Redeem(
        Points points,
        TransactionReference reference,
        bool referenceAlreadyPosted,
        IClock clock,
        OverdraftPolicy? overdraftPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Redeem(points, reference, referenceAlreadyPosted, null, clock.UtcNow, overdraftPolicy);
    }

    /// <summary>
    /// Échange le coût d'une récompense. La référence identifie l'échange, pas la récompense.
    /// <see cref="RewardRedeemed"/> porte la remise que l'Application applique à la commande.
    /// <paramref name="referenceAlreadyPosted"/> provient de <see cref="ILoyaltyLedgerLookup"/> (type Redeem).
    /// </summary>
    public void RedeemReward(
        RewardReference reward,
        TransactionReference reference,
        bool referenceAlreadyPosted,
        IClock clock,
        OverdraftPolicy? overdraftPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(reward);
        ArgumentNullException.ThrowIfNull(clock);
        this.EnsureSameTenant(reward.TenantId);
        EnsureNotClosed();
        DomainException.ThrowIf(!reward.IsActive, LoyaltyAccountErrors.RewardInactive);

        var now = clock.UtcNow;
        var entry = Redeem(reward.PointCost, reference, referenceAlreadyPosted, reward.RewardId, now, overdraftPolicy);
        Raise(new RewardRedeemed(
            TenantId, Id, CustomerId, entry.Id, reward.RewardId, reward.PointCost, reward.DiscountValue,
            reward.DiscountReference, reference, Balance, now));
    }

    /// <summary>
    /// Reprend les points d'une écriture Earn stockée (commande annulée ou remboursée). <paramref name="original"/>
    /// provient de <see cref="ILoyaltyLedgerLookup.FindPostedAsync"/> ; null signifie « écriture inexistante ».
    /// Comme une expiration, ce n'est pas une action du client : elle est permise sur un compte suspendu et c'est le
    /// seul débit qui ignore la règle de découvert — le solde peut devenir négatif si les points ont déjà été dépensés.
    /// </summary>
    public void ReverseEarn(PostedTransaction? original, string reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.UtcNow;
        var entry = Reverse(LoyaltyTransactionType.Earn, LedgerDirection.Debit, original, reason, now);
        Raise(new PointsEarnReversed(
            TenantId, Id, CustomerId, entry.Id, entry.ReversedTransactionId!.Value, entry.Points, entry.Reference!,
            entry.Reason!, Balance, now));
    }

    /// <summary>
    /// Restitue les points d'une écriture Redeem stockée (commande annulée avant usage). <paramref name="original"/>
    /// provient de <see cref="ILoyaltyLedgerLookup.FindPostedAsync"/> ; null signifie « écriture inexistante ».
    /// </summary>
    public void ReverseRedeem(PostedTransaction? original, string reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.UtcNow;
        var entry = Reverse(LoyaltyTransactionType.Redeem, LedgerDirection.Credit, original, reason, now);
        Raise(new PointsRedeemReversed(
            TenantId, Id, CustomerId, entry.Id, entry.ReversedTransactionId!.Value, entry.Points, entry.Reference!,
            entry.Reason!, Balance, now));
    }

    /// <summary>
    /// Correction manuelle sans clé d'idempotence. Permise sur un compte suspendu (usage typique : reprendre des
    /// points lors d'une enquête pour fraude).
    /// </summary>
    public void Adjust(
        Points points,
        LedgerDirection direction,
        string reason,
        IClock clock,
        OverdraftPolicy? overdraftPolicy = null) =>
        PostAdjustment(points, direction, reason, null, false, clock, overdraftPolicy);

    /// <summary>
    /// Correction manuelle rejouable sans risque : <paramref name="referenceAlreadyPosted"/> provient de
    /// <see cref="ILoyaltyLedgerLookup"/> (type Adjust).
    /// </summary>
    public void Adjust(
        Points points,
        LedgerDirection direction,
        string reason,
        TransactionReference reference,
        bool referenceAlreadyPosted,
        IClock clock,
        OverdraftPolicy? overdraftPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        PostAdjustment(points, direction, reason, reference, referenceAlreadyPosted, clock, overdraftPolicy);
    }

    /// <summary>
    /// Fait expirer jusqu'à <paramref name="points"/> points, plafonnés au solde courant, et renvoie ce qui a
    /// réellement expiré. Permis sur un compte suspendu : l'expiration est une règle temporelle, pas une action du client.
    /// </summary>
    public Points ExpirePoints(Points points, IClock clock) => PostExpiration(points, null, false, clock);

    /// <summary>
    /// Idem, rejouable : <paramref name="referenceAlreadyPosted"/> provient de <see cref="ILoyaltyLedgerLookup"/> (type Expire).
    /// </summary>
    public Points ExpirePoints(Points points, TransactionReference reference, bool referenceAlreadyPosted, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return PostExpiration(points, reference, referenceAlreadyPosted, clock);
    }

    public void Suspend(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotClosed();
        DomainException.ThrowIf(Status == LoyaltyAccountStatus.Suspended, LoyaltyAccountErrors.AlreadySuspended);

        Status = LoyaltyAccountStatus.Suspended;
        Raise(new LoyaltyAccountSuspended(TenantId, Id, clock.UtcNow));
    }

    public void Reactivate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotClosed();
        DomainException.ThrowIf(Status != LoyaltyAccountStatus.Suspended, LoyaltyAccountErrors.NotSuspended);

        Status = LoyaltyAccountStatus.Active;
        Raise(new LoyaltyAccountReactivated(TenantId, Id, clock.UtcNow));
    }

    /// <summary>
    /// Terminal : utilisé quand le client part ou est anonymisé. Le reliquat est soldé par une écriture
    /// <see cref="LoyaltyTransactionType.Closure"/> (un solde positif est perdu, un solde négatif est
    /// apuré), de sorte que le solde et la somme du registre finissent tous deux à zéro.
    /// </summary>
    public void Close(string reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotClosed();
        var trimmed = RequireReason(reason);

        var now = clock.UtcNow;
        var forfeited = Balance;
        if (forfeited != 0)
        {
            Post(NewEntry(
                LoyaltyTransactionType.Closure,
                forfeited > 0 ? LedgerDirection.Debit : LedgerDirection.Credit,
                Points.Of((int)Math.Abs(forfeited)),
                null,
                trimmed,
                now));
        }

        Status = LoyaltyAccountStatus.Closed;
        ClosedAtUtc = now;
        Raise(new LoyaltyAccountClosed(TenantId, Id, CustomerId, forfeited, trimmed, now));
    }

    private LoyaltyTransaction Redeem(
        Points points,
        TransactionReference reference,
        bool referenceAlreadyPosted,
        RewardId? rewardId,
        DateTimeOffset now,
        OverdraftPolicy? overdraftPolicy)
    {
        ArgumentNullException.ThrowIfNull(reference);
        EnsureActive();

        var entry = NewEntry(LoyaltyTransactionType.Redeem, LedgerDirection.Debit, points, reference, null, now);
        EnsureNotPosted(referenceAlreadyPosted, LoyaltyTransactionType.Redeem, reference);
        EnsureCovered(points, overdraftPolicy);

        Post(entry);
        Raise(new PointsRedeemed(TenantId, Id, CustomerId, entry.Id, points, reference, rewardId, Balance, now));
        return entry;
    }

    private void PostAdjustment(
        Points points,
        LedgerDirection direction,
        string reason,
        TransactionReference? reference,
        bool referenceAlreadyPosted,
        IClock clock,
        OverdraftPolicy? overdraftPolicy)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotClosed();

        var trimmed = reason?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed), LoyaltyAccountErrors.AdjustmentReasonRequired);
        DomainException.ThrowIf(trimmed!.Length > MaxReasonLength, LoyaltyAccountErrors.AdjustmentReasonTooLong);

        var now = clock.UtcNow;
        var entry = NewEntry(LoyaltyTransactionType.Adjust, direction, points, reference, trimmed, now);
        EnsureNotPosted(referenceAlreadyPosted, LoyaltyTransactionType.Adjust, reference);
        if (direction == LedgerDirection.Debit)
        {
            EnsureCovered(points, overdraftPolicy);
        }

        Post(entry);
        Raise(new LoyaltyAdjusted(TenantId, Id, CustomerId, entry.Id, points, direction, trimmed, Balance, now));
    }

    private Points PostExpiration(Points points, TransactionReference? reference, bool referenceAlreadyPosted, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotClosed();
        DomainException.ThrowIf(points.IsZero, LoyaltyAccountErrors.PointsNotPositive);
        EnsureNotPosted(referenceAlreadyPosted, LoyaltyTransactionType.Expire, reference);
        DomainException.ThrowIf(Balance <= 0, LoyaltyAccountErrors.NothingToExpire);

        var now = clock.UtcNow;
        var expired = Points.Of((int)Math.Min(points.Value, Balance));
        var entry = NewEntry(LoyaltyTransactionType.Expire, LedgerDirection.Debit, expired, reference, null, now);

        Post(entry);
        Raise(new PointsExpired(TenantId, Id, CustomerId, entry.Id, expired, Balance, now));
        return expired;
    }

    private LoyaltyTransaction Reverse(
        LoyaltyTransactionType originalType,
        LedgerDirection direction,
        PostedTransaction? original,
        string reason,
        DateTimeOffset now)
    {
        EnsureNotClosed();
        var trimmed = RequireReason(reason);

        DomainException.ThrowIf(original is null, LoyaltyAccountErrors.TransactionNotFound);
        DomainException.ThrowIf(original!.AccountId != Id, LoyaltyAccountErrors.TransactionOfAnotherAccount);
        DomainException.ThrowIf(original.Type != originalType, LoyaltyAccountErrors.TransactionTypeMismatch);
        DomainException.ThrowIf(
            original.IsAlreadyReversed || _transactions.Any(entry => entry.ReversedTransactionId == original.TransactionId),
            LoyaltyAccountErrors.AlreadyReversed);

        var reversal = NewEntry(
            LoyaltyTransactionType.Reversal, direction, original.Points, original.Reference, trimmed, now, original.TransactionId);
        Post(reversal);
        return reversal;
    }

    private LoyaltyTransaction NewEntry(
        LoyaltyTransactionType type,
        LedgerDirection direction,
        Points points,
        TransactionReference? reference,
        string? reason,
        DateTimeOffset now,
        LoyaltyTransactionId? reversedTransactionId = null) =>
        LoyaltyTransaction.Create(TenantId, Id, type, direction, points, reference, reason, now, reversedTransactionId);

    private void Post(LoyaltyTransaction entry)
    {
        _transactions.Add(entry);
        Balance += entry.SignedPoints;
    }

    private void EnsureNotClosed() =>
        DomainException.ThrowIf(Status == LoyaltyAccountStatus.Closed, LoyaltyAccountErrors.Closed);

    private void EnsureActive()
    {
        EnsureNotClosed();
        DomainException.ThrowIf(Status != LoyaltyAccountStatus.Active, LoyaltyAccountErrors.Suspended);
    }

    // Le registre stocké répond via le port. Les écritures en mémoire sont seulement celles ajoutées depuis le
    // chargement : elles sont vérifiées aussi, pour qu'une même référence ne soit pas passée deux fois avant
    // l'enregistrement de la première.
    private void EnsureNotPosted(bool referenceAlreadyPosted, LoyaltyTransactionType type, TransactionReference? reference) =>
        DomainException.ThrowIf(
            referenceAlreadyPosted
            || (reference is not null && _transactions.Any(entry => entry.Type == type && reference.Equals(entry.Reference))),
            LoyaltyAccountErrors.DuplicateTransaction);

    private static string RequireReason(string? reason)
    {
        var trimmed = reason?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed), LoyaltyAccountErrors.ReasonRequired);
        DomainException.ThrowIf(trimmed!.Length > MaxReasonLength, LoyaltyAccountErrors.ReasonTooLong);
        return trimmed;
    }

    private void EnsureCovered(Points debit, OverdraftPolicy? overdraftPolicy) =>
        DomainException.ThrowIf(
            !(overdraftPolicy ?? OverdraftPolicy.None).Allows(Balance, debit),
            LoyaltyAccountErrors.InsufficientBalance);
}
