namespace Notifications.Domain;

public static class NotificationErrors
{
    public static readonly DomainError ChannelInvalid = new("notifications.recipient.channel_invalid", "Channel is not supported.");
    public static readonly DomainError DeviceTokenInvalid = new("notifications.recipient.device_token_invalid", "Push device token is required, without whitespace, and limited to 512 characters.");
    public static readonly DomainError InAppUserInvalid = new("notifications.recipient.in_app_user_invalid", "An in-app notification is addressed to a user id.");
    public static readonly DomainError RecipientKindUnknown = new("notifications.recipient.kind_unknown", "Unknown kind of recipient.");
    public static readonly DomainError PayloadKeyInvalid = new("notifications.payload.key_invalid", "Variable names are dotted names such as 'customer.firstName' (64 characters max).");
    public static readonly DomainError PayloadKeyDuplicated = new("notifications.payload.key_duplicated", "A variable cannot be declared twice.");
    public static readonly DomainError PayloadValueMissing = new("notifications.payload.value_missing", "A variable value cannot be null.");
    public static readonly DomainError PayloadValueTooLong = new("notifications.payload.value_too_long", "A variable value is limited to 2000 characters.");
    public static readonly DomainError PayloadValueInvalid = new("notifications.payload.value_invalid", "A variable value cannot contain '{{' or '}}'.");
    public static readonly DomainError PayloadTooManyVariables = new("notifications.payload.too_many_variables", "A payload is limited to 50 variables.");
    public static readonly DomainError RetryPolicyInvalid = new("notifications.retry_policy.invalid", "Retry policy needs 1 to 20 attempts, a positive initial backoff and a maximum backoff not below it and of 7 days at most.");

    public static readonly DomainError SubjectRequired = new("notifications.content.subject_required", "An email needs a subject.");
    public static readonly DomainError SubjectTooLong = new("notifications.content.subject_too_long", "A subject or title is limited to 200 characters.");
    public static readonly DomainError SubjectNotSupported = new("notifications.content.subject_not_supported", "SMS and WhatsApp messages have no subject.");
    public static readonly DomainError BodyEmpty = new("notifications.content.body_empty", "A notification has a content.");
    public static readonly DomainError BodyTooLong = new("notifications.content.body_too_long", "A notification content is limited to 40000 characters.");
    public static readonly DomainError ContentNotRendered = new("notifications.content.not_rendered", "The content still holds template variables: it was not rendered.");

    public static readonly DomainError ConsentRequired = new("notifications.notification.consent_required", "A marketing notification requires a consent proof.");
    public static readonly DomainError ConsentNotGranted = new("notifications.notification.consent_not_granted", "The recipient did not consent to marketing messages on this channel.");
    public static readonly DomainError ConsentChannelMismatch = new("notifications.notification.consent_channel_mismatch", "The consent proof is for another channel.");
    public static readonly DomainError ConsentCustomerMismatch = new("notifications.notification.consent_customer_mismatch", "The consent proof was obtained for another customer.");
    public static readonly DomainError CustomerRequired = new("notifications.notification.customer_required", "A marketing notification must be addressed to a known customer.");
    public static readonly DomainError PlatformMarketing = new("notifications.notification.platform_marketing", "The platform itself sends no marketing message.");
    public static readonly DomainError ContentChannelMismatch = new("notifications.notification.content_channel_mismatch", "The content was rendered for another channel than the recipient's.");
    public static readonly DomainError RecipientMismatch = new("notifications.notification.recipient_mismatch", "An in-app notification is addressed to the user it is displayed to.");
    public static readonly DomainError ProviderChannelMismatch = new("notifications.notification.provider_channel_mismatch", "The in-app provider only delivers in-app notifications, and in-app notifications only go through it.");
    public static readonly DomainError ScheduleInPast = new("notifications.notification.schedule_in_past", "A notification is scheduled in the future.");
    public static readonly DomainError NotSchedulable = new("notifications.notification.not_schedulable", "Only a notification waiting to be sent can be scheduled.");
    public static readonly DomainError NotPending = new("notifications.notification.not_pending", "Only a pending or scheduled notification can be processed; schedule a retry first.");
    public static readonly DomainError AttemptInProgress = new("notifications.notification.attempt_in_progress", "An attempt is already in progress.");
    public static readonly DomainError NoAttemptInProgress = new("notifications.notification.no_attempt_in_progress", "No attempt is in progress.");
    public static readonly DomainError NotDue = new("notifications.notification.not_due", "The notification is not due yet.");
    public static readonly DomainError FailureCodeInvalid = new("notifications.failure.code_invalid", "A failure code is 2 to 64 characters: an upper-case letter, then upper-case letters, digits or '_'.");
    public static readonly DomainError FailureKindInvalid = new("notifications.failure.kind_invalid", "A failure is temporary or permanent.");
    public static readonly DomainError FailureReasonInvalid = new("notifications.failure.reason_invalid", "A provider message is limited to 500 characters.");
    public static readonly DomainError ProviderMessageIdInvalid = new("notifications.delivery.provider_message_id_invalid", "A provider message id is required, without whitespace, and limited to 256 characters.");
    public static readonly DomainError ProviderMessageMismatch = new("notifications.delivery.provider_message_mismatch", "The report is about another message of the provider.");
    public static readonly DomainError PermanentFailure = new("notifications.notification.permanent_failure", "A permanent failure is never retried.");
    public static readonly DomainError AlreadyRedacted = new("notifications.notification.already_redacted", "A redacted notification can no longer be sent.");
    public static readonly DomainError AlreadyDelivered = new("notifications.notification.already_delivered", "A notification already (partially) delivered cannot fail any more.");
    public static readonly DomainError NotFailed = new("notifications.notification.not_failed", "Only a failed notification can be retried.");
    public static readonly DomainError RetriesExhausted = new("notifications.notification.retries_exhausted", "The maximum number of attempts has been reached.");
    public static readonly DomainError NotSent = new("notifications.notification.not_sent", "Only a notification handed over to the provider can be delivered.");
    public static readonly DomainError AlreadySent = new("notifications.notification.already_sent", "A notification handed over to the provider is no longer sent, scheduled or cancelled.");
    public static readonly DomainError AlreadyCancelled = new("notifications.notification.already_cancelled", "A cancelled notification cannot be changed.");
    public static readonly DomainError NotInApp = new("notifications.notification.not_in_app", "Only an in-app notification is read or archived.");
    public static readonly DomainError NotAbandoned = new("notifications.notification.not_abandoned", "Only a notification that will no longer be sent can be retried manually.");
    public static readonly DomainError NotRedactable = new("notifications.notification.not_redactable", "A notification that can still be sent cannot be redacted; cancel it first.");
}
