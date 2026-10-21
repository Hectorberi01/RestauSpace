using System.Text.RegularExpressions;

namespace Files.Domain;

/// <summary>
/// Référence à l'objet métier auquel le fichier est rattaché (un produit, un logo de tenant, un export...).
/// Seulement un code de type et un id : le module Files ne sait rien du module propriétaire. Le code de type est
/// <c>&lt;module&gt;.&lt;aggregate&gt;</c> (<c>catalog.product</c>, <c>tenants.tenant</c>, <c>reporting.export_job</c>) :
/// au moins deux segments en minuscules, pour qu'un simple <c>product</c> ne puisse pas collisionner entre modules.
/// </summary>
public sealed partial class FileOwner : ValueObject
{
    public const int MaxTypeLength = 64;

    private FileOwner(string ownerType, Guid ownerId)
    {
        OwnerType = ownerType;
        OwnerId = ownerId;
    }

    public string OwnerType { get; }

    public Guid OwnerId { get; }

    public static FileOwner Create(string? ownerType, Guid ownerId)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(ownerType), FileErrors.OwnerTypeInvalid);

        var normalized = ownerType!.Trim().ToLowerInvariant();
        DomainException.ThrowIf(normalized.Length > MaxTypeLength || !TypePattern().IsMatch(normalized),FileErrors.OwnerTypeInvalid);
        DomainException.ThrowIf(ownerId == Guid.Empty, FileErrors.OwnerIdEmpty);

        return new FileOwner(normalized, ownerId);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return OwnerType;
        yield return OwnerId;
    }

    public override string ToString() => $"{OwnerType}/{OwnerId}";

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex TypePattern();
}
