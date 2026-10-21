namespace Notifications.Domain;

/// <summary>
/// Ce qu'UN restaurant utilise d'un prestataire du catalogue de la plateforme (<see cref="NotificationProvider"/>) : une
/// configuration par prestataire et par restaurant (plusieurs prestataires d'un même canal = repli, par
/// <see cref="NotificationChannelAssignment.Priority"/>). Elle fixe le mode (compte de la plateforme ou du restaurant),
/// garde la RÉFÉRENCE des identifiants dans le coffre (jamais un secret) et ses établissements. Sans affectation, elle
/// vaut pour tous les établissements du restaurant ; avec, seulement pour les établissements affectés.
/// </summary>
public sealed class RestaurantNotificationConfiguration : AggregateRoot<RestaurantNotificationConfigurationId>, ITenantOwned
{
    public const int MaxAssignments = 500;

    private readonly List<NotificationChannelAssignment> _assignments = [];

    private RestaurantNotificationConfiguration(
        RestaurantNotificationConfigurationId id,
        TenantId tenantId,
        NotificationProvider provider,
        NotificationProviderMode mode,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        NotificationProviderId = provider.Id;
        Provider = provider.Code;
        Channel = provider.Channel;
        Mode = mode;
        Status = InitialStatus(mode);
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private RestaurantNotificationConfiguration() => Provider = null!;

    public TenantId TenantId { get; private set; }

    public NotificationProviderId NotificationProviderId { get; private set; }

    /// <summary>Code du prestataire, recopié (et mis à jour par <see cref="ChangeProvider"/>) : rapprochement des envois.</summary>
    public NotificationProviderCode Provider { get; private set; }

    /// <summary>Le canal du prestataire ; ne change jamais, même avec <see cref="ChangeProvider"/>.</summary>
    public NotificationChannel Channel { get; private set; }

    public NotificationProviderMode Mode { get; private set; }

    public NotificationConfigurationStatus Status { get; private set; }

    /// <summary>Activée par le restaurant. Utilisable seulement si <see cref="Status"/> vaut <see cref="NotificationConfigurationStatus.Configured"/>.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Où sont rangés les identifiants du compte du restaurant ; null en mode plateforme et avant la saisie.</summary>
    public NotificationCredentialReference? SecretReference { get; private set; }

    public IReadOnlyCollection<NotificationChannelAssignment> Assignments => _assignments.AsReadOnly();

    /// <summary>Vrai tant qu'aucun établissement n'est affecté : la configuration vaut alors pour tous.</summary>
    public bool IsGlobal => _assignments.Count == 0;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Le restaurant choisit ce prestataire, proposé par la plateforme, dans un mode que la plateforme autorise. Créée
    /// INACTIVE : prête aussitôt en mode plateforme, en attente des identifiants en mode restaurant ; le restaurant
    /// l'active ensuite (<see cref="Enable"/>).
    /// </summary>
    /// <param name="isFirstForTenant">Réponse de l'Application : le restaurant n'a pas encore de configuration pour ce prestataire.</param>
    public static RestaurantNotificationConfiguration Create(
        TenantId tenantId, NotificationProvider provider, NotificationProviderMode mode, bool isFirstForTenant, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(!provider.IsEnabled, NotificationConfigurationErrors.ProviderUnavailable);
        EnsureModeAllowed(provider, mode);
        DomainException.ThrowIf(!isFirstForTenant, NotificationConfigurationErrors.AlreadyExists);

        var now = clock.UtcNow;
        var configuration = new RestaurantNotificationConfiguration(RestaurantNotificationConfigurationId.New(), tenantId, provider, mode, now);
        configuration.Raise(new NotificationConfigurationCreated(
            tenantId, configuration.Id, provider.Id, provider.Code, provider.Channel, mode, now));
        return configuration;
    }

    /// <summary>
    /// Mode restaurant : les identifiants du compte du restaurant viennent d'être rangés au coffre à cette référence
    /// (première saisie ou rotation), pour CE restaurant et CE prestataire. La configuration devient prête. L'événement ne
    /// porte aucun secret.
    /// </summary>
    public void Configure(NotificationCredentialReference secretReference, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(secretReference);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Mode != NotificationProviderMode.TenantManaged, NotificationConfigurationErrors.CredentialsNotApplicable);
        DomainException.ThrowIf(secretReference.TenantId != TenantId, CommonErrors.TenantMismatch);
        DomainException.ThrowIf(secretReference.Provider != Provider, NotificationConfigurationErrors.CredentialsForAnotherProvider);

        SecretReference = secretReference;
        Status = NotificationConfigurationStatus.Configured;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new NotificationConfigurationConfigured(TenantId, Id, Provider, UpdatedAtUtc));
    }

    /// <summary>
    /// Passe du compte de la plateforme à celui du restaurant, ou l'inverse, si la plateforme l'autorise. Vers le mode
    /// restaurant sans identifiants déjà rangés : la configuration attend ses identifiants et se désactive. Vers le mode
    /// plateforme : la référence du coffre est gardée (le compte du restaurant n'est pas oublié). Sans effet si le mode
    /// ne change pas.
    /// </summary>
    public void ChangeMode(NotificationProviderMode mode, NotificationProvider provider, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(provider.Id != NotificationProviderId, NotificationConfigurationErrors.ProviderMismatch);
        EnsureModeAllowed(provider, mode);
        if (mode == Mode)
        {
            return;
        }

        var now = clock.UtcNow;
        Mode = mode;
        Status = mode == NotificationProviderMode.PlatformManaged || SecretReference is not null
            ? NotificationConfigurationStatus.Configured
            : NotificationConfigurationStatus.AwaitingCredentials;
        UpdatedAtUtc = now;
        Raise(new NotificationConfigurationModeChanged(TenantId, Id, Provider, mode, Status, now));
        DisableIfNotReady(now);
    }

    /// <summary>
    /// Remplace le prestataire par un autre du MÊME canal (le restaurant change de fournisseur d'emails, par exemple) en
    /// gardant ses établissements. Les identifiants de l'ancien prestataire ne valent pas pour le nouveau : la référence
    /// du coffre est oubliée, et en mode restaurant la configuration attend les nouveaux identifiants (et se désactive).
    /// </summary>
    /// <param name="isFirstForTenant">Réponse de l'Application : le restaurant n'a pas déjà une configuration pour le nouveau prestataire.</param>
    public void ChangeProvider(NotificationProvider provider, NotificationProviderMode mode, bool isFirstForTenant, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(clock);
        if (provider.Id == NotificationProviderId)
        {
            ChangeMode(mode, provider, clock);
            return;
        }

        DomainException.ThrowIf(provider.Channel != Channel, NotificationConfigurationErrors.ChannelMismatch);
        DomainException.ThrowIf(!provider.IsEnabled, NotificationConfigurationErrors.ProviderUnavailable);
        EnsureModeAllowed(provider, mode);
        DomainException.ThrowIf(!isFirstForTenant, NotificationConfigurationErrors.AlreadyExists);

        var now = clock.UtcNow;
        var previous = Provider;
        NotificationProviderId = provider.Id;
        Provider = provider.Code;
        Mode = mode;
        SecretReference = null;
        Status = InitialStatus(mode);
        foreach (var assignment in _assignments)
        {
            assignment.MoveTo(provider.Id);
        }

        UpdatedAtUtc = now;
        Raise(new NotificationConfigurationProviderChanged(TenantId, Id, previous, provider.Id, provider.Code, Status, now));
        DisableIfNotReady(now);
    }

    /// <summary>
    /// Active la configuration : le prestataire doit être encore proposé par la plateforme, le mode encore autorisé (le
    /// contrat a pu changer depuis) et les identifiants saisis en mode restaurant.
    /// </summary>
    public void Enable(NotificationProvider provider, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(provider.Id != NotificationProviderId, NotificationConfigurationErrors.ProviderMismatch);
        DomainException.ThrowIf(IsEnabled, NotificationConfigurationErrors.AlreadyEnabled);
        DomainException.ThrowIf(!provider.IsEnabled, NotificationConfigurationErrors.ProviderUnavailable);
        EnsureModeAllowed(provider, Mode);
        DomainException.ThrowIf(Status != NotificationConfigurationStatus.Configured, NotificationConfigurationErrors.CredentialsMissing);

        IsEnabled = true;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new NotificationConfigurationEnabled(TenantId, Id, Provider, Channel, UpdatedAtUtc));
    }

    /// <summary>Plus aucun nouvel envoi par ce prestataire ; ceux en cours restent suivis (statuts, webhooks).</summary>
    public void Disable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsEnabled, NotificationConfigurationErrors.AlreadyDisabled);

        IsEnabled = false;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new NotificationConfigurationDisabled(TenantId, Id, Provider, Channel, UpdatedAtUtc));
    }

    /// <summary>
    /// Propose ce canal, par ce prestataire, dans un établissement du MÊME restaurant (ajoute ou met à jour son
    /// affectation : rang, actif). La première affectation fait passer la configuration de « tous les établissements » à
    /// « les établissements affectés ». Sans effet ni événement si rien ne change.
    /// </summary>
    public void AssignToLocation(LocationReference location, int priority, IClock clock, bool isEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(clock);
        this.EnsureSameTenant(location.TenantId);

        var existing = _assignments.SingleOrDefault(assignment => assignment.LocationId == location.LocationId);
        if (existing is null)
        {
            DomainException.ThrowIf(_assignments.Count >= MaxAssignments, NotificationConfigurationErrors.TooManyAssignments);
            _assignments.Add(NotificationChannelAssignment.Create(location.LocationId, Channel, NotificationProviderId, priority, isEnabled));
        }
        else if (!existing.Update(priority, isEnabled))
        {
            return;
        }

        UpdatedAtUtc = clock.UtcNow;
        Raise(new NotificationChannelAssignedToLocation(TenantId, Id, Channel, location.LocationId, priority, isEnabled, UpdatedAtUtc));
    }

    /// <summary>Retirer la dernière affectation rend la configuration de nouveau valable pour tous les établissements.</summary>
    public void RemoveFromLocation(LocationId locationId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(locationId);
        DomainException.ThrowIf(
            _assignments.RemoveAll(assignment => assignment.LocationId == locationId) == 0,
            NotificationConfigurationErrors.AssignmentNotFound);

        UpdatedAtUtc = clock.UtcNow;
        Raise(new NotificationChannelRemovedFromLocation(TenantId, Id, Channel, locationId, UpdatedAtUtc));
    }

    /// <summary>
    /// Cette configuration peut-elle envoyer pour cet établissement (null : envoi du restaurant, sans établissement) ? Le
    /// statut, le contrat et les capacités du prestataire de la plateforme sont vérifiés à part, par la policy d'envoi.
    /// </summary>
    public bool CanServe(LocationId? locationId) => Refusal(locationId) is null;

    /// <summary>Comme <see cref="CanServe"/>, mais lève l'erreur qui explique le refus.</summary>
    public void EnsureCanServe(LocationId? locationId)
    {
        if (Refusal(locationId) is { } error)
        {
            throw new DomainException(error);
        }
    }

    /// <summary>Rang du prestataire dans l'établissement (0 sans affectation) : le plus petit est essayé d'abord.</summary>
    public int PriorityAt(LocationId locationId) =>
        _assignments.SingleOrDefault(assignment => assignment.LocationId == locationId)?.Priority ?? 0;

    private DomainError? Refusal(LocationId? locationId)
    {
        if (!IsEnabled)
        {
            return NotificationConfigurationErrors.Disabled;
        }

        if (Status != NotificationConfigurationStatus.Configured)
        {
            return NotificationConfigurationErrors.CredentialsMissing;
        }

        if (IsGlobal || locationId is not { } location)
        {
            return null;
        }

        return _assignments.SingleOrDefault(assignment => assignment.LocationId == location) is { IsEnabled: true }
            ? null
            : NotificationConfigurationErrors.NotAssignedToLocation;
    }

    private void DisableIfNotReady(DateTimeOffset now)
    {
        if (IsEnabled && Status != NotificationConfigurationStatus.Configured)
        {
            IsEnabled = false;
            Raise(new NotificationConfigurationDisabled(TenantId, Id, Provider, Channel, now));
        }
    }

    private static NotificationConfigurationStatus InitialStatus(NotificationProviderMode mode) =>
        mode == NotificationProviderMode.PlatformManaged
            ? NotificationConfigurationStatus.Configured
            : NotificationConfigurationStatus.AwaitingCredentials;

    private static void EnsureModeAllowed(NotificationProvider provider, NotificationProviderMode mode)
    {
        DomainException.ThrowIf(!Enum.IsDefined(mode), NotificationConfigurationErrors.ModeUnknown);
        DomainException.ThrowIf(!mode.IsAllowedBy(provider.ConfigurationType), NotificationConfigurationErrors.ModeNotAllowed);
    }
}
