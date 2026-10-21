namespace Notifications.Domain;

public readonly record struct CampaignId
{
    private CampaignId(Guid value) => Value = value;

    public Guid Value { get; }

    public static CampaignId New() => new(Guid.CreateVersion7());

    public static CampaignId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new CampaignId(value);
    }

    public override string ToString() => Value.ToString();
}

public enum CampaignStatus
{
    Draft = 1,
    Scheduled = 2,
    Sending = 3,
    Completed = 4,
    Cancelled = 5,
}

/// <summary>
/// Envoi groupé d'un message marketing aux clients d'un restaurant qui y ont consenti sur le canal choisi, filtrés par
/// un tag facultatif. Le message est un modèle du catalogue (<c>marketing.*</c>) ; la campagne n'en fournit que les
/// variables communes (titre, lien, date). Le prénom et le lien de désinscription sont ajoutés pour chaque client par
/// l'envoi, jamais saisis ici. L'envoi avance par lots ; chaque client ne reçoit le message qu'une fois (clé
/// d'idempotence par campagne et par client), et la politique d'envoi revérifie consentement, désinscription et
/// fréquence pour chacun.
/// </summary>
public sealed class Campaign : AggregateRoot<CampaignId>, ITenantOwned
{
    public const int NameMaxLength = 100;
    public const int TagMaxLength = 50;

    /// <summary>Variables ajoutées à l'envoi pour chaque client : la campagne ne les fournit jamais.</summary>
    public static readonly IReadOnlySet<string> PerRecipientVariables =
        new HashSet<string>(["customer.firstName", NotificationTypes.UnsubscribeUrl, "loyalty.points"], StringComparer.Ordinal);

    private Dictionary<string, string> _variables = new(StringComparer.Ordinal);

    private Campaign(CampaignId id, TenantId tenantId, DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        CreatedAtUtc = createdAtUtc;
        Status = CampaignStatus.Draft;
        Name = string.Empty;
        TypeKey = string.Empty;
    }

    // Réservé à l'ORM.
    private Campaign()
    {
        Name = string.Empty;
        TypeKey = string.Empty;
    }

    public TenantId TenantId { get; private set; }

    public string Name { get; private set; }

    /// <summary>Clé du type de message (<c>marketing.promotional_campaign</c>...).</summary>
    public string TypeKey { get; private set; }

    public NotificationChannel Channel { get; private set; }

    /// <summary>Seuls les clients portant ce tag ; null : tous les clients qui ont consenti.</summary>
    public string? Tag { get; private set; }

    /// <summary>Établissement dont le prestataire et le nom servent à l'envoi ; null : configuration du restaurant.</summary>
    public LocationId? LocationId { get; private set; }

    /// <summary>Seulement les clients de <see cref="LocationId"/> (au moins une commande dans cet établissement).</summary>
    public bool OnlyLocationCustomers { get; private set; }

    public IReadOnlyDictionary<string, string> Variables => _variables.AsReadOnly();

    public CampaignStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ScheduledForUtc { get; private set; }

    public DateTimeOffset? StartedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>Clients déjà parcourus (position dans l'audience, triée par id).</summary>
    public int Processed { get; private set; }

    /// <summary>Messages créés (ils partent ensuite par la file d'envoi habituelle).</summary>
    public int Created { get; private set; }

    /// <summary>Clients écartés par la politique d'envoi (consentement retiré, désinscription, fréquence, quota...).</summary>
    public int Skipped { get; private set; }

    /// <summary>Parmi les écartés : message marketing déjà reçu récemment (règle de fréquence).</summary>
    public int SkippedForFrequency { get; private set; }

    public static Campaign Create(
        TenantId tenantId, string? name, string typeKey, NotificationChannel channel, string? tag, LocationId? locationId,
        IReadOnlyDictionary<string, string>? variables, IClock clock, bool onlyLocationCustomers = false)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var campaign = new Campaign(CampaignId.New(), Guard.NotEmpty(tenantId), clock.UtcNow);
        campaign.Apply(name, typeKey, channel, tag, locationId, variables, onlyLocationCustomers);
        return campaign;
    }

    public void Change(
        string? name, string typeKey, NotificationChannel channel, string? tag, LocationId? locationId, IReadOnlyDictionary<string, string>? variables,
        bool onlyLocationCustomers = false)
    {
        DomainException.ThrowIf(Status != CampaignStatus.Draft, CampaignErrors.NotDraft);
        Apply(name, typeKey, channel, tag, locationId, variables, onlyLocationCustomers);
    }

    /// <summary>Programme l'envoi ; sans date (ou une date passée), il commence au prochain passage du worker.</summary>
    public void Schedule(DateTimeOffset? at, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != CampaignStatus.Draft, CampaignErrors.NotDraft);
        var now = clock.UtcNow;
        DomainException.ThrowIf(at is { } date && date > now.AddDays(90), CampaignErrors.ScheduleTooFar);
        ScheduledForUtc = at is { } when && when > now ? when : now;
        Status = CampaignStatus.Scheduled;
    }

    /// <summary>Repasse en brouillon une campagne programmée qui n'a pas commencé.</summary>
    public void Unschedule()
    {
        DomainException.ThrowIf(Status != CampaignStatus.Scheduled, CampaignErrors.NotScheduled);
        Status = CampaignStatus.Draft;
        ScheduledForUtc = null;
    }

    /// <summary>Arrête la campagne : les messages déjà créés partent quand même, plus aucun autre n'est créé.</summary>
    public void Cancel(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status is CampaignStatus.Completed or CampaignStatus.Cancelled, CampaignErrors.Finished);
        Status = CampaignStatus.Cancelled;
        CompletedAtUtc = clock.UtcNow;
    }

    public bool IsDue(DateTimeOffset now) =>
        Status == CampaignStatus.Sending || (Status == CampaignStatus.Scheduled && ScheduledForUtc <= now);

    /// <summary>Un lot traité : <paramref name="visited"/> clients parcourus ; <paramref name="exhausted"/> : l'audience est épuisée.</summary>
    public void RecordBatch(int visited, int created, int skipped, int skippedForFrequency, bool exhausted, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status is not (CampaignStatus.Scheduled or CampaignStatus.Sending), CampaignErrors.Finished);
        DomainException.ThrowIf(
            visited < 0 || created < 0 || skipped < 0 || skippedForFrequency < 0 || skippedForFrequency > skipped || created + skipped > visited,
            CampaignErrors.BatchInvalid);
        var now = clock.UtcNow;
        if (Status == CampaignStatus.Scheduled)
        {
            Status = CampaignStatus.Sending;
            StartedAtUtc = now;
        }

        Processed += visited;
        Created += created;
        Skipped += skipped;
        SkippedForFrequency += skippedForFrequency;
        if (exhausted)
        {
            Status = CampaignStatus.Completed;
            CompletedAtUtc = now;
        }
    }

    private void Apply(
        string? name, string typeKey, NotificationChannel channel, string? tag, LocationId? locationId, IReadOnlyDictionary<string, string>? variables,
        bool onlyLocationCustomers)
    {
        DomainException.ThrowIf(onlyLocationCustomers && locationId is null, CampaignErrors.LocationRequired);
        var trimmed = name?.Trim() ?? string.Empty;
        DomainException.ThrowIf(trimmed.Length is 0 or > NameMaxLength, CampaignErrors.NameInvalid);
        var type = NotificationTypes.Find(TemplateKey.Create(typeKey));
        DomainException.ThrowIf(type is null || !type.IsMarketing, CampaignErrors.TypeNotMarketing);
        DomainException.ThrowIf(channel is not (NotificationChannel.Email or NotificationChannel.Sms or NotificationChannel.WhatsApp), CampaignErrors.ChannelInvalid);
        var normalizedTag = string.IsNullOrWhiteSpace(tag) ? null : tag.Trim().ToLowerInvariant();
        DomainException.ThrowIf(normalizedTag?.Length > TagMaxLength, CampaignErrors.TagInvalid);

        var payload = NotificationPayload.Create(variables);
        var kept = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in payload.Variables)
        {
            DomainException.ThrowIf(!type!.Allows(key) || PerRecipientVariables.Contains(key), CampaignErrors.VariableNotAllowed);
            if (!string.IsNullOrWhiteSpace(value))
            {
                kept[key] = value.Trim();
            }
        }

        Name = trimmed;
        TypeKey = type!.Key.Value;
        Channel = channel;
        Tag = normalizedTag;
        LocationId = locationId;
        OnlyLocationCustomers = onlyLocationCustomers;
        _variables = kept;
    }
}

public static class CampaignErrors
{
    public static readonly DomainError NameInvalid = new("notifications.campaign.name_invalid", "A campaign name has 1 to 100 characters.");
    public static readonly DomainError TypeNotMarketing = new("notifications.campaign.type_not_marketing", "A campaign sends a marketing message type.");
    public static readonly DomainError ChannelInvalid = new("notifications.campaign.channel_invalid", "A campaign is sent by e-mail, SMS or WhatsApp.");
    public static readonly DomainError LocationRequired =
        new("notifications.campaign.location_required", "Pick the location whose customers receive the campaign.");
    public static readonly DomainError TagInvalid = new("notifications.campaign.tag_invalid", "A tag has 50 characters at most.");
    public static readonly DomainError VariableNotAllowed = new("notifications.campaign.variable_not_allowed", "This variable is not set by a campaign.");
    public static readonly DomainError NotDraft = new("notifications.campaign.not_draft", "Only a draft campaign can be changed or scheduled.", DomainErrorKind.Conflict);
    public static readonly DomainError NotScheduled = new("notifications.campaign.not_scheduled", "This campaign is not waiting to start.", DomainErrorKind.Conflict);
    public static readonly DomainError Finished = new("notifications.campaign.finished", "This campaign is already finished.", DomainErrorKind.Conflict);
    public static readonly DomainError ScheduleTooFar = new("notifications.campaign.schedule_too_far", "A campaign is scheduled 90 days ahead at most.");
    public static readonly DomainError BatchInvalid = new("notifications.campaign.batch_invalid", "Inconsistent campaign batch.");
    public static readonly DomainError IntervalInvalid = new("notifications.marketing.interval_invalid", "The minimum interval is 0 to 30 days.");
}

/// <summary>
/// Règle de fréquence du restaurant : un client ne reçoit pas plus d'un message marketing par
/// <see cref="MinimumIntervalDays"/> jours (0 : pas de limite), tous canaux et campagnes confondus.
/// </summary>
public sealed class MarketingRules : ITenantOwned
{
    public const int DefaultIntervalDays = 3;
    public const int MaxIntervalDays = 30;

    private MarketingRules(TenantId tenantId, int minimumIntervalDays, DateTimeOffset updatedAtUtc)
    {
        TenantId = tenantId;
        MinimumIntervalDays = minimumIntervalDays;
        UpdatedAtUtc = updatedAtUtc;
    }

    // Réservé à l'ORM.
    private MarketingRules()
    {
    }

    public TenantId TenantId { get; private set; }

    public int MinimumIntervalDays { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static MarketingRules Create(TenantId tenantId, int minimumIntervalDays, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Validate(minimumIntervalDays);
        return new MarketingRules(Guard.NotEmpty(tenantId), minimumIntervalDays, clock.UtcNow);
    }

    public void Change(int minimumIntervalDays, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Validate(minimumIntervalDays);
        MinimumIntervalDays = minimumIntervalDays;
        UpdatedAtUtc = clock.UtcNow;
    }

    private static void Validate(int days) => DomainException.ThrowIf(days is < 0 or > MaxIntervalDays, CampaignErrors.IntervalInvalid);
}
