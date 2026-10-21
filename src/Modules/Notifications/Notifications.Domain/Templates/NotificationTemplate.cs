namespace Notifications.Domain;

/// <summary>
/// Une version d'un modèle pour (tenant ou plateforme, type de notification, canal, langue). Une version est modifiable
/// tant qu'elle est Draft et figée une fois Published (elle prouve ce qui a été envoyé) ; changer un contenu publié
/// revient à créer la version suivante avec <see cref="CreateNextVersion"/>. Le type (<see cref="Key"/>) vient du
/// catalogue <see cref="NotificationTypes"/> et borne les variables utilisables. Un restaurant peut DÉSACTIVER son modèle :
/// ses notifications reprennent alors celui de la plateforme (<see cref="NotificationTemplateResolution"/>).
/// Appartient à un tenant, ou à la plateforme quand <see cref="TenantId"/> est null (le repli de tout tenant qui n'a pas
/// de modèle publié et actif à lui) — d'où l'absence de <c>ITenantOwned</c>.
/// </summary>
public sealed class NotificationTemplate : AggregateRoot<TemplateId>
{
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 20_000;

    private NotificationTemplate(
        TemplateId id,
        TenantId? tenantId,
        TemplateKey key,
        NotificationChannel channel,
        NotificationLanguage language,
        string? subject,
        string body,
        int version,
        bool isEnabled,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        Key = key;
        Channel = channel;
        Language = language;
        Subject = subject;
        Body = body;
        Version = version;
        Status = TemplateStatus.Draft;
        IsEnabled = isEnabled;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private NotificationTemplate()
    {
        Key = null!;
        Language = null!;
        Body = null!;
    }

    /// <summary>Null pour un modèle de niveau plateforme.</summary>
    public TenantId? TenantId { get; private set; }

    public bool IsPlatformLevel => TenantId is null;

    /// <summary>Le type de notification (<c>reservation.confirmed</c>...), connu du catalogue <see cref="NotificationTypes"/>.</summary>
    public TemplateKey Key { get; private set; }

    /// <summary>Définition du type : finalité, priorité, variables autorisées.</summary>
    public NotificationTypeDefinition Type => NotificationTypes.Require(Key);

    public NotificationChannel Channel { get; private set; }

    public NotificationLanguage Language { get; private set; }

    /// <summary>Sujet (Email, obligatoire) ou titre (Push, InApp, facultatif) ; toujours null pour Sms et WhatsApp.</summary>
    public string? Subject { get; private set; }

    public string Body { get; private set; }

    public int Version { get; private set; }

    public TemplateStatus Status { get; private set; }

    /// <summary>Vrai à la création. Un modèle désactivé ne sert plus (le restaurant retombe sur celui de la plateforme).</summary>
    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    /// <summary>Les variables utilisées par le sujet et le corps, dans l'ordre de première apparition.</summary>
    public IReadOnlyList<string> Variables =>
        TemplateVariables.Extract(Subject).Concat(TemplateVariables.Extract(Body)).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Crée la version 1 d'un modèle d'un tenant, en Draft.</summary>
    /// <param name="isKeyUniqueForChannelAndLanguage">Réponse de <see cref="ITemplateUniquenessChecker.IsKeyUniqueAsync"/>.</param>
    public static NotificationTemplate Create(
        TenantId tenantId,
        TemplateKey key,
        NotificationChannel channel,
        NotificationLanguage language,
        string? subject,
        string? body,
        bool isKeyUniqueForChannelAndLanguage,
        IClock clock)
    {
        Guard.NotEmpty(tenantId);

        return CreateFirstVersion(tenantId, key, channel, language, subject, body, isKeyUniqueForChannelAndLanguage, clock);
    }

    /// <summary>Crée la version 1 d'un modèle de niveau plateforme (sans tenant), en Draft.</summary>
    /// <param name="isKeyUniqueForChannelAndLanguage">
    /// Réponse de <see cref="ITemplateUniquenessChecker.IsKeyUniqueAsync"/> interrogé avec un tenant null.
    /// </param>
    public static NotificationTemplate CreatePlatformTemplate(
        TemplateKey key,
        NotificationChannel channel,
        NotificationLanguage language,
        string? subject,
        string? body,
        bool isKeyUniqueForChannelAndLanguage,
        IClock clock) =>
        CreateFirstVersion(null, key, channel, language, subject, body, isKeyUniqueForChannelAndLanguage, clock);

    /// <summary>
    /// Démarre la version suivante comme copie Draft de celle-ci (même activation). Cette version reste intacte : elle
    /// continue de servir jusqu'à la publication de la nouvelle (archiver l'ancienne relève alors de l'Application).
    /// </summary>
    /// <param name="isNextVersionUnique">Réponse de <see cref="ITemplateUniquenessChecker.IsVersionUniqueAsync"/> pour Version + 1.</param>
    public NotificationTemplate CreateNextVersion(bool isNextVersionUnique, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == TemplateStatus.Draft, TemplateErrors.DraftCannotBeVersioned);
        DomainException.ThrowIf(!isNextVersionUnique, TemplateErrors.VersionAlreadyExists);

        return new NotificationTemplate(
            TemplateId.New(), TenantId, Key, Channel, Language, Subject, Body, Version + 1, IsEnabled, clock.UtcNow);
    }

    /// <summary>
    /// Remplace le sujet et le corps d'un Draft (mêmes règles qu'à la création, variables comprises). Un modèle publié ne
    /// change pas : créer la version suivante. Sans effet si le contenu est identique.
    /// </summary>
    public void UpdateContent(string? subject, string? body, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureDraft();
        var (validSubject, validBody) = ValidateContent(Key, Channel, subject, body);
        if (validSubject == Subject && validBody == Body)
        {
            return;
        }

        Subject = validSubject;
        Body = validBody;
        UpdatedAtUtc = clock.UtcNow;
    }

    public void Publish(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureDraft();

        var now = clock.UtcNow;
        Status = TemplateStatus.Published;
        PublishedAtUtc = now;
        UpdatedAtUtc = now;

        Raise(new TemplatePublished(TenantId, Id, Key.Value, Channel, Language.Value, Version, now));
    }

    /// <summary>Remet le modèle en service (Draft ou Published ; un modèle archivé ne revient jamais).</summary>
    public void Enable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == TemplateStatus.Archived, TemplateErrors.Archived);
        DomainException.ThrowIf(IsEnabled, TemplateErrors.AlreadyEnabled);

        IsEnabled = true;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new TemplateEnabled(TenantId, Id, Key.Value, Channel, Language.Value, Version, UpdatedAtUtc));
    }

    /// <summary>
    /// Met le modèle hors service sans le retirer : pour un restaurant, ses notifications reprennent le modèle de la
    /// plateforme ; pour la plateforme, plus aucun repli pour ce type, ce canal et cette langue.
    /// </summary>
    public void Disable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == TemplateStatus.Archived, TemplateErrors.Archived);
        DomainException.ThrowIf(!IsEnabled, TemplateErrors.AlreadyDisabled);

        IsEnabled = false;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new TemplateDisabled(TenantId, Id, Key.Value, Channel, Language.Value, Version, UpdatedAtUtc));
    }

    public void Archive(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == TemplateStatus.Archived, TemplateErrors.Archived);

        var now = clock.UtcNow;
        Status = TemplateStatus.Archived;
        ArchivedAtUtc = now;
        UpdatedAtUtc = now;

        Raise(new TemplateArchived(TenantId, Id, Key.Value, Channel, Language.Value, Version, now));
    }

    /// <summary>Publié et actif : ce modèle peut servir à un envoi.</summary>
    public bool IsUsable => Status == TemplateStatus.Published && IsEnabled;

    private static NotificationTemplate CreateFirstVersion(
        TenantId? tenantId,
        TemplateKey key,
        NotificationChannel channel,
        NotificationLanguage language,
        string? subject,
        string? body,
        bool isKeyUniqueForChannelAndLanguage,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!Enum.IsDefined(channel), TemplateErrors.ChannelInvalid);
        var (validSubject, validBody) = ValidateContent(key, channel, subject, body);
        DomainException.ThrowIf(!isKeyUniqueForChannelAndLanguage, TemplateErrors.KeyAlreadyUsed);

        return new NotificationTemplate(
            TemplateId.New(), tenantId, key, channel, language, validSubject, validBody, 1, isEnabled: true, clock.UtcNow);
    }

    private void EnsureDraft()
    {
        DomainException.ThrowIf(Status == TemplateStatus.Published, TemplateErrors.PublishedImmutable);
        DomainException.ThrowIf(Status == TemplateStatus.Archived, TemplateErrors.Archived);
    }

    /// <summary>
    /// Sujet selon le canal, corps borné, variables bien formées et autorisées par le type ; un email marketing contient
    /// le lien de désinscription.
    /// </summary>
    private static (string? Subject, string Body) ValidateContent(TemplateKey key, NotificationChannel channel, string? subject, string? body)
    {
        var type = NotificationTypes.Require(key);
        string? validSubject = null;
        switch (channel)
        {
            case NotificationChannel.Email:
                DomainException.ThrowIf(string.IsNullOrWhiteSpace(subject), TemplateErrors.SubjectRequired);
                validSubject = subject!.Trim();
                break;
            case NotificationChannel.Push or NotificationChannel.InApp:
                validSubject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
                break;
            default:
                DomainException.ThrowIf(!string.IsNullOrWhiteSpace(subject), TemplateErrors.SubjectNotSupported);
                break;
        }

        DomainException.ThrowIf(validSubject is { Length: > MaxSubjectLength }, TemplateErrors.SubjectTooLong);
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(body), TemplateErrors.BodyEmpty);
        DomainException.ThrowIf(body!.Length > MaxBodyLength, TemplateErrors.BodyTooLong);

        DomainException.ThrowIf(TemplateVariables.NotAllowed(type, validSubject, body).Count > 0, TemplateErrors.VariableNotAllowed);
        DomainException.ThrowIf(
            type.IsMarketing && channel == NotificationChannel.Email
            && !TemplateVariables.Extract(body).Contains(NotificationTypes.UnsubscribeUrl, StringComparer.Ordinal),
            TemplateErrors.UnsubscribeLinkRequired);

        return (validSubject, body);
    }
}
