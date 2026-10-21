namespace Integrations.Domain;

/// <summary>
/// Livraison d'un événement sortant vers un endpoint, avec ses nouvelles tentatives bornées.
/// Le statut ne change qu'à la fin d'une tentative : pendant qu'une tentative est en cours, la livraison reste
/// Pending/Failed et <see cref="IsAttemptInProgress"/> vaut true.
/// </summary>
public sealed class WebhookDelivery : AggregateRoot<DeliveryId>, ITenantOwned
{
    public const int MaxFailureReasonLength = 500;

    private WebhookDelivery(
        DeliveryId id,
        TenantId tenantId,
        WebhookEndpointId endpointId,
        EventId eventId,
        WebhookPayloadSnapshot payload,
        WebhookRetryPolicy retryPolicy,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        EndpointId = endpointId;
        EventId = eventId;
        Payload = payload;
        RetryPolicy = retryPolicy;
        Status = WebhookDeliveryStatus.Pending;
        CreatedAtUtc = createdAtUtc;
        NextAttemptAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private WebhookDelivery()
    {
        Payload = null!;
        RetryPolicy = null!;
    }

    public TenantId TenantId { get; private set; }

    public WebhookEndpointId EndpointId { get; private set; }

    public EventId EventId { get; private set; }

    public WebhookPayloadSnapshot Payload { get; private set; }

    /// <summary>Figée à la création : une livraison garde la même borne et le même backoff toute sa vie.</summary>
    public WebhookRetryPolicy RetryPolicy { get; private set; }

    public WebhookDeliveryStatus Status { get; private set; }

    /// <summary>Nombre de tentatives démarrées jusqu'ici.</summary>
    public int AttemptCount { get; private set; }

    /// <summary>Instant au plus tôt de la prochaine tentative ; null pendant une tentative et dans les états
    /// terminaux.</summary>
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }

    public DateTimeOffset? CurrentAttemptStartedAtUtc { get; private set; }

    public string? LastFailureReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? SucceededAtUtc { get; private set; }

    public DateTimeOffset? DeadLetteredAtUtc { get; private set; }

    public bool IsAttemptInProgress => CurrentAttemptStartedAtUtc is not null;

    public bool IsTerminal => Status is WebhookDeliveryStatus.Succeeded or WebhookDeliveryStatus.DeadLetter;

    /// <summary>
    /// Seule façon de créer une livraison : le tenant et l'id d'endpoint sont lus sur l'endpoint lui-même, qui doit
    /// être actif et abonné au type d'événement du payload.
    /// </summary>
    /// <param name="retryPolicy">Policy figée sur la livraison ; <see cref="WebhookRetryPolicy.Default"/> si omise.</param>
    public static WebhookDelivery CreateFor(
        WebhookEndpoint endpoint,
        EventId eventId,
        WebhookPayloadSnapshot payload,
        IClock clock,
        WebhookRetryPolicy? retryPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(eventId);
        DomainException.ThrowIf(endpoint.Status != WebhookEndpointStatus.Active, WebhookDeliveryErrors.EndpointNotActive);
        DomainException.ThrowIf(!endpoint.ShouldReceive(payload.EventType), WebhookDeliveryErrors.EventNotSubscribed);

        return new WebhookDelivery(
            DeliveryId.New(),
            endpoint.TenantId,
            endpoint.Id,
            eventId,
            payload,
            retryPolicy ?? WebhookRetryPolicy.Default,
            clock.UtcNow);
    }

    public bool IsDueAt(DateTimeOffset instantUtc) =>
        !IsTerminal && !IsAttemptInProgress && NextAttemptAtUtc <= instantUtc;

    public void StartAttempt(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotTerminal();
        DomainException.ThrowIf(IsAttemptInProgress, WebhookDeliveryErrors.AttemptInProgress);
        var now = clock.UtcNow;
        DomainException.ThrowIf(now < NextAttemptAtUtc, WebhookDeliveryErrors.RetryNotDue);

        AttemptCount++;
        CurrentAttemptStartedAtUtc = now;
        NextAttemptAtUtc = null;
    }

    public void Succeed(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotTerminal();
        DomainException.ThrowIf(!IsAttemptInProgress, WebhookDeliveryErrors.NoAttemptInProgress);

        var now = clock.UtcNow;
        Status = WebhookDeliveryStatus.Succeeded;
        CurrentAttemptStartedAtUtc = null;
        SucceededAtUtc = now;

        Raise(new WebhookDelivered(TenantId, Id, EndpointId, EventId, AttemptCount, now));
    }

    /// <summary>
    /// Termine la tentative en cours par un échec. Planifie la tentative suivante selon <see cref="RetryPolicy"/>, ou
    /// envoie la livraison en dead letter quand la tentative échouée était la dernière autorisée par la policy.
    /// </summary>
    public void FailAndScheduleRetry(string? reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotTerminal();
        DomainException.ThrowIf(!IsAttemptInProgress, WebhookDeliveryErrors.NoAttemptInProgress);
        var failureReason = ValidateReason(reason);

        var now = clock.UtcNow;
        if (!RetryPolicy.AllowsAttemptAfter(AttemptCount))
        {
            MoveToDeadLetter(failureReason, now);
            return;
        }

        var nextAttemptAtUtc = now + RetryPolicy.DelayAfter(AttemptCount);
        Status = WebhookDeliveryStatus.Failed;
        CurrentAttemptStartedAtUtc = null;
        LastFailureReason = failureReason;
        NextAttemptAtUtc = nextAttemptAtUtc;

        Raise(new WebhookDeliveryFailed(TenantId, Id, EndpointId, AttemptCount, failureReason, nextAttemptAtUtc, now));
    }

    /// <summary>
    /// Abandon explicite (endpoint supprimé, décision d'un opérateur...), quelles que soient les tentatives restantes.
    /// Refusé tant qu'une tentative est en cours : son issue est inconnue, et une livraison réellement reçue par le
    /// destinataire ne doit pas être enregistrée comme abandonnée. Terminer d'abord la tentative (<see cref="Succeed"/>
    /// ou <see cref="FailAndScheduleRetry"/>, ex. sur timeout d'un worker perdu), puis passer en dead letter.
    /// </summary>
    public void DeadLetter(string? reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotTerminal();
        DomainException.ThrowIf(IsAttemptInProgress, WebhookDeliveryErrors.AttemptInProgress);

        MoveToDeadLetter(ValidateReason(reason), clock.UtcNow);
    }

    private void MoveToDeadLetter(string reason, DateTimeOffset nowUtc)
    {
        Status = WebhookDeliveryStatus.DeadLetter;
        CurrentAttemptStartedAtUtc = null;
        NextAttemptAtUtc = null;
        LastFailureReason = reason;
        DeadLetteredAtUtc = nowUtc;

        Raise(new WebhookDeadLettered(TenantId, Id, EndpointId, EventId, AttemptCount, reason, nowUtc));
    }

    private void EnsureNotTerminal()
    {
        DomainException.ThrowIf(Status == WebhookDeliveryStatus.Succeeded, WebhookDeliveryErrors.AlreadySucceeded);
        DomainException.ThrowIf(Status == WebhookDeliveryStatus.DeadLetter, WebhookDeliveryErrors.AlreadyDeadLettered);
    }

    private static string ValidateReason(string? reason)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(reason), WebhookDeliveryErrors.FailureReasonInvalid);

        var trimmed = reason!.Trim();
        DomainException.ThrowIf(trimmed.Length > MaxFailureReasonLength, WebhookDeliveryErrors.FailureReasonInvalid);

        return trimmed;
    }
}
