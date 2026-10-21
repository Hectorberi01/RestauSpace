namespace Integrations.Domain;

/// <summary>
/// Hash d'une clé d'API, calculé par l'Application/Infrastructure. La clé brute n'entre jamais dans le domaine :
/// elle est générée, montrée une seule fois à l'utilisateur et hachée à l'extérieur. Le domaine traite le hash
/// comme opaque (il ne peut vérifier qu'une valeur est un hash ; la longueur minimale n'arrête que les abus évidents).
/// </summary>
public sealed class ApiKeyHash : ValueObject
{
    public const int MinLength = 32;
    public const int MaxLength = 512;

    private ApiKeyHash(string value) => Value = value;

    public string Value { get; }

    public static ApiKeyHash Create(string? value)
    {
        DomainException.ThrowIf(
            string.IsNullOrEmpty(value)
            || value.Length is < MinLength or > MaxLength
            || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)),
            ApiCredentialErrors.KeyHashInvalid);

        return new ApiKeyHash(value!);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    // Ne jamais afficher le hash dans les logs ni dans les messages d'exception.
    public override string ToString() => "ApiKeyHash(***)";
}
