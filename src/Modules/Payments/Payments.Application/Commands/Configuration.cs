using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Application.Commands;

// Deux niveaux de configuration :
// - la PLATEFORME définit ce qui est supporté (catalogue des prestataires : code, nature, devises, proposé ou retiré) ;
// - chaque RESTAURANT définit ce qu'il accepte réellement, parmi ce catalogue, et saisit les identifiants de SON compte
//   marchand pour un prestataire en ligne (l'argent va directement au restaurant).

/// <summary>
/// Ce qu'un prestataire sait faire, tel que saisi par l'administrateur de la plateforme. Pays et devises : codes ISO,
/// vides ou absents = tous. Les règles de cohérence sont celles de <see cref="PaymentProviderCapability"/>.
/// </summary>
public sealed record ProviderCapabilityInput(
    IReadOnlyList<PaymentMethodType>? Methods,
    IReadOnlyList<string>? Countries = null,
    IReadOnlyList<string>? Currencies = null,
    bool SupportsAuthorization = false,
    bool SupportsRefund = false,
    bool SupportsPartialRefund = false,
    bool SupportsWebhooks = false,
    bool SupportsRecurring = false,
    int? PaymentExpirationMinutes = null,
    IReadOnlyList<AmountLimitInput>? AmountLimits = null)
{
    public PaymentProviderCapability ToCapability() =>
        PaymentProviderCapability.Create(
            Methods ?? Array.Empty<PaymentMethodType>(),
            (Countries ?? Array.Empty<string>()).Select(CountryCode.Create),
            (Currencies ?? Array.Empty<string>()).Select(Currency.FromCode),
            SupportsAuthorization,
            SupportsRefund,
            SupportsPartialRefund,
            SupportsWebhooks,
            SupportsRecurring,
            PaymentExpirationMinutes is { } minutes ? TimeSpan.FromMinutes(minutes) : null,
            (AmountLimits ?? Array.Empty<AmountLimitInput>())
                .Select(limit => PaymentAmountLimit.Create(Currency.FromCode(limit.Currency), limit.Minimum, limit.Maximum)));
}

public sealed record AmountLimitInput(string? Currency, decimal? Minimum, decimal? Maximum);

/// <summary>
/// Plateforme : ajoute un prestataire au catalogue, proposé aussitôt aux restaurants. En ligne : son intégration doit
/// exister dans le code. Renvoie son id.
/// </summary>
public sealed record RegisterPaymentProviderCommand(
    string? Code, PaymentProviderType Type, string? DisplayName, ProviderCapabilityInput Capability) : ICommand<Guid>;

public sealed class RegisterPaymentProviderHandler(
    IPaymentProviderRepository providers, IPaymentIntegrations integrations, IPaymentsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<RegisterPaymentProviderCommand, Guid>
{
    public async Task<Guid> HandleAsync(RegisterPaymentProviderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var code = PaymentProviderCode.Create(command.Code);
        DomainException.ThrowIf(
            command.Type == PaymentProviderType.Online && integrations.Find(code) is null,
            PaymentsApplicationErrors.IntegrationMissing);

        var provider = PaymentProvider.Register(
            code,
            command.Type,
            command.DisplayName,
            (command.Capability?.ToCapability() ?? throw new DomainException(PaymentProviderErrors.MethodsMissing)),
            await providers.FindByCodeAsync(code, cancellationToken) is null,
            clock);

        providers.Add(provider);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return provider.Id.Value;
    }
}

/// <summary>Plateforme : nom affiché et capacités (pays, devises, moyens, remboursement, limites...).</summary>
public sealed record UpdatePaymentProviderCommand(Guid ProviderId, string? DisplayName, ProviderCapabilityInput Capability) : ICommand;

public sealed class UpdatePaymentProviderHandler(IPaymentProviderRepository providers, IPaymentsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<UpdatePaymentProviderCommand>
{
    public async Task HandleAsync(UpdatePaymentProviderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var provider = await ProviderCatalog.LoadAsync(providers, command.ProviderId, cancellationToken);
        provider.Rename(command.DisplayName, clock);
        provider.ChangeCapabilities((command.Capability?.ToCapability() ?? throw new DomainException(PaymentProviderErrors.MethodsMissing)), clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Plateforme : propose de nouveau le prestataire aux restaurants.</summary>
public sealed record EnablePaymentProviderCommand(Guid ProviderId) : ICommand;

public sealed class EnablePaymentProviderHandler(IPaymentProviderRepository providers, IPaymentsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<EnablePaymentProviderCommand>
{
    public async Task HandleAsync(EnablePaymentProviderCommand command, CancellationToken cancellationToken = default)
    {
        var provider = await ProviderCatalog.LoadAsync(providers, command.ProviderId, cancellationToken);
        provider.Enable(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Plateforme : retire le prestataire. Aucun restaurant ne peut plus prendre de NOUVEAU paiement par lui ; les paiements
/// en cours continuent d'être suivis (webhooks, remboursements). La configuration des restaurants est conservée.
/// </summary>
public sealed record DisablePaymentProviderCommand(Guid ProviderId) : ICommand;

public sealed class DisablePaymentProviderHandler(IPaymentProviderRepository providers, IPaymentsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<DisablePaymentProviderCommand>
{
    public async Task HandleAsync(DisablePaymentProviderCommand command, CancellationToken cancellationToken = default)
    {
        var provider = await ProviderCatalog.LoadAsync(providers, command.ProviderId, cancellationToken);
        provider.Disable(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Plateforme : identifiants du compte marchand de la PLATEFORME chez un prestataire en ligne, pour le mode
/// <see cref="PaymentMode.PlatformManaged"/> et l'encaissement des abonnements. Rangés dans le coffre
/// (<c>payments/platform/{provider}</c>) ; refusé si le prestataire n'est pas en ligne.
/// </summary>
public sealed record SetPlatformCredentialsCommand(Guid ProviderId, IReadOnlyDictionary<string, string>? Credentials) : ICommand;

public sealed class SetPlatformCredentialsHandler(
    IPaymentProviderRepository providers, IPaymentIntegrations integrations, IPaymentSecretStore secrets)
    : ICommandHandler<SetPlatformCredentialsCommand>
{
    public async Task HandleAsync(SetPlatformCredentialsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var provider = await ProviderCatalog.LoadAsync(providers, command.ProviderId, cancellationToken);
        DomainException.ThrowIf(
            provider.Type != PaymentProviderType.Online,
            PaymentConfigurationErrors.PlatformModeNotAllowed);
        var values = Credentials.Validate(integrations, provider.Code, command.Credentials);
        await secrets.StoreAsync(PaymentCredentialReference.ForPlatform(provider.Code), values, cancellationToken);
    }
}

/// <summary>
/// Restaurant : accepte un prestataire du catalogue (une configuration par prestataire). Sur place : prête et active
/// aussitôt. En ligne : à activer, après saisie des identifiants en mode restaurant (le mode par défaut).
/// </summary>
public sealed record CreatePaymentConfigurationCommand(string? Provider, PaymentMode Mode = PaymentMode.TenantManaged) : ICommand<Guid>;

public sealed class CreatePaymentConfigurationHandler(PaymentsWorkspace workspace, IClock clock)
    : ICommandHandler<CreatePaymentConfigurationCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreatePaymentConfigurationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var code = PaymentProviderCode.Create(command.Provider);
        var provider = await workspace.Providers.FindByCodeAsync(code, cancellationToken)
            ?? throw new NotFoundException(PaymentsApplicationErrors.ProviderNotFound);

        var configuration = RestaurantPaymentConfiguration.Create(
            tenant,
            provider,
            command.Mode,
            await workspace.Configurations.FindByProviderAsync(tenant, code, cancellationToken) is null,
            clock);

        workspace.Configurations.Add(configuration);
        await workspace.SaveAsync(cancellationToken);
        return configuration.Id.Value;
    }
}

/// <summary>
/// Restaurant, mode restaurant : identifiants de SON compte marchand (première saisie ou rotation). Tous les champs de
/// l'intégration, et eux seuls, non vides. Rangés dans le coffre ; la configuration ne garde que leur référence et
/// l'API ne les relit jamais.
/// </summary>
public sealed record ConfigurePaymentConfigurationCommand(Guid ConfigurationId, IReadOnlyDictionary<string, string>? Credentials) : ICommand;

public sealed class ConfigurePaymentConfigurationHandler(PaymentsWorkspace workspace, IClock clock)
    : ICommandHandler<ConfigurePaymentConfigurationCommand>
{
    public async Task HandleAsync(ConfigurePaymentConfigurationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var configuration = await workspace.LoadConfigurationAsync(tenant, command.ConfigurationId, cancellationToken);
        DomainException.ThrowIf(
            configuration.ProviderType != PaymentProviderType.Online || configuration.Mode != PaymentMode.TenantManaged,
            PaymentConfigurationErrors.CredentialsNotApplicable);
        var values = Credentials.Validate(workspace.Integrations, configuration.Provider, command.Credentials);

        // Le coffre d'abord : si la sauvegarde échoue ensuite, la même référence sera simplement réécrite au prochain essai.
        var reference = PaymentCredentialReference.ForTenant(tenant, configuration.Provider);
        await workspace.Secrets.StoreAsync(reference, values, cancellationToken);
        configuration.Configure(reference, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Restaurant : passe du compte de la plateforme au sien, ou l'inverse (si le prestataire autorise le mode plateforme).</summary>
public sealed record ChangePaymentModeCommand(Guid ConfigurationId, PaymentMode Mode) : ICommand;

public sealed class ChangePaymentModeHandler(PaymentsWorkspace workspace, IClock clock) : ICommandHandler<ChangePaymentModeCommand>
{
    public async Task HandleAsync(ChangePaymentModeCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var configuration = await workspace.LoadConfigurationAsync(workspace.CurrentTenant, command.ConfigurationId, cancellationToken);
        var provider = await ProviderCatalog.LoadAsync(workspace.Providers, configuration.PaymentProviderId.Value, cancellationToken);
        configuration.ChangeMode(command.Mode, provider, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record EnablePaymentConfigurationCommand(Guid ConfigurationId) : ICommand;

public sealed class EnablePaymentConfigurationHandler(PaymentsWorkspace workspace, IClock clock) : ICommandHandler<EnablePaymentConfigurationCommand>
{
    public async Task HandleAsync(EnablePaymentConfigurationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var configuration = await workspace.LoadConfigurationAsync(workspace.CurrentTenant, command.ConfigurationId, cancellationToken);
        var provider = await workspace.Providers.FindByCodeAsync(configuration.Provider, cancellationToken);
        var available = provider is { IsEnabled: true }
            && (configuration.ProviderType == PaymentProviderType.OnSite || workspace.Integrations.Find(configuration.Provider) is not null);

        configuration.Enable(available, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record DisablePaymentConfigurationCommand(Guid ConfigurationId) : ICommand;

public sealed class DisablePaymentConfigurationHandler(PaymentsWorkspace workspace, IClock clock) : ICommandHandler<DisablePaymentConfigurationCommand>
{
    public async Task HandleAsync(DisablePaymentConfigurationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var configuration = await workspace.LoadConfigurationAsync(workspace.CurrentTenant, command.ConfigurationId, cancellationToken);
        configuration.Disable(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Restaurant : retient (ou non) un des moyens que le prestataire offre (ex. carte mais pas Apple Pay).</summary>
public sealed record SetPaymentMethodActiveCommand(Guid ConfigurationId, PaymentMethodType Method, bool IsActive) : ICommand;

public sealed class SetPaymentMethodActiveHandler(PaymentsWorkspace workspace, IClock clock) : ICommandHandler<SetPaymentMethodActiveCommand>
{
    public async Task HandleAsync(SetPaymentMethodActiveCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var configuration = await workspace.LoadConfigurationAsync(workspace.CurrentTenant, command.ConfigurationId, cancellationToken);
        if (command.IsActive)
        {
            var provider = await ProviderCatalog.LoadAsync(workspace.Providers, configuration.PaymentProviderId.Value, cancellationToken);
            configuration.ActivatePaymentMethod(command.Method, provider, clock);
        }
        else
        {
            configuration.DeactivatePaymentMethod(command.Method, clock);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Restaurant : propose un moyen retenu dans UN de ses établissements (rang, bornes de montant, actif). L'établissement est
/// relu dans le tenant courant : un établissement d'un autre restaurant n'est pas trouvé. La première affectation réserve
/// la configuration aux établissements affectés.
/// </summary>
public sealed record AssignPaymentMethodToLocationCommand(
    Guid ConfigurationId,
    Guid LocationId,
    PaymentMethodType Method,
    int Priority = 0,
    decimal? MinAmount = null,
    decimal? MaxAmount = null,
    string? Currency = null,
    bool IsEnabled = true) : ICommand;

public sealed class AssignPaymentMethodToLocationHandler(PaymentsWorkspace workspace, IPaymentLocations locations, IClock clock)
    : ICommandHandler<AssignPaymentMethodToLocationCommand>
{
    public async Task HandleAsync(AssignPaymentMethodToLocationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var configuration = await workspace.LoadConfigurationAsync(tenant, command.ConfigurationId, cancellationToken);
        var location = (command.LocationId == Guid.Empty ? null : await locations.FindAsync(tenant, LocationId.From(command.LocationId), cancellationToken))
            ?? throw new NotFoundException(PaymentsApplicationErrors.LocationNotFound);

        Currency? currency = null;
        if (command.MinAmount is not null || command.MaxAmount is not null)
        {
            DomainException.ThrowIf(!Currency.IsKnown(command.Currency), PaymentsApplicationErrors.AmountCurrencyRequired);
            currency = Currency.FromCode(command.Currency);
        }

        configuration.AssignToLocation(
            location,
            command.Method,
            command.Priority,
            clock,
            command.MinAmount is { } min ? Money.Of(min, currency!) : null,
            command.MaxAmount is { } max ? Money.Of(max, currency!) : null,
            command.IsEnabled);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Restaurant : retire un établissement (un moyen, ou tous si <paramref name="Method"/> est null).</summary>
public sealed record RemovePaymentMethodFromLocationCommand(Guid ConfigurationId, Guid LocationId, PaymentMethodType? Method = null) : ICommand;

public sealed class RemovePaymentMethodFromLocationHandler(PaymentsWorkspace workspace, IClock clock)
    : ICommandHandler<RemovePaymentMethodFromLocationCommand>
{
    public async Task HandleAsync(RemovePaymentMethodFromLocationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var configuration = await workspace.LoadConfigurationAsync(workspace.CurrentTenant, command.ConfigurationId, cancellationToken);
        DomainException.ThrowIf(command.LocationId == Guid.Empty, PaymentConfigurationErrors.AssignmentNotFound);
        configuration.RemoveFromLocation(LocationId.From(command.LocationId), clock, command.Method);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Contrôle des identifiants saisis : tous les champs de l'intégration, eux seuls, non vides et bornés.</summary>
internal static class Credentials
{
    public const int MaxValueLength = 2000;

    public static IReadOnlyDictionary<string, string> Validate(
        IPaymentIntegrations integrations, PaymentProviderCode provider, IReadOnlyDictionary<string, string>? credentials)
    {
        var integration = integrations.Find(provider) ?? throw new DomainException(PaymentsApplicationErrors.IntegrationMissing);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, value) in credentials ?? new Dictionary<string, string>())
        {
            // Un même champ deux fois (aux espaces près) : refusé, sans jamais citer la valeur.
            DomainException.ThrowIf(!values.TryAdd(name.Trim(), value?.Trim() ?? string.Empty), PaymentsApplicationErrors.CredentialsInvalid);
        }

        DomainException.ThrowIf(
            !values.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(integration.CredentialFields)
            || values.Values.Any(value => value.Length is 0 or > MaxValueLength),
            PaymentsApplicationErrors.CredentialsInvalid);
        return values;
    }
}

internal static class ProviderCatalog
{
    public static async Task<PaymentProvider> LoadAsync(IPaymentProviderRepository providers, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await providers.FindAsync(PaymentProviderId.From(id), cancellationToken))
        ?? throw new NotFoundException(PaymentsApplicationErrors.ProviderNotFound);
}
