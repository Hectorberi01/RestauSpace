namespace Billing.Domain;

public readonly record struct SubscriptionCheckoutId
{
    private SubscriptionCheckoutId(Guid value) => Value = value;

    public Guid Value { get; }

    public static SubscriptionCheckoutId New() => new(Guid.CreateVersion7());

    public static SubscriptionCheckoutId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new SubscriptionCheckoutId(value);
    }

    public override string ToString() => Value.ToString();
}

public enum SubscriptionCheckoutStatus
{
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Expired = 4,
}

/// <summary>
/// Le règlement en ligne d'une période d'abonnement par le restaurant (Mobile Money ou carte, chez le prestataire de la
/// plateforme). Créé en attente pour le montant dû à ce moment ; le prestataire fait foi : payé, refusé, ou expiré faute
/// de réponse. Un règlement payé enregistre le paiement d'abonnement (<see cref="PaymentId"/>), sauf si la période était
/// déjà payée entre-temps : il reste alors sans paiement, à rembourser par la plateforme.
/// </summary>
public sealed class SubscriptionCheckout : AggregateRoot<SubscriptionCheckoutId>, ITenantOwned
{
    public const int MaxProviderLength = 32;
    public const int MaxReferenceLength = 100;
    public const int MaxCheckoutUrlLength = 2000;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    private SubscriptionCheckout(
        SubscriptionCheckoutId id, TenantId tenantId, SubscriptionId subscriptionId, Money amount, string provider, DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        SubscriptionId = subscriptionId;
        Amount = amount;
        Provider = provider;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = createdAtUtc + Lifetime;
        Status = SubscriptionCheckoutStatus.Pending;
    }

    // Réservé à l'ORM.
    private SubscriptionCheckout()
    {
        Amount = null!;
        Provider = null!;
    }

    public TenantId TenantId { get; private set; }

    public SubscriptionId SubscriptionId { get; private set; }

    public Money Amount { get; private set; }

    public string Provider { get; private set; }

    /// <summary>Référence de la transaction chez le prestataire, connue une fois la demande acceptée.</summary>
    public string? ProviderReference { get; private set; }

    /// <summary>Page de paiement du prestataire, s'il en ouvre une (rien pour un paiement poussé sur le téléphone).</summary>
    public string? CheckoutUrl { get; private set; }

    public SubscriptionCheckoutStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    /// <summary>Le paiement d'abonnement enregistré ; null tant qu'il n'est pas payé, ou payé pour une période déjà réglée.</summary>
    public SubscriptionPaymentId? PaymentId { get; private set; }

    public bool IsOpenAt(DateTimeOffset nowUtc) => Status == SubscriptionCheckoutStatus.Pending && nowUtc < ExpiresAtUtc;

    public static SubscriptionCheckout Start(SaasSubscription subscription, Money amount, string? provider, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(subscription.Status == SubscriptionStatus.Cancelled, SubscriptionCheckoutErrors.SubscriptionCancelled);
        DomainException.ThrowIf(amount.Amount <= 0, SubscriptionCheckoutErrors.AmountInvalid);
        var code = (provider ?? string.Empty).Trim().ToLowerInvariant();
        DomainException.ThrowIf(code.Length is 0 or > MaxProviderLength, SubscriptionCheckoutErrors.ProviderInvalid);

        var checkout = new SubscriptionCheckout(SubscriptionCheckoutId.New(), subscription.TenantId, subscription.Id, amount, code, clock.UtcNow);
        checkout.Raise(new SubscriptionCheckoutStarted(checkout.TenantId, checkout.Id, checkout.SubscriptionId, amount, code, checkout.CreatedAtUtc));
        return checkout;
    }

    /// <summary>La référence de la transaction ouverte chez le prestataire, et sa page de paiement, une seule fois.</summary>
    public void AttachReference(string? reference, Uri? checkoutUrl)
    {
        EnsurePending();
        DomainException.ThrowIf(ProviderReference is not null, SubscriptionCheckoutErrors.ReferenceAlreadySet);
        var value = (reference ?? string.Empty).Trim();
        DomainException.ThrowIf(value.Length is 0 or > MaxReferenceLength, SubscriptionCheckoutErrors.ReferenceInvalid);
        DomainException.ThrowIf(
            checkoutUrl is not null && (!checkoutUrl.IsAbsoluteUri || checkoutUrl.Scheme != Uri.UriSchemeHttps || checkoutUrl.AbsoluteUri.Length > MaxCheckoutUrlLength),
            SubscriptionCheckoutErrors.CheckoutUrlInvalid);
        ProviderReference = value;
        CheckoutUrl = checkoutUrl?.AbsoluteUri;
    }

    /// <param name="payment">Le paiement enregistré ; null si la période était déjà payée (à rembourser).</param>
    public void MarkPaid(SubscriptionPaymentId? payment, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsurePending();
        DomainException.ThrowIf(ProviderReference is null, SubscriptionCheckoutErrors.ReferenceMissing);
        Status = SubscriptionCheckoutStatus.Paid;
        PaymentId = payment;
        ClosedAtUtc = clock.UtcNow;
        Raise(new SubscriptionCheckoutPaid(TenantId, Id, SubscriptionId, Amount, Provider, payment, ClosedAtUtc.Value));
    }

    public void Fail(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsurePending();
        Status = SubscriptionCheckoutStatus.Failed;
        ClosedAtUtc = clock.UtcNow;
        Raise(new SubscriptionCheckoutClosed(TenantId, Id, SubscriptionId, Status, ClosedAtUtc.Value));
    }

    /// <summary>Sans réponse du prestataire au-delà de la durée de vie : abandonné.</summary>
    public void Expire(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsurePending();
        DomainException.ThrowIf(clock.UtcNow < ExpiresAtUtc, SubscriptionCheckoutErrors.NotExpired);
        Status = SubscriptionCheckoutStatus.Expired;
        ClosedAtUtc = clock.UtcNow;
        Raise(new SubscriptionCheckoutClosed(TenantId, Id, SubscriptionId, Status, ClosedAtUtc.Value));
    }

    private void EnsurePending() => DomainException.ThrowIf(Status != SubscriptionCheckoutStatus.Pending, SubscriptionCheckoutErrors.NotPending);
}

public static class SubscriptionCheckoutErrors
{
    public static readonly DomainError SubscriptionCancelled =
        new("billing.checkout.subscription_cancelled", "A cancelled subscription cannot be paid.");

    public static readonly DomainError AmountInvalid = new("billing.checkout.amount_invalid", "The amount to pay must be positive.");

    public static readonly DomainError ProviderInvalid = new("billing.checkout.provider_invalid", "The payment provider code is invalid.");

    public static readonly DomainError ReferenceAlreadySet =
        new("billing.checkout.reference_already_set", "The provider reference of a checkout is set once.");

    public static readonly DomainError ReferenceInvalid = new("billing.checkout.reference_invalid", "The provider reference is invalid.");

    public static readonly DomainError CheckoutUrlInvalid = new("billing.checkout.checkout_url_invalid", "The payment page must be an HTTPS address.");

    public static readonly DomainError ReferenceMissing =
        new("billing.checkout.reference_missing", "A checkout is paid only once the provider has accepted it.");

    public static readonly DomainError NotPending = new("billing.checkout.not_pending", "This payment is already closed.", DomainErrorKind.Conflict);

    public static readonly DomainError NotExpired = new("billing.checkout.not_expired", "This payment can still be completed.");
}

public sealed record SubscriptionCheckoutStarted(
    TenantId TenantId,
    SubscriptionCheckoutId CheckoutId,
    SubscriptionId SubscriptionId,
    Money Amount,
    string Provider,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SubscriptionCheckoutPaid(
    TenantId TenantId,
    SubscriptionCheckoutId CheckoutId,
    SubscriptionId SubscriptionId,
    Money Amount,
    string Provider,
    SubscriptionPaymentId? PaymentId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SubscriptionCheckoutClosed(
    TenantId TenantId,
    SubscriptionCheckoutId CheckoutId,
    SubscriptionId SubscriptionId,
    SubscriptionCheckoutStatus Status,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
