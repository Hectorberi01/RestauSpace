using System.Text.RegularExpressions;

namespace Integrations.Domain;

/// <summary>
/// Nom public d'un événement sortant, p. ex. <c>orders.order_placed</c> (module, puis fait). Même grammaire que les
/// codes de permission du module Staff et que <see cref="ApiScope"/>, pour qu'un même vocabulaire de catalogue serve
/// aussi aux événements : au moins deux segments en minuscules séparés par des points, chacun commençant par une
/// lettre et composé de lettres, chiffres ou underscores. Les majuscules sont rejetées, pas converties.
/// </summary>
public sealed partial class EventTypeCode : ValueObject
{
    public const int MaxLength = 100;

    private EventTypeCode(string value) => Value = value;

    public string Value { get; }

    public static EventTypeCode Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), WebhookEndpointErrors.EventTypeInvalid);

        var trimmed = value!.Trim();
        DomainException.ThrowIf(
            trimmed.Length > MaxLength || !Pattern().IsMatch(trimmed),
            WebhookEndpointErrors.EventTypeInvalid);

        return new EventTypeCode(trimmed);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
