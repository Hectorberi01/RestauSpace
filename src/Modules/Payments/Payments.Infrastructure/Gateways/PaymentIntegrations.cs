using Payments.Application.Abstractions;
using Payments.Domain;
using Payments.Infrastructure.Webhooks;

namespace Payments.Infrastructure.Gateways;

/// <summary>
/// Le code qui parle à UN prestataire en ligne : les champs d'identifiants qu'un restaurant doit saisir, les appels
/// sortants (<see cref="IPaymentGateway"/>) et la lecture de ses webhooks (<see cref="IPaymentWebhookReader"/>), tous deux
/// branchés sur le compte marchand du restaurant. En ajouter un = l'implémenter et l'enregistrer dans
/// <c>AddPaymentsInfrastructure</c> ; la plateforme peut alors l'ajouter à son catalogue.
/// </summary>
public interface IPaymentIntegration
{
    PaymentProviderCode Provider { get; }

    string Name { get; }

    IReadOnlyList<string> CredentialFields { get; }

    IPaymentGateway CreateGateway(IReadOnlyDictionary<string, string> credentials);

    IPaymentWebhookReader CreateWebhookReader(IReadOnlyDictionary<string, string> credentials);
}

/// <summary>Les intégrations enregistrées, par code.</summary>
internal sealed class PaymentIntegrations(IEnumerable<IPaymentIntegration> integrations) : IPaymentIntegrations
{
    private readonly Dictionary<PaymentProviderCode, IPaymentIntegration> _byProvider = Index(integrations);

    public IReadOnlyList<PaymentIntegration> All => _byProvider.Values.Select(Describe).ToList();

    public PaymentIntegration? Find(PaymentProviderCode provider) => Integration(provider) is { } integration ? Describe(integration) : null;

    public IPaymentGateway CreateGateway(PaymentProviderCode provider, IReadOnlyDictionary<string, string> credentials) =>
        (Integration(provider) ?? throw new InvalidOperationException($"No integration for payment provider '{provider}'."))
            .CreateGateway(credentials);

    public IPaymentIntegration? Integration(PaymentProviderCode provider) =>
        _byProvider.TryGetValue(provider, out var integration) ? integration : null;

    private static PaymentIntegration Describe(IPaymentIntegration integration) =>
        new(integration.Provider, integration.Name, integration.CredentialFields);

    private static Dictionary<PaymentProviderCode, IPaymentIntegration> Index(IEnumerable<IPaymentIntegration> integrations)
    {
        var index = new Dictionary<PaymentProviderCode, IPaymentIntegration>();
        foreach (var integration in integrations)
        {
            if (!index.TryAdd(integration.Provider, integration))
            {
                throw new InvalidOperationException($"Payment integration '{integration.Provider}' is registered twice.");
            }
        }

        return index;
    }
}
