namespace Loyalty.Domain;

public readonly record struct StoredValueCardId
{
    private StoredValueCardId(Guid value) => Value = value;

    public Guid Value { get; }

    public static StoredValueCardId New() => new(Guid.CreateVersion7());

    public static StoredValueCardId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new StoredValueCardId(value);
    }

    public override string ToString() => Value.ToString();
}

/// <summary>Carte cadeau (vendue ou offerte) ou avoir (rendu à un client à la place d'un remboursement).</summary>
public enum StoredValueKind
{
    GiftCard = 1,
    StoreCredit = 2,
}

public enum StoredValueStatus
{
    Active = 1,
    Cancelled = 2,
}

public enum StoredValueMovementType
{
    Issue = 1,
    Redeem = 2,
    Credit = 3,
    Reversal = 4,
    Cancel = 5,
}

/// <summary>
/// Un mouvement de la carte : <paramref name="Amount"/> signé (négatif pour un débit), solde après le mouvement.
/// <paramref name="Reference"/> : clé d'idempotence (id d'utilisation, de remboursement) ; jamais le code de la carte.
/// </summary>
public sealed record StoredValueMovement(
    Guid Id,
    StoredValueMovementType Type,
    decimal Amount,
    decimal BalanceAfter,
    string? Reference,
    Guid? ReversedMovementId,
    DateTimeOffset OccurredAtUtc);

public static class StoredValueErrors
{
    public static readonly DomainError AmountNotPositive = new("loyalty.stored_value.amount_not_positive", "The amount must be strictly positive.");
    public static readonly DomainError AmountTooLarge = new("loyalty.stored_value.amount_too_large", "The amount is above the allowed maximum.");
    public static readonly DomainError KindInvalid = new("loyalty.stored_value.kind_invalid", "The card kind is invalid.");
    public static readonly DomainError CodeInvalid = new("loyalty.stored_value.code_invalid", "The card code is invalid.");
    public static readonly DomainError ExpiryInPast = new("loyalty.stored_value.expiry_in_past", "The expiry date must be in the future.");
    public static readonly DomainError NoteTooLong = new("loyalty.stored_value.note_too_long", "The note is too long.");
    public static readonly DomainError Cancelled = new("loyalty.stored_value.cancelled", "The card was cancelled.");
    public static readonly DomainError AlreadyCancelled = new("loyalty.stored_value.already_cancelled", "The card is already cancelled.");
    public static readonly DomainError Expired = new("loyalty.stored_value.expired", "The card has expired.");
    public static readonly DomainError CurrencyMismatch = new("loyalty.stored_value.currency_mismatch", "The amount is not in the card currency.");
    public static readonly DomainError InsufficientBalance = new("loyalty.stored_value.insufficient_balance", "The card balance is too low.");
    public static readonly DomainError ReferenceRequired = new("loyalty.stored_value.reference_required", "A movement reference is required.");
    public static readonly DomainError ReferenceTooLong = new("loyalty.stored_value.reference_too_long", "The movement reference is too long.");
    public static readonly DomainError ReferenceReused = new("loyalty.stored_value.reference_reused", "This reference was already used for another movement.");
    public static readonly DomainError MovementNotFound = new("loyalty.stored_value.movement_not_found", "No movement of the card has this reference.");
    public static readonly DomainError MovementNotReversible = new("loyalty.stored_value.movement_not_reversible", "Only a redemption or a credit can be reversed.");
    public static readonly DomainError TooManyMovements = new("loyalty.stored_value.too_many_movements", "The card has too many movements.");
    public static readonly DomainError CreditAboveInitial = new("loyalty.stored_value.credit_above_initial", "A credit cannot bring the balance above what the card ever held.");
    public static readonly DomainError ReasonRequired = new("loyalty.stored_value.reason_required", "A reason is required.");
    public static readonly DomainError ReasonTooLong = new("loyalty.stored_value.reason_too_long", "The reason is too long.");
}

// Les événements ne portent jamais le code de la carte : ses 4 derniers caractères au plus, et des montants.

public sealed record StoredValueCardIssued(
    TenantId TenantId, StoredValueCardId CardId, StoredValueKind Kind, Money Amount, CustomerId? CustomerId, DateTimeOffset? ExpiresAtUtc, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record StoredValueRedeemed(TenantId TenantId, StoredValueCardId CardId, Money Amount, Money BalanceAfter, string Reference, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record StoredValueCredited(TenantId TenantId, StoredValueCardId CardId, Money Amount, Money BalanceAfter, string Reference, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record StoredValueMovementReversed(
    TenantId TenantId, StoredValueCardId CardId, string Reference, Money Amount, Money BalanceAfter, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record StoredValueCardCancelled(TenantId TenantId, StoredValueCardId CardId, Money ForfeitedBalance, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <summary>
/// Une carte à valeur stockée : carte cadeau ou avoir. Le code n'est JAMAIS gardé en clair : seulement son empreinte
/// (recherche) et ses 4 derniers caractères (affichage). Le solde est la projection des mouvements, gardés avec la
/// carte (bornés). Utiliser, créditer et annuler un mouvement sont idempotents par référence : un rejeu ne débite
/// jamais deux fois. Une carte annulée ou expirée ne sert plus ; une annulation perd le solde restant.
/// </summary>
public sealed class StoredValueCard : AggregateRoot<StoredValueCardId>, ITenantOwned
{
    public const int CodeHashLength = 64;
    public const int CodeHintLength = 4;
    public const int MaxNoteLength = 250;
    public const int MaxReferenceLength = 100;
    public const int MaxMovements = 500;
    public const decimal MaxAmount = 10_000_000m;

    private List<StoredValueMovement> _movements = [];

    private StoredValueCard(
        StoredValueCardId id, TenantId tenantId, StoredValueKind kind, string codeHash, string codeHint, Money amount,
        CustomerId? customerId, DateTimeOffset? expiresAtUtc, string? note, DateTimeOffset issuedAtUtc) : base(id)
    {
        TenantId = tenantId;
        Kind = kind;
        CodeHash = codeHash;
        CodeHint = codeHint;
        Currency = amount.Currency;
        InitialAmount = amount.Amount;
        BalanceAmount = amount.Amount;
        CustomerId = customerId;
        ExpiresAtUtc = expiresAtUtc;
        Note = note;
        IssuedAtUtc = issuedAtUtc;
        Status = StoredValueStatus.Active;
    }

    // Réservé à l'ORM.
    private StoredValueCard()
    {
        CodeHash = null!;
        CodeHint = null!;
        Currency = null!;
    }

    public TenantId TenantId { get; }

    public StoredValueKind Kind { get; }

    /// <summary>SHA-256 (hex) du code normalisé : la seule forme gardée du code.</summary>
    public string CodeHash { get; }

    /// <summary>Les 4 derniers caractères du code, pour que le personnel reconnaisse la carte.</summary>
    public string CodeHint { get; }

    public Currency Currency { get; }

    public decimal InitialAmount { get; }

    public decimal BalanceAmount { get; private set; }

    public Money Balance => Money.Of(BalanceAmount, Currency);

    public CustomerId? CustomerId { get; }

    public DateTimeOffset? ExpiresAtUtc { get; }

    public string? Note { get; }

    public StoredValueStatus Status { get; private set; }

    public DateTimeOffset IssuedAtUtc { get; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    public IReadOnlyList<StoredValueMovement> Movements => _movements.AsReadOnly();

    public bool IsExpiredAt(DateTimeOffset nowUtc) => ExpiresAtUtc is { } expiry && nowUtc >= expiry;

    /// <summary>Utilisable maintenant : active, non expirée, avec un solde.</summary>
    public bool IsUsableAt(DateTimeOffset nowUtc) => Status == StoredValueStatus.Active && !IsExpiredAt(nowUtc) && BalanceAmount > 0m;

    public static StoredValueCard Issue(
        TenantId tenantId,
        StoredValueKind kind,
        string codeHash,
        string codeHint,
        Money amount,
        IClock clock,
        CustomerId? customerId = null,
        DateTimeOffset? expiresAtUtc = null,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(!Enum.IsDefined(kind), StoredValueErrors.KindInvalid);
        DomainException.ThrowIf(
            codeHash is not { Length: CodeHashLength } || !codeHash.All(char.IsAsciiHexDigitLower), StoredValueErrors.CodeInvalid);
        DomainException.ThrowIf(
            codeHint is not { Length: CodeHintLength } || !codeHint.All(char.IsAsciiLetterOrDigit), StoredValueErrors.CodeInvalid);
        EnsurePositive(amount);
        var now = clock.UtcNow;
        DomainException.ThrowIf(expiresAtUtc is { } expiry && expiry <= now, StoredValueErrors.ExpiryInPast);
        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        DomainException.ThrowIf(trimmed is { Length: > MaxNoteLength }, StoredValueErrors.NoteTooLong);

        var card = new StoredValueCard(StoredValueCardId.New(), tenantId, kind, codeHash, codeHint, amount, customerId, expiresAtUtc, trimmed, now);
        card._movements.Add(new StoredValueMovement(Guid.CreateVersion7(), StoredValueMovementType.Issue, amount.Amount, amount.Amount, null, null, now));
        card.Raise(new StoredValueCardIssued(tenantId, card.Id, kind, amount, customerId, expiresAtUtc, now));
        return card;
    }

    /// <summary>
    /// Débite la carte (paiement d'une commande). Un rejeu de la même référence pour le même montant est sans effet ; la
    /// même référence pour un autre montant est refusée.
    /// </summary>
    public StoredValueMovement Redeem(Money amount, string reference, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(clock);
        var key = Reference(reference);
        if (Find(key) is { } existing)
        {
            DomainException.ThrowIf(existing.Type != StoredValueMovementType.Redeem || existing.Amount != -amount.Amount, StoredValueErrors.ReferenceReused);
            return existing;
        }

        EnsureAmount(amount);
        var now = clock.UtcNow;
        EnsureUsable(now);
        DomainException.ThrowIf(amount.Amount > BalanceAmount, StoredValueErrors.InsufficientBalance);

        var movement = Append(StoredValueMovementType.Redeem, -amount.Amount, key, null, now);
        Raise(new StoredValueRedeemed(TenantId, Id, amount, Balance, key, now));
        return movement;
    }

    /// <summary>
    /// Recrédite la carte (remboursement d'une commande payée avec elle). Permis sur une carte expirée (l'argent revient
    /// d'où il vient), jamais sur une carte annulée ; le solde ne dépasse jamais ce que la carte a contenu.
    /// </summary>
    public StoredValueMovement Credit(Money amount, string reference, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(clock);
        var key = Reference(reference);
        if (Find(key) is { } existing)
        {
            DomainException.ThrowIf(existing.Type != StoredValueMovementType.Credit || existing.Amount != amount.Amount, StoredValueErrors.ReferenceReused);
            return existing;
        }

        EnsureAmount(amount);
        DomainException.ThrowIf(Status == StoredValueStatus.Cancelled, StoredValueErrors.Cancelled);
        DomainException.ThrowIf(BalanceAmount + amount.Amount > InitialAmount, StoredValueErrors.CreditAboveInitial);

        var now = clock.UtcNow;
        var movement = Append(StoredValueMovementType.Credit, amount.Amount, key, null, now);
        Raise(new StoredValueCredited(TenantId, Id, amount, Balance, key, now));
        return movement;
    }

    /// <summary>
    /// Annule l'utilisation ou le crédit de cette référence (compensation quand l'opération qui l'a demandée échoue).
    /// Sans effet si rien n'a été posté sous cette référence ou si c'est déjà annulé.
    /// </summary>
    public void Reverse(string reference, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var key = Reference(reference);
        if (Find(key) is not { } original || _movements.Any(movement => movement.ReversedMovementId == original.Id))
        {
            return;
        }

        DomainException.ThrowIf(original.Type is not (StoredValueMovementType.Redeem or StoredValueMovementType.Credit), StoredValueErrors.MovementNotReversible);
        var now = clock.UtcNow;
        // Une carte annulée entre-temps reste à zéro : son solde a déjà été perdu.
        var amount = Status == StoredValueStatus.Cancelled ? 0m : -original.Amount;
        // Le solde reste entre zéro et ce que la carte a contenu, quel que soit l'ordre des mouvements.
        amount = Math.Clamp(amount, -BalanceAmount, InitialAmount - BalanceAmount);

        Append(StoredValueMovementType.Reversal, amount, null, original.Id, now);
        Raise(new StoredValueMovementReversed(TenantId, Id, key, Money.Of(Math.Abs(amount), Currency), Balance, now));
    }

    /// <summary>Retire la carte (perdue, émise par erreur) : le solde restant est perdu.</summary>
    public void Cancel(string reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == StoredValueStatus.Cancelled, StoredValueErrors.AlreadyCancelled);
        var trimmed = reason?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed), StoredValueErrors.ReasonRequired);
        DomainException.ThrowIf(trimmed!.Length > MaxNoteLength, StoredValueErrors.ReasonTooLong);

        var now = clock.UtcNow;
        var forfeited = Balance;
        Append(StoredValueMovementType.Cancel, -BalanceAmount, null, null, now);
        Status = StoredValueStatus.Cancelled;
        CancelledAtUtc = now;
        CancellationReason = trimmed;
        Raise(new StoredValueCardCancelled(TenantId, Id, forfeited, now));
    }

    private void EnsureUsable(DateTimeOffset now)
    {
        DomainException.ThrowIf(Status == StoredValueStatus.Cancelled, StoredValueErrors.Cancelled);
        DomainException.ThrowIf(IsExpiredAt(now), StoredValueErrors.Expired);
    }

    private void EnsureAmount(Money amount)
    {
        DomainException.ThrowIf(amount.Currency != Currency, StoredValueErrors.CurrencyMismatch);
        EnsurePositive(amount);
    }

    private static void EnsurePositive(Money amount)
    {
        DomainException.ThrowIf(!amount.IsPositive, StoredValueErrors.AmountNotPositive);
        DomainException.ThrowIf(amount.Amount > MaxAmount, StoredValueErrors.AmountTooLarge);
    }

    private StoredValueMovement? Find(string reference) => _movements.FirstOrDefault(movement => movement.Reference == reference);

    private StoredValueMovement Append(StoredValueMovementType type, decimal amount, string? reference, Guid? reversed, DateTimeOffset now)
    {
        DomainException.ThrowIf(_movements.Count >= MaxMovements, StoredValueErrors.TooManyMovements);
        BalanceAmount += amount;
        var movement = new StoredValueMovement(Guid.CreateVersion7(), type, amount, BalanceAmount, reference, reversed, now);
        _movements.Add(movement);
        return movement;
    }

    private static string Reference(string reference)
    {
        var trimmed = reference?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed), StoredValueErrors.ReferenceRequired);
        DomainException.ThrowIf(trimmed!.Length > MaxReferenceLength, StoredValueErrors.ReferenceTooLong);
        return trimmed;
    }
}
