namespace Integrations.Domain;

public static class WebhookDeliveryErrors
{
    public static readonly DomainError RetryPolicyInvalid = new("integrations.webhook_retry_policy.invalid", "Retry policy needs 1 to 50 attempts, a positive initial delay and a maximum delay not below it nor above 7 days.");
    public static readonly DomainError PayloadSchemaVersionInvalid = new("integrations.webhook_payload.schema_version_invalid", "Payload schema version must be at least 1.");
    public static readonly DomainError PayloadEmpty = new("integrations.webhook_payload.empty", "Payload snapshot is required.");
    public static readonly DomainError PayloadTooLarge = new("integrations.webhook_payload.too_large", "Payload snapshot exceeds 262144 characters.");

    public static readonly DomainError EndpointNotActive = new("integrations.webhook_delivery.endpoint_not_active", "A delivery can only be created for an active endpoint.");
    public static readonly DomainError EventNotSubscribed = new("integrations.webhook_delivery.event_not_subscribed", "The endpoint does not subscribe to the event type of this payload.");
    public static readonly DomainError AttemptInProgress = new("integrations.webhook_delivery.attempt_in_progress", "An attempt is in progress for this delivery: wait for its outcome.");
    public static readonly DomainError NoAttemptInProgress = new("integrations.webhook_delivery.no_attempt_in_progress", "No attempt is in progress for this delivery.");
    public static readonly DomainError RetryNotDue = new("integrations.webhook_delivery.retry_not_due", "The next attempt is not due yet.");
    public static readonly DomainError FailureReasonInvalid = new("integrations.webhook_delivery.failure_reason_invalid", "A failure reason is required and limited to 500 characters.");
    public static readonly DomainError AlreadySucceeded = new("integrations.webhook_delivery.already_succeeded", "A succeeded delivery cannot be changed.");
    public static readonly DomainError AlreadyDeadLettered = new("integrations.webhook_delivery.already_dead_lettered", "A dead-lettered delivery cannot be changed.");
}
