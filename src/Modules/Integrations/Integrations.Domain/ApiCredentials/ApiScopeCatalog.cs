namespace Integrations.Domain;

/// <summary>
/// La liste explicite des scopes connus de la plateforme. Assemblée par la couche Application à partir du même
/// vocabulaire d'autorisation que le catalogue de permissions du module Staff ; un identifiant d'API ne peut être
/// émis qu'avec des scopes qui en font partie.
/// </summary>
public sealed class ApiScopeCatalog : ValueObject
{
    private readonly HashSet<ApiScope> _scopes;

    private ApiScopeCatalog(HashSet<ApiScope> scopes) => _scopes = scopes;

    public IReadOnlyCollection<ApiScope> Scopes => _scopes.OrderBy(s => s.Value, StringComparer.Ordinal).ToList().AsReadOnly();

    public static ApiScopeCatalog Create(IEnumerable<ApiScope> scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        var set = new HashSet<ApiScope>();
        foreach (var scope in scopes)
        {
            ArgumentNullException.ThrowIfNull(scope, nameof(scopes));
            set.Add(scope);
        }

        DomainException.ThrowIf(set.Count == 0, ApiCredentialErrors.ScopeCatalogEmpty);

        return new ApiScopeCatalog(set);
    }

    public static ApiScopeCatalog Of(params string[] scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        return Create(scopes.Select(ApiScope.Create));
    }

    public bool Contains(ApiScope scope) => _scopes.Contains(scope);

    protected override IEnumerable<object?> GetEqualityComponents() => Scopes;
}
