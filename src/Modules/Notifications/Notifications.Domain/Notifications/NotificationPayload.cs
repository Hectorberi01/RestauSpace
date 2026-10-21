using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace Notifications.Domain;

/// <summary>
/// Les valeurs des variables d'un template, à rendre (<see cref="TemplateRenderer"/>) : nombre borné, noms au format des
/// templates (<c>customer.firstName</c>, voir <see cref="TemplateVariables"/>), valeurs non nulles, bornées, et sans
/// <c>{{</c> ni <c>}}</c> (une valeur ne peut pas réintroduire une variable dans le contenu rendu).
/// SÉCURITÉ : peut contenir des données personnelles ou un OTP ; jamais journalisé, jamais stocké tel quel (seul le
/// contenu rendu est gardé sur la notification, puis effacé).
/// </summary>
public sealed partial class NotificationPayload : ValueObject
{
    public const int MaxVariables = 50;
    public const int MaxKeyLength = 64;
    public const int MaxValueLength = 2000;

    private readonly ReadOnlyDictionary<string, string> _variables;

    private NotificationPayload(Dictionary<string, string> variables) =>
        _variables = new ReadOnlyDictionary<string, string>(variables);

    public static NotificationPayload Empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    public IReadOnlyDictionary<string, string> Variables => _variables;

    public static NotificationPayload Create(IEnumerable<KeyValuePair<string, string>>? variables)
    {
        if (variables is null)
        {
            return Empty;
        }

        var validated = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in variables)
        {
            DomainException.ThrowIf(
                key is null || key.Length > MaxKeyLength || !KeyPattern().IsMatch(key), NotificationErrors.PayloadKeyInvalid);
            DomainException.ThrowIf(validated.ContainsKey(key!), NotificationErrors.PayloadKeyDuplicated);
            DomainException.ThrowIf(validated.Count == MaxVariables, NotificationErrors.PayloadTooManyVariables);
            DomainException.ThrowIf(value is null, NotificationErrors.PayloadValueMissing);
            DomainException.ThrowIf(value!.Length > MaxValueLength, NotificationErrors.PayloadValueTooLong);
            DomainException.ThrowIf(
                value.Contains("{{", StringComparison.Ordinal) || value.Contains("}}", StringComparison.Ordinal),
                NotificationErrors.PayloadValueInvalid);

            validated.Add(key!, value);
        }

        return new NotificationPayload(validated);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (var pair in _variables.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            yield return pair.Key;
            yield return pair.Value;
        }
    }

    /// <summary>Une valeur par variable du template ; vide si la variable n'existe pas.</summary>
    public string? ValueOf(string variable) => _variables.GetValueOrDefault(variable);

    // Même forme que TemplateVariables. \z, pas $ : "$" correspondrait aussi juste avant un saut de ligne final.
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z][A-Za-z0-9]*)*\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
