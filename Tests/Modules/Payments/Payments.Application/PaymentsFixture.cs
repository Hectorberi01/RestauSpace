using Payments.Application.Abstractions;
using Payments.Application.Commands;
using Payments.Application.Queries;
using Payments.Domain;

namespace Payments.Application;

/// <summary>
/// Ports en mémoire du module Payments. Comme les vrais, ils ne répondent jamais que dans le tenant qui leur est donné.
/// Le tenant de « la requête » est <see cref="CurrentTenantId"/> ; commandes en francs CFA.
/// </summary>
public sealed class PaymentsFixture
    : ITenantContext, IPaymentsUnitOfWork, IPaymentOrderLock, IPaymentTenants, IPaymentOrders, IPaymentRepository, IPaymentReservations,
      IPaymentIntegrations, IPaymentSecretStore, IPaymentLocations
{
    public PaymentsFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        Writable[TenantA] = true;
        Writable[TenantB] = true;
        CurrentTenantId = TenantA;
        Gateway = new FakeGateway(this);
        ProviderStore = new ProviderCatalog(this);
        ConfigurationStore = new ConfigurationCatalog(this);

        // La plateforme : les moyens du comptoir intégrés et « fakepay » en ligne.
        foreach (var (code, name, capability) in BuiltInPaymentProviders.OnSite)
        {
            Providers.Add(PaymentProvider.Register(code, PaymentProviderType.OnSite, name, capability, isCodeUnique: true, Clock));
        }

        Providers.Add(PaymentProvider.Register(
            FakePay,
            PaymentProviderType.Online,
            "FakePay",
            PaymentProviderCapability.Create(
                new[] { PaymentMethodType.Card, PaymentMethodType.ApplePay },
                supportsRefund: true,
                supportsPartialRefund: true,
                supportsWebhooks: true,
                supportsPlatformManagedMode: true),
            isCodeUnique: true,
            Clock));

        // Le restaurant A accepte espèces, mobile money et fakepay (son compte marchand, identifiants au coffre).
        Accept(TenantA, "cash");
        Accept(TenantA, "mobile_money");
        var online = Accept(TenantA, "fakepay");
        var reference = PaymentCredentialReference.ForTenant(TenantA, FakePay);
        Secrets[reference.Value] = new Dictionary<string, string> { ["api_key"] = "sk_a" };
        online.Configure(reference, Clock);
        online.Enable(providerIsAvailable: true, Clock);
    }

    public static readonly PaymentProviderCode FakePay = PaymentProviderCode.Create("fakepay");

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    /// <summary>L'établissement des commandes de test (sauf indication contraire).</summary>
    public LocationId Location { get; } = LocationId.New();

    public TenantId? CurrentTenantId { get; set; }

    public Dictionary<TenantId, bool> Writable { get; } = [];

    /// <summary>Faux module Orders : commandes par id.</summary>
    public Dictionary<OrderId, (TenantId Tenant, PaymentOrder Order)> Orders { get; } = [];

    public List<Payment> Payments { get; } = [];

    /// <summary>Acomptes des réservations (faux module Reservations).</summary>
    public Dictionary<Guid, (TenantId Tenant, PaymentDeposit Deposit)> Deposits { get; } = [];

    Task<PaymentDeposit?> IPaymentReservations.FindDepositAsync(TenantId tenantId, Guid reservationId, CancellationToken cancellationToken) =>
        Task.FromResult(Deposits.TryGetValue(reservationId, out var found) && found.Tenant == tenantId ? found.Deposit : null);

    /// <summary>Catalogue de la plateforme.</summary>
    public List<PaymentProvider> Providers { get; } = [];

    /// <summary>Configurations des restaurants (ce qu'ils acceptent réellement).</summary>
    public List<RestaurantPaymentConfiguration> Configurations { get; } = [];

    /// <summary>Faux coffre : identifiants en clair par référence (le vrai les chiffre, dans une table à part).</summary>
    public Dictionary<string, IReadOnlyDictionary<string, string>> Secrets { get; } = new(StringComparer.Ordinal);

    /// <summary>Références illisibles (clé du coffre changée) : la lecture lève une erreur.</summary>
    public HashSet<string> UnreadableSecrets { get; } = new(StringComparer.Ordinal);

    public ProviderCatalog ProviderStore { get; }

    public ConfigurationCatalog ConfigurationStore { get; }

    /// <summary>Identifiants en clair reçus par les prestataires créés (pour vérifier le compte marchand utilisé).</summary>
    public List<IReadOnlyDictionary<string, string>> GatewayCredentials { get; } = [];

    /// <summary>Le prestataire en ligne « fakepay ».</summary>
    public FakeGateway Gateway { get; }

    /// <summary>Ce que les ports ont vu, dans l'ordre (« lock », « list », « save »...).</summary>
    public List<string> Calls { get; } = [];

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public PaymentsWorkspace Workspace => new(this, this, this, this, ProviderStore, ConfigurationStore, this, this, this);

    public RecordOfflinePaymentHandler RecordOffline => new(Workspace, this, StoredValue, Clock);

    /// <summary>Faux module Loyalty : cartes cadeaux et avoirs.</summary>
    public FakeStoredValue StoredValue { get; } = new();

    /// <summary>Les prochaines sauvegardes échouent (compensations).</summary>
    public int FailingSaves { get; set; }

    public StartOnlinePaymentHandler StartOnline => new(Workspace, this, Clock);

    public RetryPaymentHandler Retry => new(Workspace, Clock);

    public RegisterPaymentProviderHandler RegisterProvider => new(ProviderStore, this, this, Clock);

    public DisablePaymentProviderHandler DisableProvider => new(ProviderStore, this, Clock);

    public SetPlatformCredentialsHandler SetPlatformCredentials => new(ProviderStore, this, this);

    public CreatePaymentConfigurationHandler CreateConfiguration => new(Workspace, Clock);

    public ConfigurePaymentConfigurationHandler ConfigureCredentials => new(Workspace, Clock);

    public ChangePaymentModeHandler ChangeMode => new(Workspace, Clock);

    public TestPaymentConfigurationHandler TestConfiguration => new(Workspace, Clock);

    public EnablePaymentConfigurationHandler EnableConfiguration => new(Workspace, Clock);

    public DisablePaymentConfigurationHandler DisableConfiguration => new(Workspace, Clock);

    public SetPaymentMethodActiveHandler SetMethodActive => new(Workspace, Clock);

    public AssignPaymentMethodToLocationHandler AssignToLocation => new(Workspace, this, Clock);

    public RemovePaymentMethodFromLocationHandler RemoveFromLocation => new(Workspace, Clock);

    /// <summary>Faux module Locations : établissements par tenant (celui des commandes de test, au Bénin, pour le tenant A).</summary>
    public Dictionary<LocationId, TenantId> Locations { get; } = [];

    public Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Locations.TryGetValue(locationId, out var owner) && owner == tenantId
            ? new LocationReference(tenantId, locationId, CountryCode.Create("BJ"))
            : null);

    public ListPaymentConfigurationsHandler ListConfigurations => new(Workspace);

    public GetAvailablePaymentMethodsHandler AvailableMethods => new(Workspace, this);

    public ListOfferedPaymentProvidersHandler ListOffered => new(Workspace);

    public CancelPaymentHandler Cancel => new(Workspace, Clock);

    public RequestRefundHandler Refund => new(Workspace, StoredValue, Clock);

    public RefundAsStoreCreditHandler RefundAsStoreCredit => new(Workspace, StoredValue, Clock);

    public ApplyProviderResultHandler Apply => new(Workspace, Clock);

    public GetPaymentByIdHandler GetById => new(Workspace);

    public GetOrderPaymentsHandler GetOrderPayments => new(Workspace);

    public GetOrderSettledAmountHandler GetSettled => new(Workspace);

    /// <summary>Une commande payable (par défaut) de 7 000 XOF du tenant A.</summary>
    public Guid GivenOrder(decimal total = 7000m, bool acceptsPayments = true, TenantId? tenantId = null, LocationId? location = null)
    {
        var id = OrderId.New();
        Orders[id] = (tenantId ?? TenantA, new PaymentOrder(id, location ?? Location, Money.Of(total, Currency.XOF), acceptsPayments));
        return id.Value;
    }

    public Payment Stored(Guid id) => Payments.Single(payment => payment.Id.Value == id);

    public PaymentProvider Provider(string code) => Providers.Single(provider => provider.Code.Value == code);

    public RestaurantPaymentConfiguration Configuration(TenantId tenant, string code) =>
        Configurations.Single(configuration => configuration.TenantId == tenant && configuration.Provider.Value == code);

    /// <summary>Le restaurant accepte ce prestataire du catalogue (actif aussitôt sur place).</summary>
    public RestaurantPaymentConfiguration Accept(TenantId tenant, string code, PaymentMode mode = PaymentMode.TenantManaged)
    {
        var configuration = RestaurantPaymentConfiguration.Create(tenant, Provider(code), mode, isFirstForTenant: true, Clock);
        Configurations.Add(configuration);
        return configuration;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (FailingSaves > 0)
        {
            FailingSaves--;
            throw new InvalidOperationException("save failed");
        }

        Calls.Add("save");
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task LockAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        Calls.Add("lock");
        return Task.CompletedTask;
    }

    Task<bool> IPaymentTenants.AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Writable.TryGetValue(tenantId, out var writable) && writable);

    Task<PaymentOrder?> IPaymentOrders.FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult<PaymentOrder?>(Orders.TryGetValue(orderId, out var order) && order.Tenant == tenantId ? order.Order : null);

    Task<Payment?> IPaymentRepository.FindAsync(TenantId tenantId, PaymentId id, CancellationToken cancellationToken) =>
        Task.FromResult<Payment?>(Payments.SingleOrDefault(payment => payment.TenantId == tenantId && payment.Id == id));

    Task<Payment?> IPaymentRepository.FindByProviderReferenceAsync(
        TenantId tenantId, PaymentProviderCode provider, ProviderReference reference, CancellationToken cancellationToken) =>
        Task.FromResult<Payment?>(Payments.SingleOrDefault(payment =>
            payment.TenantId == tenantId
            && payment.Provider == provider
            && payment.Attempts.Any(attempt => attempt.ProviderReference == reference)));

    Task<IReadOnlyList<Payment>> IPaymentRepository.ListByOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken)
    {
        Calls.Add("list");
        return Task.FromResult<IReadOnlyList<Payment>>(
            Payments.Where(payment => payment.TenantId == tenantId && payment.OrderId == orderId).ToList());
    }

    Task<IReadOnlyList<Payment>> IPaymentRepository.ListByReservationAsync(TenantId tenantId, Guid reservationId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Payment>>(Payments.Where(payment => payment.TenantId == tenantId && payment.ReservationId == reservationId).ToList());

    Task<bool> IPaymentRepository.HasPaymentsInProgressAsync(TenantId tenantId, PaymentProviderCode provider, CancellationToken cancellationToken) =>
        Task.FromResult(Payments.Any(payment =>
            payment.TenantId == tenantId
            && payment.Provider == provider
            && (payment.Status is PaymentStatus.Pending or PaymentStatus.Authorized
                || payment.Refunds.Any(refund => refund.Status == RefundStatus.Requested))));

    void IPaymentRepository.Add(Payment payment) => Payments.Add(payment);

    IReadOnlyList<PaymentIntegration> IPaymentIntegrations.All => [new PaymentIntegration(FakePay, "FakePay", ["api_key"])];

    PaymentIntegration? IPaymentIntegrations.Find(PaymentProviderCode provider) =>
        provider == FakePay ? new PaymentIntegration(FakePay, "FakePay", ["api_key"]) : null;

    IPaymentGateway IPaymentIntegrations.CreateGateway(PaymentProviderCode provider, IReadOnlyDictionary<string, string> credentials)
    {
        GatewayCredentials.Add(credentials);
        return Gateway;
    }

    Task IPaymentSecretStore.StoreAsync(
        PaymentCredentialReference reference, IReadOnlyDictionary<string, string> credentials, CancellationToken cancellationToken)
    {
        Calls.Add("secret");
        Secrets[reference.Value] = new Dictionary<string, string>(credentials, StringComparer.Ordinal);
        UnreadableSecrets.Remove(reference.Value);
        return Task.CompletedTask;
    }

    Task<IReadOnlyDictionary<string, string>?> IPaymentSecretStore.ReadAsync(PaymentCredentialReference reference, CancellationToken cancellationToken)
    {
        if (UnreadableSecrets.Contains(reference.Value))
        {
            throw new InvalidOperationException("Merchant credentials cannot be decrypted.");
        }

        return Task.FromResult<IReadOnlyDictionary<string, string>?>(Secrets.GetValueOrDefault(reference.Value));
    }

    public sealed class ProviderCatalog(PaymentsFixture fixture) : IPaymentProviderRepository
    {
        public Task<PaymentProvider?> FindAsync(PaymentProviderId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<PaymentProvider?>(fixture.Providers.SingleOrDefault(provider => provider.Id == id));

        public Task<PaymentProvider?> FindByCodeAsync(PaymentProviderCode code, CancellationToken cancellationToken = default) =>
            Task.FromResult<PaymentProvider?>(fixture.Providers.SingleOrDefault(provider => provider.Code == code));

        public Task<IReadOnlyList<PaymentProvider>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PaymentProvider>>(fixture.Providers.OrderBy(provider => provider.DisplayName, StringComparer.Ordinal).ToList());

        public void Add(PaymentProvider provider) => fixture.Providers.Add(provider);
    }

    public sealed class ConfigurationCatalog(PaymentsFixture fixture) : IRestaurantPaymentConfigurationRepository
    {
        public Task<RestaurantPaymentConfiguration?> FindAsync(
            TenantId tenantId, RestaurantPaymentConfigurationId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<RestaurantPaymentConfiguration?>(
                fixture.Configurations.SingleOrDefault(configuration => configuration.TenantId == tenantId && configuration.Id == id));

        public Task<RestaurantPaymentConfiguration?> FindByProviderAsync(
            TenantId tenantId, PaymentProviderCode provider, CancellationToken cancellationToken = default) =>
            Task.FromResult<RestaurantPaymentConfiguration?>(
                fixture.Configurations.SingleOrDefault(configuration => configuration.TenantId == tenantId && configuration.Provider == provider));

        public Task<IReadOnlyList<RestaurantPaymentConfiguration>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RestaurantPaymentConfiguration>>(
                fixture.Configurations.Where(configuration => configuration.TenantId == tenantId).ToList());

        public void Add(RestaurantPaymentConfiguration configuration) => fixture.Configurations.Add(configuration);
    }

    public sealed class FakeGateway(PaymentsFixture fixture) : IPaymentGateway
    {
        private int _next;

        public PaymentProviderCode Provider { get; } = FakePay;

        /// <summary>Vrai : le prochain appel échoue comme un prestataire injoignable.</summary>
        public bool Down { get; set; }

        public List<GatewayPaymentRequest> Started { get; } = [];

        public string LastReference => $"fp_{_next}";

        public Task<GatewayCheckout> StartAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default)
        {
            fixture.Calls.Add("gateway");
            if (Down)
            {
                throw new PaymentGatewayException("Provider unreachable.");
            }

            Started.Add(request);
            _next++;
            return Task.FromResult(new GatewayCheckout(ProviderReference.Create(LastReference), new Uri($"https://pay.example/{LastReference}")));
        }

        public Task CancelAsync(ProviderReference reference, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RequestRefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <summary>Vrai : le prestataire refuse les identifiants au prochain contrôle.</summary>
        public bool RejectsCredentials { get; set; }

        public Task<GatewayCredentialCheck> CheckCredentialsAsync(CancellationToken cancellationToken = default)
        {
            fixture.Calls.Add("check");
            if (Down)
            {
                throw new PaymentGatewayException("Provider unreachable.");
            }

            return Task.FromResult(new GatewayCredentialCheck(
                RejectsCredentials ? GatewayCredentialStatus.Rejected : GatewayCredentialStatus.Verified, true, []));
        }
    }

    /// <summary>Une carte par code ; chaque mouvement noté par référence (comme Loyalty, le rejeu est sans effet).</summary>
    public sealed class FakeStoredValue : IPaymentStoredValue
    {
        public Dictionary<string, (Guid Id, TenantId Tenant, decimal Balance)> Cards { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, (Guid CardId, decimal Amount)> Movements { get; } = new(StringComparer.Ordinal);

        public List<Guid> Cancelled { get; } = [];

        public string Give(TenantId tenant, decimal balance)
        {
            var code = "CODE-" + Cards.Count.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            Cards[code] = (Guid.NewGuid(), tenant, balance);
            return code;
        }

        public decimal BalanceOf(string code) => Cards[code].Balance;

        public Task<StoredValueDebit> RedeemAsync(TenantId tenantId, string code, Money amount, string reference, CancellationToken cancellationToken = default)
        {
            if (!Cards.TryGetValue(code, out var card) || card.Tenant != tenantId)
            {
                throw new NotFoundException(new DomainError("loyalty.stored_value.not_found", "The card does not exist."));
            }

            if (card.Balance < amount.Amount)
            {
                throw new DomainException(new DomainError("loyalty.stored_value.insufficient_balance", "The card balance is too low."));
            }

            Cards[code] = card with { Balance = card.Balance - amount.Amount };
            Movements[reference] = (card.Id, -amount.Amount);
            return Task.FromResult(new StoredValueDebit(card.Id, code[^4..]));
        }

        public Task CreditAsync(TenantId tenantId, Guid cardId, Money amount, string reference, CancellationToken cancellationToken = default)
        {
            if (!Movements.ContainsKey(reference))
            {
                Change(cardId, amount.Amount);
                Movements[reference] = (cardId, amount.Amount);
            }

            return Task.CompletedTask;
        }

        public Task ReverseAsync(TenantId tenantId, Guid cardId, string reference, CancellationToken cancellationToken = default)
        {
            if (Movements.Remove(reference, out var movement))
            {
                Change(cardId, -movement.Amount);
            }

            return Task.CompletedTask;
        }

        public Task<IssuedStoreCredit> IssueStoreCreditAsync(TenantId tenantId, Money amount, string note, CancellationToken cancellationToken = default)
        {
            var code = Give(tenantId, amount.Amount);
            return Task.FromResult(new IssuedStoreCredit(Cards[code].Id, code, code[^4..]));
        }

        public Task CancelAsync(TenantId tenantId, Guid cardId, string reason, CancellationToken cancellationToken = default)
        {
            Cancelled.Add(cardId);
            return Task.CompletedTask;
        }

        private void Change(Guid cardId, decimal by)
        {
            var code = Cards.Single(pair => pair.Value.Id == cardId).Key;
            Cards[code] = Cards[code] with { Balance = Cards[code].Balance + by };
        }
    }
}
