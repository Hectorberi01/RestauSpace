namespace Notifications.Domain;

/// <summary>
/// Un prestataire que la PLATEFORME sait utiliser pour un canal : code unique, nom, canal, comment un restaurant peut le
/// configurer (<see cref="NotificationProviderConfigurationType"/>) et ce qu'il sait faire
/// (<see cref="NotificationProviderCapability"/>). Agrégat de la plateforme, pas d'un tenant : un restaurant choisit
/// parmi eux, il n'en crée jamais. Aucun identifiant ici (coffre, hors agrégat), aucun comportement propre à un
/// prestataire (Infrastructure). Le retirer n'efface rien : les envois en cours restent suivis, aucun nouveau ne part.
/// </summary>
public sealed class NotificationProvider : AggregateRoot<NotificationProviderId>
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 60;

    private NotificationProvider(
        NotificationProviderId id,
        NotificationProviderCode code,
        string name,
        NotificationChannel channel,
        NotificationProviderConfigurationType configurationType,
        NotificationProviderCapability capability,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        Code = code;
        Name = name;
        Channel = channel;
        ConfigurationType = configurationType;
        Capability = capability;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private NotificationProvider()
    {
        Code = null!;
        Name = null!;
        Capability = null!;
    }

    public NotificationProviderCode Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>Fixé à la création : un prestataire d'un autre canal est un autre prestataire.</summary>
    public NotificationChannel Channel { get; private set; }

    public NotificationProviderConfigurationType ConfigurationType { get; private set; }

    public NotificationProviderCapability Capability { get; private set; }

    /// <summary>Proposé aux restaurants. Faux à la création : la plateforme l'active une fois prêt.</summary>
    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>
    /// Ajoute le prestataire au catalogue, NON proposé : la plateforme range d'abord ses propres identifiants (mode
    /// plateforme) ou vérifie l'intégration, puis appelle <see cref="Enable"/>.
    /// </summary>
    /// <param name="isCodeUnique">Réponse de l'Application : aucun autre prestataire ne porte ce code.</param>
    public static NotificationProvider Create(
        NotificationProviderCode code,
        string? name,
        NotificationChannel channel,
        NotificationProviderConfigurationType configurationType,
        NotificationProviderCapability capability,
        bool isCodeUnique,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(clock);
        NotificationChannels.Require(channel);
        EnsureFits(channel, configurationType);
        DomainException.ThrowIf(capability.Channel != channel, NotificationProviderErrors.ChannelMismatch);
        DomainException.ThrowIf(!isCodeUnique, NotificationProviderErrors.CodeNotUnique);

        var now = clock.UtcNow;
        var provider = new NotificationProvider(NotificationProviderId.New(), code, CleanName(name), channel, configurationType, capability, now);
        provider.Raise(new NotificationProviderCreated(provider.Id, code, channel, configurationType, now));
        return provider;
    }

    /// <summary>Sans effet ni événement si le nom ne change pas.</summary>
    public void Rename(string? name, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var cleaned = CleanName(name);
        if (cleaned == Name)
        {
            return;
        }

        Name = cleaned;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new NotificationProviderRenamed(Id, Code, cleaned, UpdatedAtUtc));
    }

    /// <summary>
    /// Remplace ce que le prestataire sait faire (nouveau pays, statut de remise...), dans SON canal. Les envois en cours
    /// ne sont pas touchés. Sans effet ni événement si rien ne change.
    /// </summary>
    public void UpdateCapabilities(NotificationProviderCapability capability, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(capability);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(capability.Channel != Channel, NotificationProviderErrors.ChannelMismatch);
        if (capability == Capability)
        {
            return;
        }

        Capability = capability;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new NotificationProviderCapabilitiesUpdated(Id, Code, UpdatedAtUtc));
    }

    /// <summary>
    /// Nouveau contrat avec le prestataire (il autorise désormais, ou n'autorise plus, l'envoi depuis le compte de la
    /// plateforme). Les configurations des restaurants qui ne le respectent plus ne peuvent plus envoyer : règle de la
    /// configuration (étape 4). Sans effet ni événement si rien ne change.
    /// </summary>
    public void ChangeConfigurationType(NotificationProviderConfigurationType configurationType, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureFits(Channel, configurationType);
        if (configurationType == ConfigurationType)
        {
            return;
        }

        ConfigurationType = configurationType;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new NotificationProviderConfigurationTypeChanged(Id, Code, configurationType, UpdatedAtUtc));
    }

    public void Enable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(IsEnabled, NotificationProviderErrors.AlreadyEnabled);
        IsEnabled = true;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new NotificationProviderEnabled(Id, Code, UpdatedAtUtc));
    }

    public void Disable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsEnabled, NotificationProviderErrors.AlreadyDisabled);
        IsEnabled = false;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new NotificationProviderDisabled(Id, Code, UpdatedAtUtc));
    }

    /// <summary>
    /// Proposé et capable de délivrer vers ce pays (null = pays inconnu, seule la disponibilité compte) : utilisable pour
    /// un nouvel envoi. Le contexte complet (configuration du restaurant, établissement, quotas) est vu par la policy.
    /// </summary>
    public bool CanDeliverTo(CountryCode? country) => IsEnabled && (country is null || Capability.Supports(country));

    /// <summary>InApp est livré en interne et seulement lui : un prestataire externe a un compte (plateforme et/ou restaurant).</summary>
    private static void EnsureFits(NotificationChannel channel, NotificationProviderConfigurationType configurationType)
    {
        DomainException.ThrowIf(!Enum.IsDefined(configurationType), NotificationProviderErrors.ConfigurationTypeUnknown);
        DomainException.ThrowIf(
            channel.UsesExternalProvider() == (configurationType == NotificationProviderConfigurationType.Internal),
            NotificationProviderErrors.ConfigurationTypeNotForChannel);
    }

    private static string CleanName(string? name)
    {
        var cleaned = name?.Trim() ?? string.Empty;
        DomainException.ThrowIf(cleaned.Length is < MinNameLength or > MaxNameLength, NotificationProviderErrors.NameInvalid);
        return cleaned;
    }
}
