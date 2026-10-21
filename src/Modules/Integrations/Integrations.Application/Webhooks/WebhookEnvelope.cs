using System.Text.Json;
using System.Text.Json.Nodes;

namespace Integrations.Application.Webhooks;

/// <summary>Le corps envoyé : <c>{"id", "type", "timestamp", "data"}</c>, figé une fois pour toutes les tentatives.</summary>
public static class WebhookEnvelope
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <param name="dataJson">Les données de l'événement, un objet JSON (construit par l'hôte, sans donnée personnelle).</param>
    public static string Build(Guid eventId, string eventType, DateTimeOffset occurredAtUtc, string dataJson)
    {
        var data = JsonNode.Parse(dataJson) as JsonObject
            ?? throw new ArgumentException("Webhook data must be a JSON object.", nameof(dataJson));
        var envelope = new JsonObject
        {
            ["id"] = eventId.ToString("D"),
            ["type"] = eventType,
            ["timestamp"] = occurredAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["data"] = data,
        };
        return envelope.ToJsonString(Json);
    }
}
