using System.Text.RegularExpressions;

namespace Staff.Domain;

/// <summary>
/// Code d'autorisation stable comme <c>orders.cancel</c> : au moins deux segments en minuscules séparés par des points,
/// chacun commençant par une lettre et composé de lettres, chiffres ou underscores.
/// </summary>
public sealed partial class PermissionCode : ValueObject
{
    public const int MaxLength = 100;

    private PermissionCode(string value) => Value = value;

    public string Value { get; }

    public static PermissionCode Create(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value), RoleErrors.PermissionCodeInvalid);

        var trimmed = value!.Trim();
        DomainException.ThrowIf(
            trimmed.Length > MaxLength || !Pattern().IsMatch(trimmed),
            RoleErrors.PermissionCodeInvalid);

        return new PermissionCode(trimmed);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
