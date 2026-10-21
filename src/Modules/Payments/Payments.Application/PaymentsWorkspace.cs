using Payments.Application.Abstractions;
using Payments.Domain;

namespace Payments.Application;

/// <summary>
/// Étapes communes aux cas d'usage : le tenant vient du contexte (requête, ou message traité en arrière-plan), une
/// écriture exige un tenant qui les accepte, chaque paiement est chargé dans ce tenant seulement (404 sinon), puis on
/// sauvegarde. Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class PaymentsWorkspace(
    ITenantContext tenantContext,
    IPaymentTenants tenants,
    IPaymentOrders orders,
    IPaymentRepository payments,
    IPaymentProviderRepository providers,
    IRestaurantPaymentConfigurationRepository configurations,
    IPaymentIntegrations integrations,
    IPaymentSecretStore secrets,
    IPaymentsUnitOfWork unitOfWork)
{
    public IPaymentRepository Payments => payments;

    public IPaymentProviderRepository Providers => providers;

    public IRestaurantPaymentConfigurationRepository Configurations => configurations;

    public IPaymentIntegrations Integrations => integrations;

    public IPaymentSecretStore Secrets => secrets;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken))
        {
            throw new DomainException(PaymentsApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<PaymentOrder> RequireOrderAsync(TenantId tenantId, Guid orderId, CancellationToken cancellationToken) =>
        (orderId == Guid.Empty ? null : await orders.FindAsync(tenantId, OrderId.From(orderId), cancellationToken))
        ?? throw new NotFoundException(PaymentsApplicationErrors.OrderNotFound);

    public async Task<Payment> LoadAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await payments.FindAsync(tenantId, PaymentId.From(id), cancellationToken))
        ?? throw new NotFoundException(PaymentsApplicationErrors.PaymentNotFound);

    public async Task<RestaurantPaymentConfiguration> LoadConfigurationAsync(TenantId tenantId, Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await configurations.FindAsync(tenantId, RestaurantPaymentConfigurationId.From(id), cancellationToken))
        ?? throw new NotFoundException(PaymentsApplicationErrors.PaymentConfigurationNotFound);

    /// <summary>
    /// La configuration par laquelle un NOUVEAU paiement peut être pris : activée et prête chez le restaurant, prestataire
    /// proposé par la plateforme, dans la devise de la commande et de la bonne nature (sur place ou en ligne). Sinon 422.
    /// (La résolution complète selon l'établissement, le pays, le montant et le moyen viendra avec la policy de
    /// disponibilité.)
    /// </summary>
    public async Task<(RestaurantPaymentConfiguration Configuration, PaymentProvider Provider)> RequireAcceptedConfigurationAsync(
        TenantId tenantId, PaymentProviderCode code, PaymentProviderType type, Currency currency, CancellationToken cancellationToken)
    {
        var provider = await providers.FindByCodeAsync(code, cancellationToken);
        var configuration = await configurations.FindByProviderAsync(tenantId, code, cancellationToken);
        if (provider is null
            || configuration is not { IsEnabled: true, Status: PaymentConfigurationStatus.Configured }
            || !provider.CanTakePaymentsIn(currency))
        {
            throw new DomainException(PaymentsApplicationErrors.MethodNotAccepted);
        }

        DomainException.ThrowIf(provider.Type != type, PaymentsApplicationErrors.MethodTypeMismatch);
        return (configuration, provider);
    }

    /// <summary>
    /// Le prestataire en ligne branché sur le bon compte marchand : celui de la plateforme en mode plateforme, celui du
    /// restaurant sinon. Les identifiants sont lus dans le coffre ici seulement, et jamais conservés.
    /// </summary>
    public async Task<IPaymentGateway> GatewayForAsync(RestaurantPaymentConfiguration configuration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var reference = CredentialReferences.Of(configuration);
        if (reference is null || integrations.Find(configuration.Provider) is null)
        {
            throw new DomainException(PaymentsApplicationErrors.MethodNotAccepted);
        }

        IReadOnlyDictionary<string, string>? values;
        try
        {
            values = await secrets.ReadAsync(reference, cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            // Clé du coffre changée ou valeur altérée : les identifiants doivent être ressaisis.
            throw new DomainException(PaymentsApplicationErrors.CredentialsUnreadable, exception);
        }

        return integrations.CreateGateway(
            configuration.Provider, values ?? throw new DomainException(PaymentsApplicationErrors.CredentialsUnreadable));
    }

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);
}

/// <summary>Où lire les identifiants d'une configuration en ligne : compte de la plateforme ou du restaurant.</summary>
public static class CredentialReferences
{
    /// <summary>Null sur place, ou en mode restaurant tant que rien n'est rangé.</summary>
    public static PaymentCredentialReference? Of(RestaurantPaymentConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.ProviderType != PaymentProviderType.Online)
        {
            return null;
        }

        return configuration.Mode == PaymentMode.PlatformManaged
            ? PaymentCredentialReference.ForPlatform(configuration.Provider)
            : configuration.SecretReference;
    }
}

/// <summary>Ce que les paiements d'une commande couvrent déjà, et ce qu'il reste à payer.</summary>
public static class OrderBalance
{
    /// <summary>
    /// Part d'un paiement dans le total de la commande. Encaissé (remboursé ou non) : <see cref="Payment.SettledAmount"/>.
    /// En cours ou en échec : tout son montant reste réservé, puisqu'il peut encore être encaissé (un paiement échoué peut
    /// être relancé) ; l'annuler libère sa part. Annulé : rien (une capture tardive ne règle rien).
    /// </summary>
    public static Money CommittedBy(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return payment.Status is PaymentStatus.Pending or PaymentStatus.Authorized or PaymentStatus.Failed
            ? payment.Amount
            : payment.SettledAmount;
    }

    public static Money Committed(IEnumerable<Payment> payments, Currency currency) =>
        payments.Aggregate(Money.Zero(currency), (sum, payment) => sum + CommittedBy(payment));

    public static Money Settled(IEnumerable<Payment> payments, Currency currency) =>
        payments.Aggregate(Money.Zero(currency), (sum, payment) => sum + payment.SettledAmount);
}
