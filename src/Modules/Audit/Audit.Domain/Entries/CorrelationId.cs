namespace Audit.Domain;

/// <summary>Identifiant opaque reliant l'entrée à la requête/trace qui l'a provoquée.</summary>
public sealed class CorrelationId : ValueObject
{
    public const int MaxLength = 100;

    private CorrelationId(string value) => Value = value;

    public string Value { get; }

    public static CorrelationId Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), AuditErrors.CorrelationIdInvalid);

        var trimmed = value!.Trim();
        DomainException.ThrowIf(
            trimmed.Length > MaxLength || trimmed.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)),
            AuditErrors.CorrelationIdInvalid);

        return new CorrelationId(trimmed);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
