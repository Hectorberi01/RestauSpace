using Storefront.Application.Abstractions;
using Storefront.Domain;

namespace Storefront.Application;

/// <summary>
/// Étapes communes aux cas d'usage. Le tenant vient du contexte (session de la console, ou clé publique de
/// l'application), jamais d'une valeur de requête ; une écriture exige un tenant qui les accepte.
/// </summary>
public sealed class StorefrontWorkspace(
    ITenantContext tenantContext,
    IStorefrontTenants tenants,
    ISalesAppRepository apps,
    IOnlineOrderingSettingsRepository settings,
    IStorefrontLocations locations,
    IStorefrontEntitlements entitlements,
    IStorefrontUnitOfWork unitOfWork)
{
    public ISalesAppRepository Apps => apps;

    public IOnlineOrderingSettingsRepository Settings => settings;

    public IStorefrontLocations Locations => locations;

    public IStorefrontEntitlements Entitlements => entitlements;

    public IStorefrontTenants Tenants => tenants;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (await tenants.FindAsync(CurrentTenant, cancellationToken) is not { AcceptsBusinessWrites: true })
        {
            throw new DomainException(StorefrontApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<SalesApp> LoadAppAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await apps.FindAsync(CurrentTenant, SalesAppId.From(id), cancellationToken))
        ?? throw new NotFoundException(StorefrontApplicationErrors.AppNotFound);

    /// <summary>Le canal doit être une option active du restaurant.</summary>
    public async Task RequireOptionAsync(SalesAppKind channel, CancellationToken cancellationToken) =>
        DomainException.ThrowIf(
            !await entitlements.AllowsAsync(CurrentTenant, channel, cancellationToken), StorefrontApplicationErrors.OptionRequired);

    /// <summary>Les références des établissements demandés, tous du restaurant courant (404 sinon).</summary>
    public async Task<IReadOnlyList<LocationReference>> RequireLocationsAsync(
        IEnumerable<Guid>? ids, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var wanted = (ids ?? []).Distinct().ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        var known = (await locations.ListAsync(CurrentTenant, nowUtc, cancellationToken)).Select(location => location.LocationId.Value).ToHashSet();
        return wanted.Select(id => id != Guid.Empty && known.Contains(id)
                ? new LocationReference(CurrentTenant, LocationId.From(id))
                : throw new NotFoundException(StorefrontApplicationErrors.LocationNotFound))
            .ToList();
    }

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);
}

/// <summary>
/// Les clés publiques : <c>pk_live_</c> + base64url(24 octets aléatoires). Seule leur empreinte SHA-256 est gardée, avec
/// leurs 4 derniers caractères ; la clé est montrée UNE fois, à l'émission ou à la rotation.
/// </summary>
public static class PublishableKeys
{
    public const string Prefix = "pk_live_";

    /// <summary>Longueur maximale acceptée d'une clé présentée (au-delà : refusée sans calcul).</summary>
    public const int MaxPresentedLength = 100;

    public static string NewKey() =>
        Prefix + Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static PublishableKeyHash Hash(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return PublishableKeyHash.Create(
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key))).ToLowerInvariant());
    }

    public static string Hint(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key[^SalesApp.KeyHintLength..];
    }

    /// <summary>Vrai si la chaîne a la forme d'une clé publique (avant tout calcul).</summary>
    public static bool LooksValid(string? key) =>
        key is { Length: > 8 and <= MaxPresentedLength } && key.StartsWith(Prefix, StringComparison.Ordinal)
        && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
