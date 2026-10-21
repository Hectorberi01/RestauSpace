namespace Notifications.Domain;

/// <summary>
/// Ce qu'UNE personne (client ou utilisateur) accepte de recevoir d'un restaurant, par finalité et par canal. Tout est
/// accepté par défaut, sauf le marketing pour un utilisateur (il n'en reçoit jamais) ; la personne refuse (opt-out) ou
/// accepte de nouveau (opt-in) canal par canal. Une PRÉFÉRENCE N'EST PAS UN CONSENTEMENT : accepter le marketing ici
/// ne dispense jamais de la preuve de consentement de Customers, et accepter le transactionnel n'ouvre rien au marketing.
/// Les messages essentiels (sécurité du compte) passent outre (<see cref="NotificationTypeDefinition.IsEssential"/>).
/// </summary>
public sealed class NotificationPreference : AggregateRoot<NotificationPreferenceId>, ITenantOwned
{
    private readonly List<ChannelPreference> _choices = [];
    private List<string> _mutedTypes = [];

    private NotificationPreference(NotificationPreferenceId id, TenantId tenantId, RecipientId owner, DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        Owner = owner;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private NotificationPreference() => Owner = null!;
    public TenantId TenantId { get; private set; }
    public RecipientId Owner { get; private set; }
    /// <summary>Les choix exprimés (une ligne au plus par finalité et canal) ; un couple absent garde sa valeur par défaut.</summary>
    public IReadOnlyCollection<ChannelPreference> Choices => _choices.AsReadOnly();

    /// <summary>
    /// Les alertes d'équipe (<c>staff.*</c>) que l'utilisateur ne veut plus recevoir, sur aucun canal. Un type absent suit
    /// les choix par canal.
    /// </summary>
    public IReadOnlyList<string> MutedTypes => _mutedTypes.AsReadOnly();

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <param name="isFirstForOwner">Réponse de l'Application : cette personne n'a pas encore de préférences dans ce restaurant.</param>
    public static NotificationPreference Create(TenantId tenantId, RecipientId owner, bool isFirstForOwner, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(!isFirstForOwner, NotificationPreferenceErrors.AlreadyExists);

        return new NotificationPreference(NotificationPreferenceId.New(), tenantId, owner, clock.UtcNow);
    }

    /// <summary>Valeur par défaut d'un couple jamais choisi : accepté, sauf le marketing pour un utilisateur.</summary>
    public static bool AllowedByDefault(RecipientId owner, NotificationPurpose purpose)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return purpose == NotificationPurpose.Transactional || owner.IsCustomer;
    }

    /// <summary>La personne accepte-t-elle cette finalité sur ce canal (sans compter le consentement marketing) ?</summary>
    public bool Allows(NotificationPurpose purpose, NotificationChannel channel) =>
        Find(purpose, channel)?.IsAllowed ?? AllowedByDefault(Owner, purpose);

    public bool IsMuted(TemplateKey type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _mutedTypes.Contains(type.Value, StringComparer.Ordinal);
    }

    /// <summary>Coupe (ou rétablit) un type d'alerte d'équipe pour cet utilisateur, tous canaux confondus.</summary>
    public void SetTypeMuted(NotificationTypeDefinition type, bool muted, PreferenceSource source, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Owner.IsCustomer, NotificationPreferenceErrors.TypesForStaffOnly);
        DomainException.ThrowIf(!IsStaffAlert(type), NotificationPreferenceErrors.TypeNotMutable);
        if (IsMuted(type.Key) == muted)
        {
            return;
        }

        if (muted)
        {
            _mutedTypes = [.. _mutedTypes, type.Key.Value];
        }
        else
        {
            _mutedTypes = _mutedTypes.Where(value => value != type.Key.Value).ToList();
        }

        UpdatedAtUtc = clock.UtcNow;
        Raise(new NotificationTypeMuteChanged(TenantId, Id, Owner.Id, type.Key.Value, muted, source.Value, UpdatedAtUtc));
    }

    /// <summary>Les types qu'un utilisateur peut couper : les alertes d'équipe non essentielles.</summary>
    public static bool IsStaffAlert(NotificationTypeDefinition type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return !type.IsEssential && !type.IsMarketing && type.Key.Value.StartsWith("staff.", StringComparison.Ordinal);
    }

    /// <summary>Refuse cette finalité sur ce canal. Rejouer (deux clics sur un lien de désinscription) ne change rien.</summary>
    public void OptOut(NotificationPurpose purpose, NotificationChannel channel, PreferenceSource source, IClock clock) =>
        Set(purpose, channel, isAllowed: false, source, clock);

    /// <summary>Accepte de nouveau. Pour le marketing, le consentement (Customers) reste nécessaire à chaque envoi.</summary>
    public void OptIn(NotificationPurpose purpose, NotificationChannel channel, PreferenceSource source, IClock clock) =>
        Set(purpose, channel, isAllowed: true, source, clock);

    /// <summary>Désinscription de TOUT le marketing, sur tous les canaux (lien « ne plus rien recevoir »).</summary>
    public void OptOutOfAllMarketing(PreferenceSource source, IClock clock)
    {
        foreach (var channel in NotificationChannels.All)
        {
            OptOut(NotificationPurpose.Marketing, channel, source, clock);
        }
    }

    private void Set(NotificationPurpose purpose, NotificationChannel channel, bool isAllowed, PreferenceSource source, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!Enum.IsDefined(purpose), NotificationPreferenceErrors.PurposeUnknown);
        NotificationChannels.Require(channel);
        DomainException.ThrowIf(
            purpose == NotificationPurpose.Marketing && !Owner.IsCustomer,
            NotificationPreferenceErrors.MarketingForCustomersOnly);
        if (Allows(purpose, channel) == isAllowed)
        {
            return;
        }

        var now = clock.UtcNow;
        _choices.RemoveAll(choice => choice.Purpose == purpose && choice.Channel == channel);
        _choices.Add(new ChannelPreference(purpose, channel, isAllowed, now, source));
        UpdatedAtUtc = now;
        Raise(isAllowed
            ? new NotificationOptedIn(TenantId, Id, Owner.Kind, Owner.Id, purpose, channel, source.Value, now)
            : new NotificationOptedOut(TenantId, Id, Owner.Kind, Owner.Id, purpose, channel, source.Value, now));
    }

    private ChannelPreference? Find(NotificationPurpose purpose, NotificationChannel channel) =>
        _choices.SingleOrDefault(choice => choice.Purpose == purpose && choice.Channel == channel);
}
