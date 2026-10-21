using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Application.Commands;

// Encaissements de la PLATEFORME (abonnements des restaurants), sur son propre compte marchand chez le prestataire :
// identifiants rangés au coffre par la plateforme (SetPlatformCredentialsCommand). Appelés par l'hôte pour Billing ;
// aucune route. Rien n'est enregistré ici : Billing garde la trace de l'encaissement et relit son état.

public sealed record PlatformCollectionStart(string Provider, string ProviderReference, Uri? CheckoutUrl);

/// <param name="Reference">Id de l'encaissement côté Billing : sert d'id de paiement auprès du prestataire.</param>
/// <param name="Label">Libellé montré au payeur, sans donnée personnelle.</param>
/// <param name="PayerPhone">Numéro du payeur (paiement poussé sur son téléphone). Jamais conservé.</param>
public sealed record StartPlatformCollectionCommand(
    string Provider,
    Guid Reference,
    Guid TenantId,
    decimal Amount,
    string Currency,
    string Label,
    string? PayerPhone,
    Uri? ReturnUrl) : ICommand<PlatformCollectionStart>
{
    public override string ToString() => $"StartPlatformCollectionCommand ({Provider}, {Reference}, {Amount} {Currency}, ***)";
}

public sealed class StartPlatformCollectionHandler(PlatformCollector collector) : ICommandHandler<StartPlatformCollectionCommand, PlatformCollectionStart>
{
    public async Task<PlatformCollectionStart> HandleAsync(StartPlatformCollectionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var amount = Money.Of(command.Amount, Currency.FromCode(command.Currency));
        var phone = PayerPhones.Normalize(command.PayerPhone);
        var (provider, gateway) = await collector.GatewayAsync(command.Provider, amount.Currency, cancellationToken);
        try
        {
            var checkout = await gateway.StartAsync(
                new GatewayPaymentRequest(
                    TenantId.From(command.TenantId),
                    PaymentId.From(command.Reference),
                    OrderId.From(command.Reference),
                    amount,
                    PayerPhone: phone,
                    ReturnUrl: command.ReturnUrl,
                    Label: command.Label),
                cancellationToken);
            return new PlatformCollectionStart(provider.Value, checkout.Reference.Value, checkout.CheckoutUrl);
        }
        catch (PaymentGatewayException exception)
        {
            throw new DomainException(PaymentsApplicationErrors.GatewayFailed, exception);
        }
    }
}

public enum PlatformCollectionState
{
    Pending = 1,
    Paid = 2,
    Failed = 3,

    /// <summary>Le prestataire ne permet pas de relire l'encaissement : il reste en attente.</summary>
    Unknown = 4,
}

public sealed record PlatformCollectionStatus(PlatformCollectionState State, decimal? Amount, string? Currency);

/// <summary>Relit l'état d'un encaissement de la plateforme chez le prestataire, qui fait foi.</summary>
public sealed record GetPlatformCollectionStatusQuery(string Provider, string ProviderReference) : IQuery<PlatformCollectionStatus>;

public sealed class GetPlatformCollectionStatusHandler(PlatformCollector collector) : IQueryHandler<GetPlatformCollectionStatusQuery, PlatformCollectionStatus>
{
    public async Task<PlatformCollectionStatus> HandleAsync(GetPlatformCollectionStatusQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (_, gateway) = await collector.GatewayAsync(query.Provider, null, cancellationToken);
        try
        {
            return await gateway.FetchStatusAsync(ProviderReference.Create(query.ProviderReference), cancellationToken) switch
            {
                null => new PlatformCollectionStatus(PlatformCollectionState.Unknown, null, null),
                { State: GatewayTransactionState.Captured } status => new PlatformCollectionStatus(PlatformCollectionState.Paid, status.Amount, status.Currency),
                { State: GatewayTransactionState.Failed } status => new PlatformCollectionStatus(PlatformCollectionState.Failed, status.Amount, status.Currency),
                var status => new PlatformCollectionStatus(PlatformCollectionState.Pending, status.Amount, status.Currency),
            };
        }
        catch (PaymentGatewayException exception)
        {
            throw new DomainException(PaymentsApplicationErrors.GatewayFailed, exception);
        }
    }
}

/// <summary>
/// Le prestataire peut-il encaisser pour la plateforme dans cette devise : inscrit, actif, en ligne, devise acceptée,
/// intégration présente et identifiants de la plateforme rangés et lisibles. Aucun appel au prestataire.
/// </summary>
public sealed record CheckPlatformCollectionQuery(string Provider, string Currency) : IQuery<bool>;

public sealed class CheckPlatformCollectionHandler(PlatformCollector collector) : IQueryHandler<CheckPlatformCollectionQuery, bool>
{
    public async Task<bool> HandleAsync(CheckPlatformCollectionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        try
        {
            await collector.GatewayAsync(query.Provider, Currency.FromCode(query.Currency), cancellationToken);
            return true;
        }
        catch (DomainException)
        {
            return false;
        }
    }
}

/// <summary>
/// Ce qui manque à un prestataire en ligne pour encaisser pour la plateforme dans une devise. Codes stables :
/// <c>disabled</c> (inactif), <c>currency</c> (devise non autorisée), <c>integration</c> (absent du code),
/// <c>credentials</c> (compte plateforme non enregistré), <c>credentials_unreadable</c> (identifiants illisibles).
/// </summary>
/// <param name="Methods">Les moyens de paiement déclarés du prestataire (Card, MobileMoney, PayPal…).</param>
public sealed record PlatformCollectionReadiness(string Provider, string Name, bool Ready, IReadOnlyList<string> Missing, IReadOnlyList<string>? Methods = null);

/// <summary>Les prestataires en ligne du catalogue et, pour chacun, ce qui lui manque pour encaisser dans la devise. Aucun appel au prestataire.</summary>
public sealed record ListPlatformCollectionReadinessQuery(string Currency) : IQuery<IReadOnlyList<PlatformCollectionReadiness>>;

public sealed class ListPlatformCollectionReadinessHandler(IPaymentProviderRepository providers, IPaymentIntegrations integrations, IPaymentSecretStore secrets)
    : IQueryHandler<ListPlatformCollectionReadinessQuery, IReadOnlyList<PlatformCollectionReadiness>>
{
    public async Task<IReadOnlyList<PlatformCollectionReadiness>> HandleAsync(ListPlatformCollectionReadinessQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var currency = Currency.FromCode(query.Currency);
        var result = new List<PlatformCollectionReadiness>();
        foreach (var provider in (await providers.ListAsync(cancellationToken)).Where(provider => provider.Type == PaymentProviderType.Online))
        {
            var missing = new List<string>();
            if (!provider.IsEnabled)
            {
                missing.Add("disabled");
            }
            else if (!provider.CanTakePaymentsIn(currency))
            {
                missing.Add("currency");
            }

            if (integrations.Find(provider.Code) is null)
            {
                missing.Add("integration");
            }

            try
            {
                if (await secrets.ReadAsync(PaymentCredentialReference.ForPlatform(provider.Code), cancellationToken) is null)
                {
                    missing.Add("credentials");
                }
            }
            catch (InvalidOperationException)
            {
                missing.Add("credentials_unreadable");
            }

            result.Add(new PlatformCollectionReadiness(
                provider.Code.Value, provider.DisplayName, missing.Count == 0, missing, provider.Capability.SupportedMethods.Select(method => method.ToString()).ToList()));
        }

        return result;
    }
}

/// <summary>Le prestataire de la plateforme : inscrit au catalogue, actif, en ligne, avec ses identifiants rangés.</summary>
public sealed class PlatformCollector(IPaymentProviderRepository providers, IPaymentIntegrations integrations, IPaymentSecretStore secrets)
{
    public async Task<(PaymentProviderCode Provider, IPaymentGateway Gateway)> GatewayAsync(
        string code, Currency? currency, CancellationToken cancellationToken)
    {
        var providerCode = PaymentProviderCode.Create(code);
        var provider = await providers.FindByCodeAsync(providerCode, cancellationToken);
        DomainException.ThrowIf(
            provider is null || !provider.IsEnabled || provider.Type != PaymentProviderType.Online
            || (currency is not null && !provider.CanTakePaymentsIn(currency))
            || integrations.Find(providerCode) is null,
            PaymentsApplicationErrors.PlatformCollectionUnavailable);

        IReadOnlyDictionary<string, string>? credentials;
        try
        {
            credentials = await secrets.ReadAsync(PaymentCredentialReference.ForPlatform(providerCode), cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            throw new DomainException(PaymentsApplicationErrors.CredentialsUnreadable, exception);
        }

        DomainException.ThrowIf(credentials is null, PaymentsApplicationErrors.PlatformCollectionUnavailable);
        return (providerCode, integrations.CreateGateway(providerCode, credentials!));
    }
}
