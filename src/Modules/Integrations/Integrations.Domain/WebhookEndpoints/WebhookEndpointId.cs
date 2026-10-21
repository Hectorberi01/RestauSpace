namespace Integrations.Domain;

public readonly record struct WebhookEndpointId
{
    private WebhookEndpointId(Guid value) => Value = value;

    public Guid Value { get; }

    public static WebhookEndpointId New() => new(Guid.CreateVersion7());

    public static WebhookEndpointId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new WebhookEndpointId(value);
    }

    public override string ToString() => Value.ToString();
}
