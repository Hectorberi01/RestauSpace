namespace Tenants.Domain.Tenants;

/// <summary>
/// Organisation cliente du SaaS. Son identité EST le <see cref="TenantId"/> que porte tout agrégat appartenant à un
/// tenant dans les autres modules ; ces agrégats la référencent par id et ne sont jamais ses enfants.
/// </summary>
public sealed class Tenant : AggregateRoot<TenantId>
{
    public TenantName Name { get; private set; }

    public TenantStatus Status { get; private set; }

    public Currency DefaultCurrency { get; private set; }

    public TimeZoneId TimeZone { get; private set; }

    public Branding Branding { get; private set; }

    /// <summary>Présentation publique (accroche, description, couverture, liens), vide par défaut.</summary>
    public PublicProfile PublicProfile { get; private set; } = PublicProfile.Empty;

    public bool PricesIncludeTax { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Défini uniquement pendant l'état Suspended.</summary>
    public TenantSuspensionReason? SuspensionReason { get; private set; }

    public DateTimeOffset? SuspendedAtUtc { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public bool CanAcceptBusinessWrites => Status is TenantStatus.Trial or TenantStatus.Active;

       // Réservé à l'ORM.
    private Tenant()
    {
        Name = null!;
        DefaultCurrency = null!;
        TimeZone = null!;
        Branding = null!;
    }

    private Tenant(
        TenantId id,
        TenantName name,
        Currency defaultCurrency,
        TimeZoneId timeZone,
        Branding branding,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        Name = name;
        DefaultCurrency = defaultCurrency;
        TimeZone = timeZone;
        Branding = branding;
        PricesIncludeTax = true;
        Status = TenantStatus.Trial;
        CreatedAtUtc = createdAtUtc;
    }

    public static Tenant Create(
        TenantName name,
        Currency defaultCurrency,
        TimeZoneId timeZone,
        IClock clock,
        Branding? branding = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(defaultCurrency);
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;
        var tenant = new Tenant(
            TenantId.New(), name, defaultCurrency, timeZone, branding ?? Branding.Create(name.Value), now);
        tenant.Raise(new TenantCreated(tenant.Id, name.Value, defaultCurrency.Code, timeZone.Value, now));
        return tenant;
    }

    public void EnsureCanAcceptBusinessWrites()
    {
        EnsureNotClosed();
        DomainException.ThrowIf(Status == TenantStatus.Suspended, TenantErrors.Suspended);
    }

    public void Activate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotClosed();
        DomainException.ThrowIf(Status != TenantStatus.Trial, TenantErrors.OnlyTrialCanBeActivated);

        Status = TenantStatus.Active;
        Raise(new TenantActivated(Id, clock.UtcNow));
    }

    public void Suspend(TenantSuspensionReason reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotClosed();
        DomainException.ThrowIf(!Enum.IsDefined(reason), TenantErrors.SuspensionReasonInvalid);
        DomainException.ThrowIf(Status == TenantStatus.Suspended, TenantErrors.AlreadySuspended);

        var now = clock.UtcNow;
        var previous = Status;
        Status = TenantStatus.Suspended;
        SuspensionReason = reason;
        SuspendedAtUtc = now;
        Raise(new TenantSuspended(Id, reason, previous, now));
    }

    /// <summary>Suspended -> Active, quel que soit le statut avant la suspension (un essai suspendu ne reprend pas).</summary>
    public void Reactivate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotClosed();
        DomainException.ThrowIf(Status != TenantStatus.Suspended, TenantErrors.NotSuspended);

        Status = TenantStatus.Active;
        SuspensionReason = null;
        SuspendedAtUtc = null;
        Raise(new TenantReactivated(Id, clock.UtcNow));
    }

    /// <summary>État terminal : accessible depuis tous les autres statuts, jamais quitté.</summary>
    public void Close(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotClosed();

        var now = clock.UtcNow;
        var previous = Status;
        Status = TenantStatus.Closed;
        ClosedAtUtc = now;
        Raise(new TenantClosed(Id, previous, now));
    }

    public void Rename(TenantName newName, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newName);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotClosed();
        if (newName == Name)
        {
            return;
        }

        var previous = Name;
        Name = newName;
        Raise(new TenantRenamed(Id, previous.Value, newName.Value, clock.UtcNow));
    }

    /// <summary>
    /// Uniquement la valeur par défaut des FUTURS documents : les montants déjà enregistrés ailleurs gardent leur
    /// devise. Refusé dès que le catalogue a des prix : ses produits, tarifés dans l'ancienne devise, ne pourraient
    /// plus être commandés.
    /// </summary>
    /// <param name="hasPricedCatalog">Réponse de <see cref="ITenantPricedCatalogChecker"/>, fournie par l'Application.</param>
    public void ChangeDefaultCurrency(Currency newCurrency, bool hasPricedCatalog, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newCurrency);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotClosed();
        if (newCurrency == DefaultCurrency)
        {
            return;
        }

        DomainException.ThrowIf(hasPricedCatalog, TenantErrors.CurrencyLockedByPricedCatalog);

        var previous = DefaultCurrency;
        DefaultCurrency = newCurrency;
        Raise(new TenantDefaultCurrencyChanged(Id, previous.Code, newCurrency.Code, clock.UtcNow));
    }

    /// <summary>Pour les FUTURES commandes uniquement : une commande garde le mode avec lequel elle a été créée.</summary>
    public void ChangePriceTaxMode(bool pricesIncludeTax, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotClosed();
        if (pricesIncludeTax == PricesIncludeTax)
        {
            return;
        }

        PricesIncludeTax = pricesIncludeTax;
        Raise(new TenantPriceTaxModeChanged(Id, pricesIncludeTax, clock.UtcNow));
    }

    public void ChangeTimeZone(TimeZoneId newTimeZone, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newTimeZone);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotClosed();
        if (newTimeZone == TimeZone)
        {
            return;
        }

        var previous = TimeZone;
        TimeZone = newTimeZone;
        Raise(new TenantTimeZoneChanged(Id, previous.Value, newTimeZone.Value, clock.UtcNow));
    }

    public void UpdateBranding(Branding newBranding, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newBranding);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotClosed();
        if (newBranding == Branding)
        {
            return;
        }

        Branding = newBranding;
        Raise(new TenantBrandingUpdated(Id, newBranding, clock.UtcNow));
    }

    public void UpdatePublicProfile(PublicProfile newProfile, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newProfile);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotClosed();
        if (newProfile == PublicProfile)
        {
            return;
        }

        PublicProfile = newProfile;
        Raise(new TenantPublicProfileUpdated(Id, clock.UtcNow));
    }

    private void EnsureNotClosed() => DomainException.ThrowIf(Status == TenantStatus.Closed, TenantErrors.Closed);
}
