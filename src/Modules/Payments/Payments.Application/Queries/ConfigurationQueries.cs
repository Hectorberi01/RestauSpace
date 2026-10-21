using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Application.Queries;

/// <param name="CredentialFields">Champs que chaque restaurant saisit (en ligne) ; vide au comptoir.</param>
/// <param name="HasIntegration">En ligne : l'intégration existe dans le code (sinon le prestataire ne peut pas servir).</param>
public sealed record PaymentProviderDto(
    Guid Id,
    string Code,
    PaymentProviderType Type,
    string DisplayName,
    bool IsEnabled,
    IReadOnlyList<string> Currencies,
    ProviderCapabilityDto Capability,
    IReadOnlyList<string> CredentialFields,
    bool HasIntegration);

/// <summary>Ce que le prestataire sait faire ; pays et devises vides = tous.</summary>
public sealed record ProviderCapabilityDto(
    IReadOnlyList<PaymentMethodType> Methods,
    IReadOnlyList<string> Countries,
    IReadOnlyList<string> Currencies,
    bool SupportsAuthorization,
    bool SupportsRefund,
    bool SupportsPartialRefund,
    bool SupportsWebhooks,
    bool SupportsRecurring,
    int? PaymentExpirationMinutes,
    IReadOnlyList<AmountLimitDto> AmountLimits)
{
    public static ProviderCapabilityDto From(PaymentProviderCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        return new ProviderCapabilityDto(
            capability.SupportedMethods,
            capability.SupportedCountries.Select(country => country.Value).ToList(),
            capability.SupportedCurrencies.Select(currency => currency.Code).ToList(),
            capability.SupportsAuthorization,
            capability.SupportsRefund,
            capability.SupportsPartialRefund,
            capability.SupportsWebhooks,
            capability.SupportsRecurring,
            capability.PaymentExpiration is { } expiration ? (int)expiration.TotalMinutes : null,
            capability.AmountLimits.Select(limit => new AmountLimitDto(limit.Currency.Code, limit.Minimum, limit.Maximum)).ToList());
    }
}

public sealed record AmountLimitDto(string Currency, decimal? Minimum, decimal? Maximum);

/// <param name="IsRegistered">Déjà au catalogue de la plateforme.</param>
public sealed record PaymentIntegrationDto(string Code, string Name, IReadOnlyList<string> CredentialFields, bool IsRegistered);

public sealed record PaymentMethodAssignmentDto(
    Guid LocationId, PaymentMethodType Method, bool IsEnabled, int Priority, decimal? MinAmount, decimal? MaxAmount, string? Currency);

/// <param name="IsAvailable">Le prestataire est proposé par la plateforme : sinon aucun nouveau paiement, même activée.</param>
/// <param name="HasCredentials">Des identifiants sont rangés au coffre (jamais leurs valeurs, ni leurs noms).</param>
/// <param name="IsGlobal">Aucun établissement affecté : vaut pour tous les établissements du restaurant.</param>
/// <param name="WebhookPath">En ligne : l'adresse à déclarer chez le prestataire pour ses webhooks.</param>
public sealed record RestaurantPaymentConfigurationDto(
    Guid Id,
    Guid ProviderId,
    string Provider,
    string DisplayName,
    PaymentProviderType Type,
    PaymentMode Mode,
    PaymentConfigurationStatus Status,
    bool IsEnabled,
    bool IsAvailable,
    bool HasCredentials,
    IReadOnlyList<PaymentMethodType> EnabledMethods,
    IReadOnlyList<string> Currencies,
    IReadOnlyList<string> RequiredCredentialFields,
    bool IsGlobal,
    IReadOnlyList<PaymentMethodAssignmentDto> Assignments,
    string? WebhookPath,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Plateforme : tout le catalogue, retirés compris.</summary>
public sealed record ListPaymentProvidersQuery : IQuery<IReadOnlyList<PaymentProviderDto>>;

public sealed class ListPaymentProvidersHandler(IPaymentProviderRepository providers, IPaymentIntegrations integrations)
    : IQueryHandler<ListPaymentProvidersQuery, IReadOnlyList<PaymentProviderDto>>
{
    public Task<IReadOnlyList<PaymentProviderDto>> HandleAsync(ListPaymentProvidersQuery query, CancellationToken cancellationToken = default) =>
        CatalogView.ListAsync(providers, integrations, onlyOffered: false, cancellationToken);
}

/// <summary>Plateforme : les intégrations présentes dans le code, et si elles sont déjà au catalogue.</summary>
public sealed record ListPaymentIntegrationsQuery : IQuery<IReadOnlyList<PaymentIntegrationDto>>;

public sealed class ListPaymentIntegrationsHandler(
    IPaymentIntegrations integrations, IPaymentProviderRepository providers)
    : IQueryHandler<ListPaymentIntegrationsQuery, IReadOnlyList<PaymentIntegrationDto>>
{
    public async Task<IReadOnlyList<PaymentIntegrationDto>> HandleAsync(ListPaymentIntegrationsQuery query, CancellationToken cancellationToken = default)
    {
        var registered = (await providers.ListAsync(cancellationToken)).Select(provider => provider.Code).ToHashSet();
        return integrations.All
            .OrderBy(integration => integration.Provider.Value, StringComparer.Ordinal)
            .Select(integration => new PaymentIntegrationDto(
                integration.Provider.Value, integration.Name, integration.CredentialFields, registered.Contains(integration.Provider)))
            .ToList();
    }
}

/// <summary>Restaurant : les prestataires que la plateforme propose en ce moment, parmi lesquels choisir.</summary>
public sealed record ListOfferedPaymentProvidersQuery : IQuery<IReadOnlyList<PaymentProviderDto>>;

public sealed class ListOfferedPaymentProvidersHandler(PaymentsWorkspace workspace)
    : IQueryHandler<ListOfferedPaymentProvidersQuery, IReadOnlyList<PaymentProviderDto>>
{
    public Task<IReadOnlyList<PaymentProviderDto>> HandleAsync(ListOfferedPaymentProvidersQuery query, CancellationToken cancellationToken = default)
    {
        // Lecture d'un membre de restaurant : exige un restaurant sélectionné, comme toute route du personnel.
        _ = workspace.CurrentTenant;
        return CatalogView.ListAsync(workspace.Providers, workspace.Integrations, onlyOffered: true, cancellationToken);
    }
}

/// <summary>Restaurant : ses configurations, activées ou non, avec leur disponibilité côté plateforme.</summary>
public sealed record ListPaymentConfigurationsQuery : IQuery<IReadOnlyList<RestaurantPaymentConfigurationDto>>;

public sealed class ListPaymentConfigurationsHandler(PaymentsWorkspace workspace)
    : IQueryHandler<ListPaymentConfigurationsQuery, IReadOnlyList<RestaurantPaymentConfigurationDto>>
{
    public async Task<IReadOnlyList<RestaurantPaymentConfigurationDto>> HandleAsync(
        ListPaymentConfigurationsQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var configurations = await workspace.Configurations.ListAsync(tenant, cancellationToken);
        var providers = (await workspace.Providers.ListAsync(cancellationToken)).ToDictionary(provider => provider.Id);

        return configurations
            .Select(configuration =>
            {
                var provider = providers.GetValueOrDefault(configuration.PaymentProviderId);
                var integration = workspace.Integrations.Find(configuration.Provider);
                var online = configuration.ProviderType == PaymentProviderType.Online;
                return new RestaurantPaymentConfigurationDto(
                    configuration.Id.Value,
                    configuration.PaymentProviderId.Value,
                    configuration.Provider.Value,
                    provider?.DisplayName ?? configuration.Provider.Value,
                    configuration.ProviderType,
                    configuration.Mode,
                    configuration.Status,
                    configuration.IsEnabled,
                    provider is { IsEnabled: true } && (!online || integration is not null),
                    configuration.SecretReference is not null,
                    configuration.EnabledMethods,
                    provider?.SupportedCurrencies.Select(currency => currency.Code).ToList() ?? (IReadOnlyList<string>)Array.Empty<string>(),
                    online && configuration.Mode == PaymentMode.TenantManaged
                        ? integration?.CredentialFields ?? Array.Empty<string>()
                        : Array.Empty<string>(),
                    configuration.IsGlobal,
                    configuration.Assignments
                        .OrderBy(assignment => assignment.LocationId.Value)
                        .ThenBy(assignment => assignment.Priority)
                        .Select(assignment => new PaymentMethodAssignmentDto(
                            assignment.LocationId.Value,
                            assignment.Method,
                            assignment.IsEnabled,
                            assignment.Priority,
                            assignment.MinAmount?.Amount,
                            assignment.MaxAmount?.Amount,
                            (assignment.MinAmount ?? assignment.MaxAmount)?.Currency.Code))
                        .ToList(),
                    online ? $"/payments/webhooks/{configuration.Provider.Value}/{configuration.Id.Value}" : null,
                    configuration.UpdatedAtUtc);
            })
            .OrderBy(configuration => configuration.DisplayName, StringComparer.CurrentCulture)
            .ToList();
    }
}

/// <summary>
/// Le catalogue en DTO, avec les champs d'identifiants des intégrations. « Proposé » = actif à la plateforme ET
/// utilisable (un prestataire en ligne sans intégration dans le code ne l'est pas).
/// </summary>
internal static class CatalogView
{
    public static async Task<IReadOnlyList<PaymentProviderDto>> ListAsync(
        IPaymentProviderRepository providers, IPaymentIntegrations integrations, bool onlyOffered, CancellationToken cancellationToken) =>
        (await providers.ListAsync(cancellationToken))
            .Select(provider =>
            {
                var integration = integrations.Find(provider.Code);
                return new PaymentProviderDto(
                    provider.Id.Value,
                    provider.Code.Value,
                    provider.Type,
                    provider.DisplayName,
                    provider.IsEnabled,
                    provider.SupportedCurrencies.Select(currency => currency.Code).ToList(),
                    ProviderCapabilityDto.From(provider.Capability),
                    integration?.CredentialFields ?? Array.Empty<string>(),
                    provider.Type == PaymentProviderType.OnSite || integration is not null);
            })
            .Where(provider => !onlyOffered || (provider.IsEnabled && provider.HasIntegration))
            .ToList();
}

/// <param name="Priority">Rang dans l'établissement, le plus petit d'abord (0 sans affectation).</param>
public sealed record AvailablePaymentMethodDto(Guid ConfigurationId, string Provider, string DisplayName, PaymentProviderType Type, PaymentMethodType Method, int Priority);

/// <summary>
/// Les moyens de paiement proposés pour une commande de l'établissement (montant et devise de la commande), au comptoir
/// ou en ligne : voir <see cref="PaymentMethodAvailability"/>.
/// </summary>
public sealed record GetAvailablePaymentMethodsQuery(Guid LocationId, decimal Amount, string? Currency, PaymentChannel Channel)
    : IQuery<IReadOnlyList<AvailablePaymentMethodDto>>;

public sealed class GetAvailablePaymentMethodsHandler(PaymentsWorkspace workspace, IPaymentLocations locations)
    : IQueryHandler<GetAvailablePaymentMethodsQuery, IReadOnlyList<AvailablePaymentMethodDto>>
{
    public async Task<IReadOnlyList<AvailablePaymentMethodDto>> HandleAsync(GetAvailablePaymentMethodsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var location = (query.LocationId == Guid.Empty ? null : await locations.FindAsync(tenant, LocationId.From(query.LocationId), cancellationToken))
            ?? throw new NotFoundException(PaymentsApplicationErrors.LocationNotFound);
        var amount = Money.Of(query.Amount, Currency.FromCode(query.Currency));
        var providers = (await workspace.Providers.ListAsync(cancellationToken)).ToDictionary(provider => provider.Id);
        var configurations = (await workspace.Configurations.ListAsync(tenant, cancellationToken))
            .Where(configuration => configuration.ProviderType == PaymentProviderType.OnSite || workspace.Integrations.Find(configuration.Provider) is not null)
            .Select(configuration => (configuration, providers.GetValueOrDefault(configuration.PaymentProviderId)));

        return PaymentMethodAvailability.For(new PaymentContext(location, amount, query.Channel), configurations)
            .Select(item => new AvailablePaymentMethodDto(
                item.ConfigurationId.Value, item.Provider.Value, item.DisplayName, item.Type, item.Method, item.Priority))
            .ToList();
    }
}
