using System.Text.RegularExpressions;

namespace Audit.Domain;

/// <summary>
/// Ce sur quoi l'action a porté : un code de type et un identifiant opaque (ids de n'importe quel module, en texte).
/// Le type est mis en minuscules pour que <c>Order</c> et <c>order</c> soient une seule cible ; l'id est gardé tel quel.
/// </summary>
public sealed partial class AuditTarget : ValueObject
{
    public const int MaxTypeLength = 100;
    public const int MaxIdLength = 200;

    private AuditTarget(string type, string id)
    {
        Type = type;
        Id = id;
    }

    public string Type { get; }

    public string Id { get; }

    public static AuditTarget Create(string? type, string? id)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(type), AuditErrors.TargetTypeInvalid);
        var trimmedType = type!.Trim().ToLowerInvariant();
        DomainException.ThrowIf(
            trimmedType.Length > MaxTypeLength || !TypePattern().IsMatch(trimmedType),
            AuditErrors.TargetTypeInvalid);

        DomainException.ThrowIf(string.IsNullOrWhiteSpace(id), AuditErrors.TargetIdInvalid);
        var trimmedId = id!.Trim();
        DomainException.ThrowIf(
            trimmedId.Length > MaxIdLength || trimmedId.Any(AuditText.IsLineBreakingOrControl),
            AuditErrors.TargetIdInvalid);

        return new AuditTarget(trimmedType, trimmedId);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Type;
        yield return Id;
    }

    public override string ToString() => $"{Type}/{Id}";

    [GeneratedRegex(@"^[a-z][a-z0-9_.]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex TypePattern();
}
