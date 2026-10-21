namespace Integrations.Domain;

/// <summary>
/// Pointeur vers le secret de signature dans le coffre à secrets (chemin du vault, id de clé...). JAMAIS le secret
/// lui-même : le domaine ne peut pas les distinguer, l'Application ne doit donc passer que ce que le coffre a renvoyé.
/// </summary>
public sealed class SecretReference : ValueObject
{
    public const int MaxLength = 200;

    private SecretReference(string value) => Value = value;

    public string Value { get; }

    public static SecretReference Create(string? value)
    {
        DomainException.ThrowIf(
            string.IsNullOrEmpty(value)
            || value.Length > MaxLength
            || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)),
            WebhookEndpointErrors.SecretReferenceInvalid);

        return new SecretReference(value!);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
