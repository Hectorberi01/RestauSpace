namespace Storefront.Domain;

public readonly record struct CustomerSessionId
{
    private CustomerSessionId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CustomerSessionId New() => new(Guid.CreateVersion7());

    public static CustomerSessionId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new CustomerSessionId(value);
    }

    public override string ToString() => Value.ToString();
}

/// <summary>
/// La session d'un client connecté par code à usage unique, sur UNE application de vente (site, application). Le client
/// présente son jeton de session ; seule l'empreinte est gardée. Elle expire après <see cref="Lifetime"/> et se révoque
/// (déconnexion). Aucune coordonnée ici : le client est dans Customers, désigné par son id.
/// </summary>
public sealed class CustomerSession : AggregateRoot<CustomerSessionId>, ITenantOwned
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(60);
    public const int TokenHintLength = 4;

    private CustomerSession(
        CustomerSessionId id, TenantId tenantId, SalesAppId appId, CustomerId customerId, PublishableKeyHash tokenHash, string tokenHint, DateTimeOffset now)
        : base(id)
    {
        TenantId = tenantId;
        AppId = appId;
        CustomerId = customerId;
        TokenHash = tokenHash;
        TokenHint = tokenHint;
        CreatedAtUtc = now;
        ExpiresAtUtc = now + Lifetime;
    }

    // Réservé à l'ORM.
    private CustomerSession()
    {
        TokenHash = null!;
        TokenHint = null!;
    }

    public TenantId TenantId { get; private set; }

    public SalesAppId AppId { get; private set; }

    public CustomerId CustomerId { get; private set; }

    public PublishableKeyHash TokenHash { get; private set; }

    public string TokenHint { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsActive(DateTimeOffset nowUtc) => RevokedAtUtc is null && nowUtc < ExpiresAtUtc;

    /// <param name="app">Le site ou l'application où le client s'est connecté ; jamais une borne.</param>
    public static CustomerSession Open(SalesApp app, CustomerId customerId, PublishableKeyHash tokenHash, string tokenHint, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(tokenHash);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(customerId);
        DomainException.ThrowIf(app.Kind == SalesAppKind.Kiosk, CustomerSessionErrors.KioskNotAllowed);
        DomainException.ThrowIf(!app.IsActive, CustomerSessionErrors.AppDisabled);
        DomainException.ThrowIf(tokenHint is not { Length: TokenHintLength }, CustomerSessionErrors.TokenInvalid);
        return new CustomerSession(CustomerSessionId.New(), app.TenantId, app.Id, customerId, tokenHash, tokenHint, clock.UtcNow);
    }

    /// <summary>Déconnexion : le jeton cesse aussitôt de fonctionner. Rejouable.</summary>
    public void Revoke(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        RevokedAtUtc ??= clock.UtcNow;
    }
}

public static class CustomerSessionErrors
{
    public static readonly DomainError KioskNotAllowed = new("storefront.customer.kiosk_not_allowed", "Customers do not sign in at a kiosk.");
    public static readonly DomainError AppDisabled = new("storefront.customer.app_disabled", "This application is disabled.");
    public static readonly DomainError TokenInvalid = new("storefront.customer.token_invalid", "The session token is invalid.");
}
