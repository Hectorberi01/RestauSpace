namespace Payments.Domain;

/// <summary>
/// Ce qu'UN restaurant accepte d'un prestataire du catalogue de la plateforme (<see cref="PaymentProvider"/>) : un par
/// prestataire et par restaurant. Il fixe le mode (compte de la plateforme ou du restaurant), garde la RÉFÉRENCE des
/// identifiants dans le coffre (jamais un secret), les moyens du prestataire que le restaurant retient, et leur
/// affectation aux établissements. Sans affectation, la configuration vaut pour tous les établissements du restaurant ;
/// avec, seulement pour les établissements affectés.
/// </summary>
public sealed class RestaurantPaymentConfiguration : AggregateRoot<RestaurantPaymentConfigurationId>, ITenantOwned
{
    public const int MaxAssignments = 500;

    private readonly List<PaymentMethodAssignment> _assignments = [];

    // Pas readonly : l'ORM le pose au chargement (stocké en une seule valeur jsonb). Jamais réassigné par le domaine.
    private List<PaymentMethodType> _methods = [];

    private RestaurantPaymentConfiguration(
        RestaurantPaymentConfigurationId id,
        TenantId tenantId,
        PaymentProvider provider,
        PaymentMode mode,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        PaymentProviderId = provider.Id;
        Provider = provider.Code;
        ProviderType = provider.Type;
        Mode = mode;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private RestaurantPaymentConfiguration() => Provider = null!;

    public TenantId TenantId { get; private set; }

    public PaymentProviderId PaymentProviderId { get; private set; }

    /// <summary>Code du prestataire, recopié à la création (il ne change jamais) : rapprochement des paiements.</summary>
    public PaymentProviderCode Provider { get; private set; }

    /// <summary>Recopiée à la création : la nature d'un prestataire ne change jamais.</summary>
    public PaymentProviderType ProviderType { get; private set; }

    /// <summary>Sans objet sur place (toujours <see cref="PaymentMode.TenantManaged"/> : l'argent est dans la caisse du restaurant).</summary>
    public PaymentMode Mode { get; private set; }

    public PaymentConfigurationStatus Status { get; private set; }

    /// <summary>Activée par le restaurant. Utilisable seulement si <see cref="Status"/> vaut <see cref="PaymentConfigurationStatus.Configured"/>.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Où sont rangés les identifiants du compte marchand du restaurant ; null sur place ou en mode plateforme.</summary>
    public PaymentCredentialReference? SecretReference { get; private set; }

    /// <summary>Moyens du prestataire que le restaurant retient (au moins un), triés.</summary>
    public IReadOnlyList<PaymentMethodType> EnabledMethods => _methods.AsReadOnly();

    public IReadOnlyCollection<PaymentMethodAssignment> Assignments => _assignments.AsReadOnly();

    /// <summary>Vrai tant qu'aucun établissement n'est affecté : la configuration vaut alors pour tous.</summary>
    public bool IsGlobal => _assignments.Count == 0;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Le restaurant accepte ce prestataire, proposé par la plateforme, avec tous ses moyens. Sur place : configurée et
    /// active aussitôt. En ligne : inactive ; prête aussitôt en mode plateforme (si le prestataire l'autorise), en attente
    /// des identifiants en mode restaurant.
    /// </summary>
    /// <param name="isFirstForTenant">Réponse de l'Application : le restaurant n'a pas encore ce prestataire.</param>
    public static RestaurantPaymentConfiguration Create(
        TenantId tenantId, PaymentProvider provider, PaymentMode mode, bool isFirstForTenant, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(!provider.IsEnabled, PaymentConfigurationErrors.ProviderUnavailable);
        DomainException.ThrowIf(!isFirstForTenant, PaymentConfigurationErrors.AlreadyExists);
        EnsureModeAllowed(provider.Type, provider.Capability, mode);

        var now = clock.UtcNow;
        var configuration = new RestaurantPaymentConfiguration(RestaurantPaymentConfigurationId.New(), tenantId, provider, mode, now)
        {
            Status = provider.Type == PaymentProviderType.OnSite || mode == PaymentMode.PlatformManaged
                ? PaymentConfigurationStatus.Configured
                : PaymentConfigurationStatus.AwaitingCredentials,
            IsEnabled = provider.Type == PaymentProviderType.OnSite,
        };
        configuration._methods = provider.Capability.SupportedMethods.ToList();
        configuration.Raise(new PaymentConfigurationCreated(
            tenantId, configuration.Id, provider.Id, provider.Code, mode, configuration.IsEnabled, now));
        return configuration;
    }

    /// <summary>
    /// Mode restaurant : les identifiants viennent d'être rangés dans le coffre à cette référence (première saisie ou
    /// rotation). La configuration devient prête. L'événement ne porte que la référence, jamais un secret.
    /// </summary>
    public void Configure(PaymentCredentialReference secretReference, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(secretReference);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(
            ProviderType != PaymentProviderType.Online || Mode != PaymentMode.TenantManaged,
            PaymentConfigurationErrors.CredentialsNotApplicable);
        DomainException.ThrowIf(secretReference.TenantId != TenantId, CommonErrors.TenantMismatch);

        SecretReference = secretReference;
        Status = PaymentConfigurationStatus.Configured;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new PaymentConfigurationConfigured(TenantId, Id, Provider, UpdatedAtUtc));
    }

    /// <summary>
    /// Passe du compte de la plateforme à celui du restaurant, ou l'inverse (en ligne seulement). Vers le mode restaurant
    /// sans identifiants déjà rangés : la configuration attend ses identifiants et se désactive.
    /// </summary>
    public void ChangeMode(PaymentMode mode, PaymentProvider provider, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(provider.Id != PaymentProviderId, PaymentConfigurationErrors.ProviderMismatch);
        DomainException.ThrowIf(ProviderType == PaymentProviderType.OnSite, PaymentConfigurationErrors.ModeNotApplicable);
        EnsureModeAllowed(ProviderType, provider.Capability, mode);
        if (mode == Mode)
        {
            return;
        }

        var now = clock.UtcNow;
        Mode = mode;
        Status = mode == PaymentMode.PlatformManaged || SecretReference is not null
            ? PaymentConfigurationStatus.Configured
            : PaymentConfigurationStatus.AwaitingCredentials;
        UpdatedAtUtc = now;
        Raise(new PaymentConfigurationModeChanged(TenantId, Id, Provider, mode, Status, now));
        if (IsEnabled && Status != PaymentConfigurationStatus.Configured)
        {
            IsEnabled = false;
            Raise(new PaymentConfigurationDisabled(TenantId, Id, Provider, now));
        }
    }

    /// <param name="providerIsAvailable">
    /// Le prestataire est encore proposé par la plateforme (et, en ligne, son intégration existe) : réponse de l'Application.
    /// </param>
    public void Enable(bool providerIsAvailable, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(IsEnabled, PaymentConfigurationErrors.AlreadyEnabled);
        DomainException.ThrowIf(!providerIsAvailable, PaymentConfigurationErrors.ProviderUnavailable);
        DomainException.ThrowIf(Status != PaymentConfigurationStatus.Configured, PaymentConfigurationErrors.CredentialsMissing);

        IsEnabled = true;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new PaymentConfigurationEnabled(TenantId, Id, Provider, UpdatedAtUtc));
    }

    /// <summary>Plus de nouveau paiement par ce prestataire ; ceux en cours continuent d'être suivis (webhooks, remboursements).</summary>
    public void Disable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsEnabled, PaymentConfigurationErrors.AlreadyDisabled);

        IsEnabled = false;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new PaymentConfigurationDisabled(TenantId, Id, Provider, UpdatedAtUtc));
    }

    /// <summary>Retient un moyen que le prestataire offre (sans effet s'il est déjà retenu).</summary>
    public void ActivatePaymentMethod(PaymentMethodType method, PaymentProvider provider, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(provider.Id != PaymentProviderId, PaymentConfigurationErrors.ProviderMismatch);
        DomainException.ThrowIf(!provider.Capability.Supports(method), PaymentConfigurationErrors.MethodNotOffered);
        if (_methods.Contains(method))
        {
            return;
        }

        _methods.Add(method);
        _methods.Sort();
        UpdatedAtUtc = clock.UtcNow;
        Raise(new PaymentConfigurationMethodActivated(TenantId, Id, Provider, method, UpdatedAtUtc));
    }

    /// <summary>
    /// Ne retient plus ce moyen (il en faut toujours un) ; ses affectations aux établissements sont retirées avec lui.
    /// Sans effet s'il n'était pas retenu.
    /// </summary>
    public void DeactivatePaymentMethod(PaymentMethodType method, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (!_methods.Contains(method))
        {
            return;
        }

        DomainException.ThrowIf(_methods.Count == 1, PaymentConfigurationErrors.LastMethod);
        _methods.Remove(method);
        _assignments.RemoveAll(assignment => assignment.Method == method);
        UpdatedAtUtc = clock.UtcNow;
        Raise(new PaymentConfigurationMethodDeactivated(TenantId, Id, Provider, method, UpdatedAtUtc));
    }

    /// <summary>
    /// Propose un moyen retenu dans un établissement du MÊME restaurant (ajoute ou met à jour son affectation : rang,
    /// bornes de montant, actif). La première affectation fait passer la configuration de « tous les établissements » à
    /// « les établissements affectés ».
    /// </summary>
    public void AssignToLocation(
        LocationReference location,
        PaymentMethodType method,
        int priority,
        IClock clock,
        Money? minAmount = null,
        Money? maxAmount = null,
        bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(clock);
        this.EnsureSameTenant(location.TenantId);
        DomainException.ThrowIf(!_methods.Contains(method), PaymentConfigurationErrors.MethodNotActive);

        var existing = _assignments.SingleOrDefault(assignment => assignment.LocationId == location.LocationId && assignment.Method == method);
        if (existing is null)
        {
            DomainException.ThrowIf(_assignments.Count >= MaxAssignments, PaymentConfigurationErrors.TooManyAssignments);
            var assignment = PaymentMethodAssignment.Create(location.LocationId, method, priority, minAmount, maxAmount);
            if (!isEnabled)
            {
                assignment.Update(priority, minAmount, maxAmount, isEnabled: false);
            }

            _assignments.Add(assignment);
        }
        else if (!existing.Update(priority, minAmount, maxAmount, isEnabled))
        {
            return;
        }

        UpdatedAtUtc = clock.UtcNow;
        Raise(new PaymentMethodAssignedToLocation(TenantId, Id, Provider, location.LocationId, method, UpdatedAtUtc));
    }

    /// <summary>
    /// Retire l'établissement : un seul moyen, ou tous ceux de la configuration si <paramref name="method"/> est null.
    /// Retirer la dernière affectation rend la configuration de nouveau valable pour tous les établissements.
    /// </summary>
    public void RemoveFromLocation(LocationId locationId, IClock clock, PaymentMethodType? method = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(locationId);
        var removed = _assignments.RemoveAll(assignment => assignment.LocationId == locationId && (method is null || assignment.Method == method));
        DomainException.ThrowIf(removed == 0, PaymentConfigurationErrors.AssignmentNotFound);

        UpdatedAtUtc = clock.UtcNow;
        Raise(new PaymentMethodRemovedFromLocation(TenantId, Id, Provider, locationId, method, UpdatedAtUtc));
    }

    /// <summary>
    /// Ce restaurant peut-il prendre un nouveau paiement par ce moyen, dans cet établissement, pour ce montant ? (Le
    /// statut et les capacités du prestataire de la plateforme sont vérifiés à part, par la policy de disponibilité.)
    /// </summary>
    public bool CanTake(LocationId locationId, PaymentMethodType method, Money amount) => Refusal(locationId, method, amount) is null;

    /// <summary>Comme <see cref="CanTake"/>, mais lève l'erreur qui explique le refus. Appelé par <c>Payment</c> à sa création.</summary>
    public void EnsureCanTake(LocationId locationId, PaymentMethodType method, Money amount)
    {
        if (Refusal(locationId, method, amount) is { } error)
        {
            throw new DomainException(error);
        }
    }

    private DomainError? Refusal(LocationId locationId, PaymentMethodType method, Money amount)
    {
        ArgumentNullException.ThrowIfNull(amount);
        if (!IsEnabled)
        {
            return PaymentConfigurationErrors.Disabled;
        }

        if (Status != PaymentConfigurationStatus.Configured)
        {
            return PaymentConfigurationErrors.CredentialsMissing;
        }

        if (!_methods.Contains(method))
        {
            return PaymentConfigurationErrors.MethodNotAccepted;
        }

        if (IsGlobal)
        {
            return null;
        }

        var assignment = _assignments.SingleOrDefault(candidate => candidate.LocationId == locationId && candidate.Method == method);
        if (assignment is not { IsEnabled: true })
        {
            return PaymentConfigurationErrors.NotAssignedToLocation;
        }

        return assignment.Allows(amount) ? null : PaymentConfigurationErrors.AmountOutOfBounds;
    }

    /// <summary>Rang d'affichage du moyen dans l'établissement (0 sans affectation).</summary>
    public int PriorityAt(LocationId locationId, PaymentMethodType method) =>
        _assignments.SingleOrDefault(candidate => candidate.LocationId == locationId && candidate.Method == method)?.Priority ?? 0;

    private static void EnsureModeAllowed(PaymentProviderType type, PaymentProviderCapability capability, PaymentMode mode)
    {
        DomainException.ThrowIf(!Enum.IsDefined(mode), PaymentConfigurationErrors.ModeUnknown);
        DomainException.ThrowIf(type == PaymentProviderType.OnSite && mode != PaymentMode.TenantManaged, PaymentConfigurationErrors.ModeNotApplicable);
        DomainException.ThrowIf(
            mode == PaymentMode.PlatformManaged && !capability.SupportsPlatformManagedMode,
            PaymentConfigurationErrors.PlatformModeNotAllowed);
    }
}
