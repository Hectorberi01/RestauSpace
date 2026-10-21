using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Payments.Application;
using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Domain;
using Payments.Infrastructure.Gateways;
using Payments.Infrastructure.Persistence;
using Payments.Infrastructure.Webhooks;
using Testcontainers.PostgreSql;

namespace Payments.Infrastructure;

/// <summary>
/// Un vrai PostgreSQL (Docker) avec les migrations du module appliquées, partagé par la classe de test. L'intégration
/// sandbox est activée ; « testpay » est une intégration en ligne qui note ce qu'on demande au prestataire.
/// </summary>
public sealed class PaymentsDatabase : IAsyncLifetime
{
    /// <summary>Le secret de webhook que les restaurants de test saisissent pour le sandbox.</summary>
    public const string SandboxSecret = "test-sandbox-secret";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private ServiceProvider _services = null!;

    public FakeClock Clock { get; } = new();

    public RecordingGateway TestPay { get; } = new();

    public IServiceProvider Services => _services;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IClock>(Clock)
            .AddScoped<BackgroundTenant>()
            .AddScoped<RequestTenant>()
            .AddScoped<ITenantContext>(provider => provider.GetRequiredService<RequestTenant>())
            .AddSingleton<IPaymentIntegration>(new TestPayIntegration(TestPay))
            .AddPaymentsInfrastructure(_container.GetConnectionString(), options =>
            {
                options.CredentialsKey = Enumerable.Range(0, 32).Select(index => (byte)(index * 3)).ToArray();
                options.EnableSandbox = true;
            })
            // Ce que l'hôte et Payments.Api ajoutent : le cas d'usage appliqué par les webhooks et ses ports.
            .AddScoped<PaymentsWorkspace>()
            .AddScoped<IPaymentTenants, AllTenantsWritable>()
            .AddScoped<IPaymentOrders, NoOrders>()
            .AddScoped<ICommandHandler<ApplyProviderResultCommand>, ApplyProviderResultHandler>()
            .BuildServiceProvider(validateScopes: true);

        await using (var scope = NewScope(tenantId: null))
        {
            await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.MigrateAsync();
        }

        await _services.SeedBuiltInPaymentProvidersAsync();
    }

    /// <summary>
    /// Une configuration en ligne active pour <paramref name="tenantId"/> (mode restaurant), identifiants rangés par le
    /// vrai coffre ; le prestataire n'est pas enregistré au catalogue (les règles du catalogue sont testées par
    /// l'Application).
    /// </summary>
    public async Task<RestaurantPaymentConfiguration> GivenOnlineConfigurationAsync(
        TenantId tenantId, string provider, IReadOnlyDictionary<string, string> credentials)
    {
        var catalogEntry = PaymentProvider.Register(
            PaymentProviderCode.Create(provider),
            PaymentProviderType.Online,
            provider,
            PaymentProviderCapability.Create(new[] { PaymentMethodType.Card }, supportsRefund: true, supportsWebhooks: true),
            isCodeUnique: true,
            Clock);
        var configuration = RestaurantPaymentConfiguration.Create(tenantId, catalogEntry, PaymentMode.TenantManaged, isFirstForTenant: true, Clock);
        await using var scope = NewScope(tenantId);
        var reference = PaymentCredentialReference.ForTenant(tenantId, catalogEntry.Code);
        await scope.ServiceProvider.GetRequiredService<IPaymentSecretStore>().StoreAsync(reference, credentials);
        configuration.Configure(reference, Clock);
        configuration.Enable(providerIsAvailable: true, Clock);
        scope.ServiceProvider.GetRequiredService<IRestaurantPaymentConfigurationRepository>().Add(configuration);
        await scope.ServiceProvider.GetRequiredService<IPaymentsUnitOfWork>().SaveChangesAsync();
        return configuration;
    }

    /// <summary>
    /// Une configuration prête et active de <paramref name="tenantId"/>, NON enregistrée : de quoi créer des paiements
    /// (le domaine vérifie la configuration à la création, la persistance des paiements n'en dépend pas).
    /// </summary>
    public RestaurantPaymentConfiguration Accepting(
        TenantId tenantId, string provider, PaymentProviderType type = PaymentProviderType.Online, PaymentMethodType method = PaymentMethodType.Card)
    {
        var code = PaymentProviderCode.Create(provider);
        var catalogEntry = PaymentProvider.Register(
            code, type, provider, PaymentProviderCapability.Create(new[] { method }, supportsRefund: true), isCodeUnique: true, Clock);
        var configuration = RestaurantPaymentConfiguration.Create(tenantId, catalogEntry, PaymentMode.TenantManaged, isFirstForTenant: true, Clock);
        if (type == PaymentProviderType.Online)
        {
            configuration.Configure(PaymentCredentialReference.ForTenant(tenantId, code), Clock);
            configuration.Enable(providerIsAvailable: true, Clock);
        }

        return configuration;
    }

    /// <summary>L'établissement de toutes les commandes de test.</summary>
    public static readonly LocationId Location = LocationId.New();

    /// <summary>Un scope = une requête pour <paramref name="tenantId"/> (null : aucun restaurant choisi).</summary>
    public AsyncServiceScope NewScope(TenantId? tenantId)
    {
        var scope = _services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<RequestTenant>().Current = tenantId;
        return scope;
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _container.DisposeAsync();
    }

    private sealed class AllTenantsWritable : IPaymentTenants
    {
        public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class NoOrders : IPaymentOrders
    {
        public Task<PaymentOrder?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PaymentOrder?>(null);
    }
}

/// <summary>
/// Le tenant d'une « requête » de test, fixé à l'ouverture de son scope ; à défaut, celui d'un traitement d'arrière-plan
/// (<see cref="BackgroundTenant"/>), comme dans l'hôte hors requête HTTP.
/// </summary>
public sealed class RequestTenant(BackgroundTenant background) : ITenantContext
{
    public TenantId? Current { get; set; }

    public TenantId? CurrentTenantId => Current ?? background.Current;
}

/// <summary>Intégration « testpay » (champ <c>api_key</c>) : toujours le même prestataire qui enregistre.</summary>
public sealed class TestPayIntegration(RecordingGateway gateway) : IPaymentIntegration
{
    public PaymentProviderCode Provider => gateway.Provider;

    public string Name => "TestPay";

    public IReadOnlyList<string> CredentialFields { get; } = ["api_key"];

    public IPaymentGateway CreateGateway(IReadOnlyDictionary<string, string> credentials)
    {
        gateway.ApiKeys.Enqueue(credentials["api_key"]);
        return gateway;
    }

    public IPaymentWebhookReader CreateWebhookReader(IReadOnlyDictionary<string, string> credentials) =>
        throw new NotSupportedException("TestPay sends no webhook in these tests.");
}

/// <summary>Prestataire en ligne « testpay » : note les remboursements et annulations demandés, et les clés utilisées.</summary>
public sealed class RecordingGateway : IPaymentGateway
{
    public PaymentProviderCode Provider { get; } = PaymentProviderCode.Create("testpay");

    public ConcurrentQueue<GatewayRefundRequest> Refunds { get; } = new();

    public ConcurrentQueue<string> Cancelled { get; } = new();

    public ConcurrentQueue<string> ApiKeys { get; } = new();

    /// <summary>Vrai : les remboursements sont refusés définitivement.</summary>
    public bool RefuseRefunds { get; set; }

    public Task<GatewayCheckout> StartAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(new GatewayCheckout(ProviderReference.Create("tp_" + Guid.NewGuid().ToString("N")), null));

    public Task CancelAsync(ProviderReference reference, CancellationToken cancellationToken = default)
    {
        Cancelled.Enqueue(reference.Value);
        return Task.CompletedTask;
    }

    public Task RequestRefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default)
    {
        if (RefuseRefunds)
        {
            throw new PaymentGatewayException("Refund window closed.", isPermanent: true);
        }

        Refunds.Enqueue(request);
        return Task.CompletedTask;
    }
}
