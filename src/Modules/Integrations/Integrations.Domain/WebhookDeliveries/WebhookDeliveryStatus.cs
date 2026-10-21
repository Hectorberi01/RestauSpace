namespace Integrations.Domain;

public enum WebhookDeliveryStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
    DeadLetter = 4,
}
