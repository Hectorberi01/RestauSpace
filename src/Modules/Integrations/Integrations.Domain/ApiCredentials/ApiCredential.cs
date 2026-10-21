namespace Integrations.Domain;

/// <summary>Identifiant d'intégration d'un tenant. Seul le HASH de la clé est gardé ; la clé brute est montrée une fois, hors domaine.</summary>
public sealed class ApiCredential : AggregateRoot<CredentialId>, ITenantOwned
{
    public const int MaxNameLength = 100;
    public const int MaxScopes = 50;

    private readonly HashSet<ApiScope> _scopes = [];

    private ApiCredential(
        CredentialId id,
        TenantId tenantId,
        string name,
        ApiKeyHash keyHash,
        HashSet<ApiScope> scopes,
        DateTimeOffset? expiresAtUtc,
        DateTimeOffset issuedAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        KeyHash = keyHash;
        _scopes = scopes;
        ExpiresAtUtc = expiresAtUtc;
        Status = ApiCredentialStatus.Active;
        IssuedAtUtc = issuedAtUtc;
    }

    // Réservé à l'ORM.
    private ApiCredential()
    {
        Name = null!;
        KeyHash = null!;
    }

    public TenantId TenantId { get; private set; }

    public string Name { get; private set; }

    public ApiKeyHash KeyHash { get; private set; }

    public IReadOnlyCollection<ApiScope> Scopes => _scopes.ToList().AsReadOnly();

    public ApiCredentialStatus Status { get; private set; }

    public DateTimeOffset? ExpiresAtUtc { get; private set; }

    public DateTimeOffset IssuedAtUtc { get; private set; }

    public DateTimeOffset? RotatedAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    /// <summary>
    /// Dernière authentification réussie avec la clé, à l'heure près. Posée par l'Infrastructure à l'authentification
    /// (écriture directe, sans événement ni contrôle de version : ce n'est pas une décision métier). Null : jamais utilisée.
    /// </summary>
    public DateTimeOffset? LastUsedAtUtc { get; private set; }

    public static ApiCredential Issue(
        TenantId tenantId,
        string? name,
        ApiKeyHash keyHash,
        IEnumerable<ApiScope> scopes,
        ApiScopeCatalog catalog,
        DateTimeOffset? expiresAtUtc,
        IClock clock,
        ChangeActor? actor = null)
    {
        ArgumentNullException.ThrowIfNull(keyHash);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);

        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), ApiCredentialErrors.NameEmpty);
        var trimmedName = name!.Trim();
        DomainException.ThrowIf(trimmedName.Length > MaxNameLength, ApiCredentialErrors.NameTooLong);

        var grantedScopes = new HashSet<ApiScope>();
        foreach (var scope in scopes)
        {
            ArgumentNullException.ThrowIfNull(scope, nameof(scopes));
            grantedScopes.Add(scope);
        }

        DomainException.ThrowIf(grantedScopes.Count == 0, ApiCredentialErrors.ScopesRequired);
        DomainException.ThrowIf(grantedScopes.Count > MaxScopes, ApiCredentialErrors.TooManyScopes);
        DomainException.ThrowIf(grantedScopes.Any(scope => !catalog.Contains(scope)), ApiCredentialErrors.ScopeUnknown);

        var now = clock.UtcNow;
        var expiry = expiresAtUtc?.ToUniversalTime();
        DomainException.ThrowIf(expiry <= now, ApiCredentialErrors.ExpiryNotInFuture);

        var credential = new ApiCredential(
            CredentialId.New(), tenantId, trimmedName, keyHash, grantedScopes, expiry, now);
        credential.Raise(new ApiCredentialIssued(
            tenantId, credential.Id, trimmedName, expiry, now, actor,
            grantedScopes.Select(scope => scope.Value).Order(StringComparer.Ordinal).ToList()));
        return credential;
    }

    public bool IsExpiredAt(DateTimeOffset instantUtc) => ExpiresAtUtc <= instantUtc;

    /// <summary>
    /// Clé dormante : encore utilisable mais inutilisée depuis <paramref name="after"/> (depuis son émission si elle n'a
    /// jamais servi). Candidate à la révocation.
    /// </summary>
    public bool IsDormantAt(DateTimeOffset instantUtc, TimeSpan after) =>
        IsUsableAt(instantUtc) && (LastUsedAtUtc ?? IssuedAtUtc) + after <= instantUtc;

    /// <summary>Utilisable tant qu'il est actif et non expiré (l'instant d'expiration lui-même est exclu).</summary>
    public bool IsUsableAt(DateTimeOffset instantUtc) =>
        Status == ApiCredentialStatus.Active && !IsExpiredAt(instantUtc);

    /// <summary>Vrai seulement tant que l'identifiant est utilisable : révoqué ou expiré, il n'a aucun scope.</summary>
    public bool HasScope(ApiScope scope, DateTimeOffset instantUtc)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return IsUsableAt(instantUtc) && _scopes.Contains(scope);
    }

    /// <summary>Décision d'autorisation pour une requête authentifiée avec cet identifiant.</summary>
    public void EnsureCanAccess(ApiScope scope, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureUsable(clock.UtcNow);
        DomainException.ThrowIf(!_scopes.Contains(scope), ApiCredentialErrors.ScopeNotGranted);
    }

    /// <summary>Remplace le hash de la clé. L'ancienne clé cesse aussitôt de fonctionner (pas de délai de grâce).</summary>
    public void Rotate(ApiKeyHash newKeyHash, IClock clock, ChangeActor? actor = null)
    {
        ArgumentNullException.ThrowIfNull(newKeyHash);
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.UtcNow;
        EnsureUsable(now);
        if (KeyHash == newKeyHash)
        {
            return;
        }

        KeyHash = newKeyHash;
        RotatedAtUtc = now;

        Raise(new ApiCredentialRotated(TenantId, Id, now, actor));
    }

    public void Revoke(IClock clock, ChangeActor? actor = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == ApiCredentialStatus.Revoked, ApiCredentialErrors.Revoked);

        var now = clock.UtcNow;
        Status = ApiCredentialStatus.Revoked;
        RevokedAtUtc = now;

        Raise(new ApiCredentialRevoked(TenantId, Id, now, actor));
    }

    private void EnsureUsable(DateTimeOffset nowUtc)
    {
        DomainException.ThrowIf(Status == ApiCredentialStatus.Revoked, ApiCredentialErrors.Revoked);
        DomainException.ThrowIf(IsExpiredAt(nowUtc), ApiCredentialErrors.Expired);
    }
}
