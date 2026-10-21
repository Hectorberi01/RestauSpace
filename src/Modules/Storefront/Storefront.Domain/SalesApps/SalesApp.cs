namespace Storefront.Domain;

/// <summary>Où l'application de vente est utilisée : un site web, une application mobile en marque blanche ou une borne.</summary>
public enum SalesAppKind
{
    Web = 1,
    MobileApp = 2,
    Kiosk = 3,
}

public enum SalesAppStatus
{
    Active = 1,
    Disabled = 2,
}

/// <summary>
/// Une application de vente d'un restaurant (son site, son application, ses bornes) et sa clé publique <c>pk_</c>. Seule
/// l'EMPREINTE de la clé est gardée, avec ses 4 derniers caractères pour la reconnaître ; la clé est montrée une fois, à
/// l'émission ou à la rotation. Un site web n'est servi qu'aux origines déclarées. Aucun établissement déclaré : tous
/// ceux du restaurant.
/// </summary>
public sealed class SalesApp : AggregateRoot<SalesAppId>, ITenantOwned
{
    public const int MaxNameLength = 80;
    public const int MaxOrigins = 10;
    public const int MaxLocations = 100;
    public const int MaxReturnUrls = 5;
    public const int MaxUrlLength = 300;
    public const int KeyHintLength = 4;

    private List<string> _allowedOrigins = [];
    private List<LocationId> _locationIds = [];
    private List<string> _returnUrls = [];

    private SalesApp(SalesAppId id, TenantId tenantId, string name, SalesAppKind kind, PublishableKeyHash keyHash, string keyHint, DateTimeOffset now)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Kind = kind;
        KeyHash = keyHash;
        KeyHint = keyHint;
        KeyIssuedAtUtc = now;
        Status = SalesAppStatus.Active;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    // Réservé à l'ORM.
    private SalesApp()
    {
        Name = null!;
        KeyHash = null!;
        KeyHint = null!;
    }

    public TenantId TenantId { get; private set; }

    public string Name { get; private set; }

    public SalesAppKind Kind { get; private set; }

    public SalesAppStatus Status { get; private set; }

    public PublishableKeyHash KeyHash { get; private set; }

    /// <summary>Les 4 derniers caractères de la clé, pour la reconnaître dans la console.</summary>
    public string KeyHint { get; private set; }

    public DateTimeOffset KeyIssuedAtUtc { get; private set; }

    /// <summary>Origines autorisées (<c>https://hote[:port]</c>) ; site web seulement.</summary>
    public IReadOnlyList<string> AllowedOrigins => _allowedOrigins.AsReadOnly();

    /// <summary>Établissements servis ; vide = tous ceux du restaurant.</summary>
    public IReadOnlyList<LocationId> LocationIds => _locationIds.AsReadOnly();

    /// <summary>Pages de retour après un paiement en ligne autorisées pour cette application.</summary>
    public IReadOnlyList<string> ReturnUrls => _returnUrls.AsReadOnly();

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Contrôle anti-robot (Turnstile) exigé avant une commande, une réservation ou un code de connexion.</summary>
    public bool RequiresHumanCheck { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool IsActive => Status == SalesAppStatus.Active;

    public static SalesApp Create(TenantId tenantId, string? name, SalesAppKind kind, PublishableKeyHash keyHash, string keyHint, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(keyHash);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(!Enum.IsDefined(kind), SalesAppErrors.KindInvalid);
        var app = new SalesApp(SalesAppId.New(), tenantId, ValidName(name), kind, keyHash, ValidHint(keyHint), clock.UtcNow);
        app.Raise(new SalesAppCreated(tenantId, app.Id, kind, app.CreatedAtUtc));
        return app;
    }

    public void Rename(string? name, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var valid = ValidName(name);
        if (valid == Name)
        {
            return;
        }

        Name = valid;
        Changed(clock);
    }

    /// <summary>
    /// Remplace les origines autorisées, normalisées en <c>schéma://hôte[:port]</c> : celles du site, ou celles d'une
    /// application mobile ou d'une borne qui appelle l'API depuis un navigateur ou une WebView. Les schémas
    /// <c>capacitor://localhost</c> et <c>ionic://localhost</c> sont réservés aux applications mobiles.
    /// </summary>
    public void SetAllowedOrigins(IEnumerable<string>? origins, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var normalized = (origins ?? []).Select(NormalizeOrigin).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        DomainException.ThrowIf(normalized.Count > MaxOrigins, SalesAppErrors.TooManyOrigins);
        DomainException.ThrowIf(Kind != SalesAppKind.MobileApp && normalized.Exists(IsWebViewOrigin), SalesAppErrors.OriginInvalid);
        if (normalized.SequenceEqual(_allowedOrigins, StringComparer.Ordinal))
        {
            return;
        }

        _allowedOrigins = normalized;
        Changed(clock);
    }

    /// <summary>
    /// Remplace les établissements servis. Chaque référence vient du MÊME restaurant (vérifié ici) ; aucune = tous.
    /// </summary>
    public void SetLocations(IEnumerable<LocationReference>? locations, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var list = (locations ?? []).ToList();
        foreach (var location in list)
        {
            ArgumentNullException.ThrowIfNull(location, nameof(locations));
            this.EnsureSameTenant(location.TenantId);
        }

        var ids = list.Select(location => location.LocationId).Distinct().OrderBy(id => id.Value).ToList();
        DomainException.ThrowIf(ids.Count > MaxLocations, SalesAppErrors.TooManyLocations);
        if (ids.SequenceEqual(_locationIds))
        {
            return;
        }

        _locationIds = ids;
        Changed(clock);
    }

    /// <summary>Remplace les pages de retour de paiement autorisées : HTTPS, ou HTTP sur la machine locale (développement).</summary>
    public void SetReturnUrls(IEnumerable<string>? urls, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var normalized = (urls ?? []).Select(NormalizeReturnUrl).Distinct(StringComparer.Ordinal).ToList();
        DomainException.ThrowIf(normalized.Count > MaxReturnUrls, SalesAppErrors.TooManyReturnUrls);
        if (normalized.SequenceEqual(_returnUrls, StringComparer.Ordinal))
        {
            return;
        }

        _returnUrls = normalized;
        Changed(clock);
    }

    /// <summary>Exige ou non le contrôle anti-robot : site web et application seulement (une borne appairée n'en a pas besoin).</summary>
    public void SetHumanCheck(bool required, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(required && Kind == SalesAppKind.Kiosk, SalesAppErrors.HumanCheckNotForKiosk);
        if (required == RequiresHumanCheck)
        {
            return;
        }

        RequiresHumanCheck = required;
        Changed(clock);
    }

    /// <summary>Nouvelle clé : l'ancienne cesse aussitôt de fonctionner (une clé publique peut être republiée sans délai).</summary>
    public void RotateKey(PublishableKeyHash keyHash, string keyHint, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(keyHash);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(keyHash == KeyHash, SalesAppErrors.KeyUnchanged);
        KeyHash = keyHash;
        KeyHint = ValidHint(keyHint);
        KeyIssuedAtUtc = clock.UtcNow;
        UpdatedAtUtc = KeyIssuedAtUtc;
        Raise(new SalesAppKeyRotated(TenantId, Id, KeyIssuedAtUtc));
    }

    public void Disable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == SalesAppStatus.Disabled, SalesAppErrors.AlreadyDisabled);
        Status = SalesAppStatus.Disabled;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new SalesAppDisabled(TenantId, Id, UpdatedAtUtc));
    }

    public void Enable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == SalesAppStatus.Active, SalesAppErrors.AlreadyActive);
        Status = SalesAppStatus.Active;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new SalesAppEnabled(TenantId, Id, UpdatedAtUtc));
    }

    /// <summary>Vrai si cet établissement est servi par l'application.</summary>
    public bool Serves(LocationId locationId) => _locationIds.Count == 0 || _locationIds.Contains(locationId);

    /// <summary>
    /// Vrai si une requête portant cette origine peut utiliser la clé. Sans origine (serveur, application mobile, borne) :
    /// toujours. Avec une origine (navigateur, WebView) : seulement si l'application l'a déclarée.
    /// </summary>
    public bool AcceptsOrigin(string? origin)
    {
        if (string.IsNullOrEmpty(origin))
        {
            return true;
        }

        return TryNormalizeOrigin(origin, out var normalized) && _allowedOrigins.Contains(normalized, StringComparer.Ordinal);
    }

    /// <summary>Vrai si l'adresse est une des pages de retour déclarées (comparaison exacte, après normalisation).</summary>
    public bool AllowsReturnUrl(string? url) =>
        url is not null && TryNormalizeUrl(url, out var normalized) && _returnUrls.Contains(normalized, StringComparer.Ordinal);

    public static string NormalizeOrigin(string? origin) =>
        TryNormalizeOrigin(origin, out var normalized) ? normalized : throw new DomainException(SalesAppErrors.OriginInvalid);

    private static bool TryNormalizeOrigin(string? origin, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(origin) || origin.Length > MaxUrlLength
            || !Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var uri)
            || !(IsAllowedScheme(uri) || IsWebViewScheme(uri))
            || uri.AbsolutePath is not ("/" or "") || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
        {
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        return true;
    }

    private static string NormalizeReturnUrl(string? url) =>
        TryNormalizeUrl(url, out var normalized) ? normalized : throw new DomainException(SalesAppErrors.ReturnUrlInvalid);

    private static bool TryNormalizeUrl(string? url, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxUrlLength
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || !IsAllowedScheme(uri) || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0)
        {
            return false;
        }

        normalized = uri.GetLeftPart(UriPartial.Path);
        return true;
    }

    private static readonly string[] WebViewSchemes = ["capacitor", "ionic"];

    private static bool IsWebViewScheme(Uri uri) =>
        WebViewSchemes.Contains(uri.Scheme, StringComparer.Ordinal) && string.Equals(uri.Host, "localhost", StringComparison.Ordinal) && uri.IsDefaultPort;

    private static bool IsWebViewOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) && IsWebViewScheme(uri);

    private static bool IsAllowedScheme(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);

    private static string ValidName(string? name)
    {
        var trimmed = name?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed), SalesAppErrors.NameRequired);
        DomainException.ThrowIf(trimmed!.Length > MaxNameLength, SalesAppErrors.NameTooLong);
        return trimmed;
    }

    private static string ValidHint(string? hint)
    {
        DomainException.ThrowIf(
            hint is null || hint.Length != KeyHintLength || !hint.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'),
            SalesAppErrors.KeyHintInvalid);
        return hint!;
    }

    private void Changed(IClock clock)
    {
        UpdatedAtUtc = clock.UtcNow;
        Raise(new SalesAppChanged(TenantId, Id, UpdatedAtUtc));
    }
}

/// <summary>Empreinte SHA-256 (hexadécimal) d'une clé publique. Jamais affichée.</summary>
public sealed class PublishableKeyHash : ValueObject
{
    public const int Length = 64;

    private PublishableKeyHash(string value) => Value = value;

    public string Value { get; }

    public static PublishableKeyHash Create(string? value)
    {
        DomainException.ThrowIf(
            value is null || value.Length != Length || !value.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')),
            SalesAppErrors.KeyHashInvalid);
        return new PublishableKeyHash(value!);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => "PublishableKeyHash(***)";
}

/// <summary>Un établissement, tel que vu par la vente en ligne : son restaurant, pour vérifier qu'il est le même.</summary>
public sealed record LocationReference
{
    public LocationReference(TenantId tenantId, LocationId locationId)
    {
        TenantId = Guard.NotEmpty(tenantId);
        LocationId = Guard.NotEmpty(locationId);
    }

    public TenantId TenantId { get; }

    public LocationId LocationId { get; }
}

public static class SalesAppErrors
{
    public static readonly DomainError NameRequired = new("storefront.app.name_required", "The application needs a name.");
    public static readonly DomainError NameTooLong = new("storefront.app.name_too_long", "The application name is too long (80 characters at most).");
    public static readonly DomainError KindInvalid = new("storefront.app.kind_invalid", "Unknown application kind.");
    public static readonly DomainError OriginInvalid = new("storefront.app.origin_invalid", "An origin is https://host[:port], without a path.");
    public static readonly DomainError TooManyOrigins = new("storefront.app.too_many_origins", "10 origins at most.");
    public static readonly DomainError TooManyLocations = new("storefront.app.too_many_locations", "Too many locations.");
    public static readonly DomainError ReturnUrlInvalid = new("storefront.app.return_url_invalid", "A return page is an https address.");
    public static readonly DomainError TooManyReturnUrls = new("storefront.app.too_many_return_urls", "5 return pages at most.");
    public static readonly DomainError KeyHashInvalid = new("storefront.app.key_hash_invalid", "The key fingerprint is invalid.");
    public static readonly DomainError KeyHintInvalid = new("storefront.app.key_hint_invalid", "The key hint is invalid.");
    public static readonly DomainError KeyUnchanged = new("storefront.app.key_unchanged", "The new key must differ from the current one.");
    public static readonly DomainError AlreadyDisabled = new("storefront.app.already_disabled", "The application is already disabled.");
    public static readonly DomainError HumanCheckNotForKiosk = new("storefront.app.human_check_not_for_kiosk", "A kiosk does not use the anti-robot check.");
    public static readonly DomainError AlreadyActive = new("storefront.app.already_active", "The application is already active.");
}

public sealed record SalesAppCreated(TenantId TenantId, SalesAppId SalesAppId, SalesAppKind Kind, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SalesAppChanged(TenantId TenantId, SalesAppId SalesAppId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SalesAppKeyRotated(TenantId TenantId, SalesAppId SalesAppId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SalesAppDisabled(TenantId TenantId, SalesAppId SalesAppId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SalesAppEnabled(TenantId TenantId, SalesAppId SalesAppId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
