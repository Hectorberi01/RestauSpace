namespace Integrations.Domain;

/// <summary>Destination configurée par un tenant pour recevoir les événements sortants.</summary>
public sealed class WebhookEndpoint : AggregateRoot<WebhookEndpointId>, ITenantOwned
{
    public const int MaxSubscribedEvents = 100;

    /// <summary>Durée maximale pendant laquelle l'ancien secret signe encore, après une rotation.</summary>
    public static readonly TimeSpan MaxSecretGracePeriod = TimeSpan.FromDays(7);

    // Non readonly : l'ORM réécrit ce champ en relisant la ligne (colonne jsonb).
    private HashSet<EventTypeCode> _subscribedEvents = [];

    private WebhookEndpoint(
        WebhookEndpointId id,
        TenantId tenantId,
        WebhookUrl url,
        HashSet<EventTypeCode> subscribedEvents,
        SecretReference secretReference,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        Url = url;
        _subscribedEvents = subscribedEvents;
        SecretReference = secretReference;
        Status = WebhookEndpointStatus.Active;
        CreatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private WebhookEndpoint()
    {
        Url = null!;
        SecretReference = null!;
    }

    public TenantId TenantId { get; private set; }

    public WebhookUrl Url { get; private set; }

    public IReadOnlyCollection<EventTypeCode> SubscribedEvents => _subscribedEvents.ToList().AsReadOnly();

    public SecretReference SecretReference { get; private set; }

    public WebhookEndpointStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? SecretRotatedAtUtc { get; private set; }

    /// <summary>
    /// L'ancien secret pendant sa période de grâce après une rotation : les envois portent alors DEUX signatures (l'ancienne
    /// et la nouvelle), le destinataire a le temps de changer de secret. Null hors période de grâce.
    /// </summary>
    public SecretReference? PreviousSecretReference { get; private set; }

    public DateTimeOffset? PreviousSecretExpiresAtUtc { get; private set; }

    /// <summary>Livraisons abandonnées d'affilée (remis à zéro par une livraison réussie ou une réactivation).</summary>
    public int ConsecutiveDeadLetters { get; private set; }

    /// <summary>Instant de la dernière désactivation AUTOMATIQUE ; null après une réactivation.</summary>
    public DateTimeOffset? AutoDisabledAtUtc { get; private set; }

    public static WebhookEndpoint Create(
        TenantId tenantId,
        WebhookUrl url,
        IEnumerable<EventTypeCode> subscribedEvents,
        SecretReference secretReference,
        IClock clock,
        ChangeActor? actor = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(subscribedEvents);
        ArgumentNullException.ThrowIfNull(secretReference);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);

        var events = new HashSet<EventTypeCode>();
        foreach (var eventType in subscribedEvents)
        {
            ArgumentNullException.ThrowIfNull(eventType, nameof(subscribedEvents));
            events.Add(eventType);
        }

        DomainException.ThrowIf(events.Count == 0, WebhookEndpointErrors.EventsRequired);
        DomainException.ThrowIf(events.Count > MaxSubscribedEvents, WebhookEndpointErrors.TooManyEvents);

        var now = clock.UtcNow;
        var endpoint = new WebhookEndpoint(WebhookEndpointId.New(), tenantId, url, events, secretReference, now);
        endpoint.Raise(new WebhookEndpointCreated(tenantId, endpoint.Id, url.Redacted, now, actor));
        return endpoint;
    }

    public bool IsSubscribedTo(EventTypeCode eventType) => _subscribedEvents.Contains(eventType);

    /// <summary>Vrai quand un événement sortant de ce type doit produire une livraison vers cet endpoint.</summary>
    public bool ShouldReceive(EventTypeCode eventType) =>
        Status == WebhookEndpointStatus.Active && IsSubscribedTo(eventType);

    public void Subscribe(EventTypeCode eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        DomainException.ThrowIf(_subscribedEvents.Contains(eventType), WebhookEndpointErrors.AlreadySubscribed);
        DomainException.ThrowIf(_subscribedEvents.Count == MaxSubscribedEvents, WebhookEndpointErrors.TooManyEvents);

        _subscribedEvents.Add(eventType);
    }

    public void Unsubscribe(EventTypeCode eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        DomainException.ThrowIf(!_subscribedEvents.Contains(eventType), WebhookEndpointErrors.NotSubscribed);
        DomainException.ThrowIf(
            Status == WebhookEndpointStatus.Active && _subscribedEvents.Count == 1,
            WebhookEndpointErrors.EventsRequired);

        _subscribedEvents.Remove(eventType);
    }

    /// <summary>
    /// Remplace la référence du secret. Avec <paramref name="gracePeriod"/> &gt; 0, l'ancien secret reste valable jusqu'à
    /// maintenant + la période (il signe encore les envois) ; à zéro, il est révoqué aussitôt (secret compromis). Un
    /// ancien secret encore en grâce est oublié : l'Application doit le supprimer du coffre AVANT d'appeler.
    /// </summary>
    public void RotateSecretReference(SecretReference newSecretReference, IClock clock, TimeSpan gracePeriod = default, ChangeActor? actor = null)
    {
        ArgumentNullException.ThrowIfNull(newSecretReference);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThan(gracePeriod, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gracePeriod, MaxSecretGracePeriod);
        if (SecretReference == newSecretReference)
        {
            return;
        }

        var now = clock.UtcNow;
        var previous = SecretReference;
        SecretReference = newSecretReference;
        SecretRotatedAtUtc = now;
        if (gracePeriod > TimeSpan.Zero)
        {
            PreviousSecretReference = previous;
            PreviousSecretExpiresAtUtc = now + gracePeriod;
        }
        else
        {
            PreviousSecretReference = null;
            PreviousSecretExpiresAtUtc = null;
        }

        Raise(new WebhookSecretReferenceRotated(TenantId, Id, now, PreviousSecretExpiresAtUtc, actor));
    }

    /// <summary>L'ancien secret, s'il est encore en période de grâce à cet instant (il signe alors aussi).</summary>
    public SecretReference? PreviousSecretActiveAt(DateTimeOffset instantUtc) =>
        PreviousSecretExpiresAtUtc > instantUtc ? PreviousSecretReference : null;

    /// <summary>
    /// Fin de la période de grâce : oublie l'ancien secret s'il a expiré et renvoie sa référence (à supprimer du
    /// coffre) ; null s'il n'y en a pas ou s'il est encore valable.
    /// </summary>
    public SecretReference? ForgetExpiredPreviousSecret(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (PreviousSecretReference is not { } previous || PreviousSecretExpiresAtUtc > clock.UtcNow)
        {
            return null;
        }

        PreviousSecretReference = null;
        PreviousSecretExpiresAtUtc = null;
        return previous;
    }

    /// <summary>Une livraison a réussi : le compteur d'abandons consécutifs repart de zéro.</summary>
    public void RecordDeliverySucceeded() => ConsecutiveDeadLetters = 0;

    /// <summary>
    /// Une livraison a été abandonnée après toutes ses tentatives. À <paramref name="threshold"/> abandons d'affilée,
    /// un endpoint actif est désactivé par le système (<see cref="WebhookEndpointAutoDisabled"/>). Renvoie vrai dans ce cas.
    /// </summary>
    public bool RecordDeliveryAbandoned(int threshold, IClock clock)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(threshold, 1);
        ArgumentNullException.ThrowIfNull(clock);
        ConsecutiveDeadLetters++;
        if (Status != WebhookEndpointStatus.Active || ConsecutiveDeadLetters < threshold)
        {
            return false;
        }

        var now = clock.UtcNow;
        Status = WebhookEndpointStatus.Disabled;
        AutoDisabledAtUtc = now;
        Raise(new WebhookEndpointAutoDisabled(TenantId, Id, Url.Redacted, ConsecutiveDeadLetters, now));
        return true;
    }

    public void Disable(IClock clock, ChangeActor? actor = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == WebhookEndpointStatus.Disabled, WebhookEndpointErrors.AlreadyDisabled);

        Status = WebhookEndpointStatus.Disabled;

        Raise(new WebhookEndpointDisabled(TenantId, Id, clock.UtcNow, actor));
    }

    /// <summary>Réactive l'endpoint ; le compteur d'abandons consécutifs repart de zéro.</summary>
    public void Enable(IClock clock, ChangeActor? actor = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == WebhookEndpointStatus.Active, WebhookEndpointErrors.AlreadyActive);
        DomainException.ThrowIf(_subscribedEvents.Count == 0, WebhookEndpointErrors.EventsRequired);

        Status = WebhookEndpointStatus.Active;
        ConsecutiveDeadLetters = 0;
        AutoDisabledAtUtc = null;

        Raise(new WebhookEndpointEnabled(TenantId, Id, clock.UtcNow, actor));
    }
}
