using System.Text.RegularExpressions;

namespace Audit.Domain;

/// <summary>Code d'action pointé, normalisé (sans espaces autour, en minuscules), ex.
/// <c>payments.refund.issued</c>.</summary>
public sealed partial class AuditAction : ValueObject
{
    public const int MaxLength = 100;

    private AuditAction(string value) => Value = value;

    public string Value { get; }

    public static AuditAction Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), AuditErrors.ActionInvalid);

        var normalized = value!.Trim().ToLowerInvariant();
        DomainException.ThrowIf(normalized.Length > MaxLength || !Pattern().IsMatch(normalized), AuditErrors.ActionInvalid);

        return new AuditAction(normalized);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
