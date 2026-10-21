namespace Fiscal.Domain;

public readonly record struct FiscalProfileId
{
    private FiscalProfileId(Guid value) => Value = value;

    public Guid Value { get; }

    public static FiscalProfileId New() => new(Guid.CreateVersion7());

    public static FiscalProfileId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new FiscalProfileId(value);
    }

    public override string ToString() => Value.ToString();
}

/// <summary>Régime fiscal du restaurant : il fixe le groupe de taxe des articles sur la facture normalisée.</summary>
public enum TaxRegime
{
    /// <summary>Assujetti à la TVA : articles du groupe B (18 %), ceux à taux nul au groupe A (exonéré).</summary>
    Vat = 1,

    /// <summary>Taxe professionnelle synthétique : tous les articles au groupe E.</summary>
    Tps = 2,
}

/// <summary>Acompte sur impôt assis sur les bénéfices, calculé par la DGI sur la facture.</summary>
public enum AibRate
{
    None = 0,

    /// <summary>1 %.</summary>
    A = 1,

    /// <summary>5 %.</summary>
    B = 2,
}

public enum FiscalMode
{
    /// <summary>Aucune facture normalisée n'est demandée.</summary>
    Disabled = 1,

    /// <summary>Environnement de test de la DGI : factures sans valeur légale (code préfixé TEST).</summary>
    Sandbox = 2,

    /// <summary>Environnement réel de la DGI.</summary>
    Production = 3,
}

/// <summary>
/// Identité fiscale d'un restaurant et son raccordement à l'e-MECeF de la DGI du Bénin. Le jeton d'accès est rangé au
/// coffre de l'Infrastructure : l'agrégat ne sait que s'il existe. Un seul profil par restaurant.
/// </summary>
public sealed class FiscalProfile : AggregateRoot<FiscalProfileId>, ITenantOwned
{
    public const int IfuLength = 13;
    public const int MaxRccmLength = 40;

    private FiscalProfile(FiscalProfileId id, TenantId tenantId, string ifu, string? rccm, TaxRegime regime, AibRate aib, DateTimeOffset now)
        : base(id)
    {
        TenantId = tenantId;
        Ifu = ifu;
        Rccm = rccm;
        Regime = regime;
        Aib = aib;
        Mode = FiscalMode.Disabled;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
    }

    // Réservé à l'ORM.
    private FiscalProfile()
    {
        Ifu = null!;
    }

    public TenantId TenantId { get; private set; }

    /// <summary>Identifiant fiscal unique : 13 chiffres.</summary>
    public string Ifu { get; private set; }

    /// <summary>Registre du commerce (mention du reçu) ; facultatif.</summary>
    public string? Rccm { get; private set; }

    public TaxRegime Regime { get; private set; }

    public AibRate Aib { get; private set; }

    public FiscalMode Mode { get; private set; }

    /// <summary>Un jeton e-MECeF a été rangé au coffre (sa valeur n'est jamais relue hors de l'Infrastructure).</summary>
    public bool HasToken { get; private set; }

    public DateTimeOffset? TokenUpdatedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool IsActive => Mode != FiscalMode.Disabled;

    public static FiscalProfile Create(TenantId tenantId, string? ifu, string? rccm, TaxRegime regime, AibRate aib, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        var profile = new FiscalProfile(FiscalProfileId.New(), tenantId, IfuOf(ifu), RccmOf(rccm), RegimeOf(regime), AibOf(aib), clock.UtcNow);
        profile.Raise(new FiscalProfileChanged(tenantId, profile.Id, clock.UtcNow));
        return profile;
    }

    public void Update(string? ifu, string? rccm, TaxRegime regime, AibRate aib, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var newIfu = IfuOf(ifu);
        // Le jeton DGI est lié à un IFU : en changer oblige à saisir un nouveau jeton et coupe la facturation.
        if (newIfu != Ifu)
        {
            HasToken = false;
            TokenUpdatedAtUtc = null;
            Mode = FiscalMode.Disabled;
        }

        Ifu = newIfu;
        Rccm = RccmOf(rccm);
        Regime = RegimeOf(regime);
        Aib = AibOf(aib);
        UpdatedAtUtc = clock.UtcNow;
        Raise(new FiscalProfileChanged(TenantId, Id, clock.UtcNow));
    }

    public void MarkTokenStored(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        HasToken = true;
        TokenUpdatedAtUtc = clock.UtcNow;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new FiscalProfileChanged(TenantId, Id, clock.UtcNow));
    }

    public void SetMode(FiscalMode mode, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!Enum.IsDefined(mode), FiscalErrors.ModeInvalid);
        DomainException.ThrowIf(mode != FiscalMode.Disabled && !HasToken, FiscalErrors.TokenMissing);
        Mode = mode;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new FiscalProfileChanged(TenantId, Id, clock.UtcNow));
    }

    /// <summary>Groupe de taxe e-MECeF d'un article selon le régime et son taux.</summary>
    public string TaxGroupFor(decimal taxRate) => Regime == TaxRegime.Tps ? "E" : taxRate > 0 ? "B" : "A";

    private static string IfuOf(string? ifu)
    {
        var value = ifu?.Trim() ?? string.Empty;
        DomainException.ThrowIf(value.Length != IfuLength || !value.All(char.IsAsciiDigit), FiscalErrors.IfuInvalid);
        return value;
    }

    private static string? RccmOf(string? rccm)
    {
        var value = rccm?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        DomainException.ThrowIf(value.Length > MaxRccmLength || value.Any(char.IsControl), FiscalErrors.RccmInvalid);
        return value;
    }

    private static TaxRegime RegimeOf(TaxRegime regime)
    {
        DomainException.ThrowIf(!Enum.IsDefined(regime), FiscalErrors.RegimeInvalid);
        return regime;
    }

    private static AibRate AibOf(AibRate aib)
    {
        DomainException.ThrowIf(!Enum.IsDefined(aib), FiscalErrors.AibInvalid);
        return aib;
    }
}

public sealed record FiscalProfileChanged(TenantId TenantId, FiscalProfileId ProfileId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
