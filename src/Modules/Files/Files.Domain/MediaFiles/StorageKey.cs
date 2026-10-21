namespace Files.Domain;

/// <summary>Clé opaque de l'objet chez le fournisseur de stockage. Le domaine ne l'interprète jamais.</summary>
public sealed class StorageKey : ValueObject
{
    public const int MaxLength = 1024;

    private StorageKey(string value) => Value = value;

    public string Value { get; }

    public static StorageKey Create(string? value)
    {
        DomainException.ThrowIf(
            string.IsNullOrEmpty(value)
            || value.Length > MaxLength
            || value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)),
            FileErrors.StorageKeyInvalid);

        return new StorageKey(value!);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
