namespace Staff.Domain;

/// <summary>
/// La liste explicite des permissions connues de la plateforme. Assemblée par la couche Application à partir de ce
/// que déclare chaque module ; un rôle ne peut recevoir qu'un code qui en fait partie.
/// </summary>
public sealed class PermissionCatalog : ValueObject
{
    private readonly HashSet<PermissionCode> _codes;

    private PermissionCatalog(HashSet<PermissionCode> codes) => _codes = codes;

    public IReadOnlyCollection<PermissionCode> Codes => _codes.OrderBy(c => c.Value, StringComparer.Ordinal).ToList().AsReadOnly();

    public static PermissionCatalog Create(IEnumerable<PermissionCode> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);

        var set = new HashSet<PermissionCode>();
        foreach (var code in codes)
        {
            ArgumentNullException.ThrowIfNull(code, nameof(codes));
            set.Add(code);
        }

        DomainException.ThrowIf(set.Count == 0, RoleErrors.PermissionCatalogEmpty);

        return new PermissionCatalog(set);
    }

    public static PermissionCatalog Of(params string[] codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        return Create(codes.Select(PermissionCode.Create));
    }

    public bool Contains(PermissionCode code) => _codes.Contains(code);

    protected override IEnumerable<object?> GetEqualityComponents() => Codes;
}
