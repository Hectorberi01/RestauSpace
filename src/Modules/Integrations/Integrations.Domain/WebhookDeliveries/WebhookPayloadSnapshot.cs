namespace Integrations.Domain;

/// <summary>
/// Ce qui est envoyé, figé à la création de la livraison : les nouvelles tentatives renvoient toujours le même body,
/// quoi qu'il soit arrivé depuis à l'agrégat source. La version de schéma permet aux consommateurs d'évoluer ; le body
/// est opaque pour le domaine.
/// </summary>
public sealed class WebhookPayloadSnapshot : ValueObject
{
    public const int MaxPayloadLength = 262_144;

    private WebhookPayloadSnapshot(EventTypeCode eventType, int schemaVersion, string payload)
    {
        EventType = eventType;
        SchemaVersion = schemaVersion;
        Payload = payload;
    }

    public EventTypeCode EventType { get; }

    public int SchemaVersion { get; }

    public string Payload { get; }

    public static WebhookPayloadSnapshot Create(EventTypeCode eventType, int schemaVersion, string? payload)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        DomainException.ThrowIf(schemaVersion < 1, WebhookDeliveryErrors.PayloadSchemaVersionInvalid);
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(payload), WebhookDeliveryErrors.PayloadEmpty);
        DomainException.ThrowIf(payload!.Length > MaxPayloadLength, WebhookDeliveryErrors.PayloadTooLarge);

        return new WebhookPayloadSnapshot(eventType, schemaVersion, payload);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return EventType;
        yield return SchemaVersion;
        yield return Payload;
    }
}
