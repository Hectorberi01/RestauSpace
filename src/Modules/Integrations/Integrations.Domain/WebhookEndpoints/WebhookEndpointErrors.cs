namespace Integrations.Domain;

public static class WebhookEndpointErrors
{
    public static readonly DomainError UrlInvalid = new("integrations.webhook_url.invalid", "Webhook URL must be an absolute URL of at most 2048 characters.");
    public static readonly DomainError UrlNotHttps = new("integrations.webhook_url.https_required", "Webhook URL must use HTTPS.");
    public static readonly DomainError UrlHasCredentials = new("integrations.webhook_url.credentials_forbidden", "Webhook URL cannot embed credentials.");
    public static readonly DomainError UrlHostNotPublic = new("integrations.webhook_url.host_not_public", "Webhook URL must target a public host: no loopback, private, link-local or unspecified address, and no host name without a dot.");
    public static readonly DomainError UrlPortInvalid = new("integrations.webhook_url.port_invalid", "Webhook URL cannot use port 0.");
    public static readonly DomainError EventTypeInvalid = new("integrations.event_type.invalid", "Event type must be a lowercase dotted code such as 'orders.order_placed'.");
    public static readonly DomainError SecretReferenceInvalid = new("integrations.secret_reference.invalid", "Secret reference is required, without whitespace, and limited to 200 characters.");

    public static readonly DomainError EventsRequired = new("integrations.webhook_endpoint.events_required", "An active webhook endpoint must subscribe to at least one event.");
    public static readonly DomainError TooManyEvents = new("integrations.webhook_endpoint.too_many_events", "A webhook endpoint is limited to 100 subscribed events.");
    public static readonly DomainError AlreadySubscribed = new("integrations.webhook_endpoint.already_subscribed", "The endpoint already subscribes to this event.");
    public static readonly DomainError NotSubscribed = new("integrations.webhook_endpoint.not_subscribed", "The endpoint does not subscribe to this event.");
    public static readonly DomainError AlreadyDisabled = new("integrations.webhook_endpoint.already_disabled", "The endpoint is already disabled.");
    public static readonly DomainError AlreadyActive = new("integrations.webhook_endpoint.already_active", "The endpoint is already active.");
}
