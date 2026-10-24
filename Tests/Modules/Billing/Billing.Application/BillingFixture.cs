using Billing.Application.Abstractions;
using Billing.Application.Commands;
using Billing.Application.Queries;
using Billing.Domain;

namespace Billing.Application;

/// <summary>
/// Ports en mémoire du module Billing. Comme les vrais, les abonnements et compteurs ne répondent que pour le tenant
/// donné ; les plans sont globaux ; le back-office (<see cref="IPlatformSubscriptions"/>) voit tout. Le tenant de « la
/// requête » est <see cref="CurrentTenantId"/> (null : la plateforme, sans restaurant).
/// </summary>
public sealed class BillingFixture
    : ITenantContext, IBillingUnitOfWork, IBillingTenants, IPlanRepository, ISubscriptionRepository, IPlatformSubscriptions,
      IUsageCounterRepository, ILiveSubscriptionChecker, IUsageCounterUniquenessChecker, ISubscriptionPaymentRepository,
      ITenantAddOnRepository, ISubscriptionCheckoutRepository, ISubscriptionCollector, IInvoiceIssuer, IOnlinePaymentRouteRepository,
      IBillingOverviewReader
{
    public BillingFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        Writable[TenantA] = true;
        Writable[TenantB] = true;
        CurrentTenantId = TenantA;
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    /// <summary>Devise des tenants connus (EUR si absente).</summary>
    public Dictionary<TenantId, Currency> Currencies { get; } = [];

    /// <summary>Tenants connus ; true = accepte les écritures métier.</summary>
    public Dictionary<TenantId, bool> Writable { get; } = [];

    public List<Plan> Plans { get; } = [];

    public List<SaasSubscription> Subscriptions { get; } = [];

    public List<UsageCounter> Counters { get; } = [];

    public List<SubscriptionPayment> Payments { get; } = [];

    public List<TenantAddOn> AddOns { get; } = [];

    public BillingSettings Settings { get; } = new();

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public List<SubscriptionCheckout> Checkouts { get; } = [];

    /// <summary>Prestataire des abonnements ; null : pas de paiement en ligne.</summary>
    public string? CollectorProvider { get; set; } = "fedapay";

    /// <summary>Ce que répond le prestataire à la relecture, par référence (en attente si absente).</summary>
    public Dictionary<string, CollectionStatus> ProviderStatuses { get; } = [];

    /// <summary>Le prestataire refuse de démarrer un encaissement.</summary>
    public bool ProviderDown { get; set; }

    public List<(SubscriptionCheckoutId Checkout, Money Amount, string Label, string? Phone)> Started { get; } = [];

    /// <summary>Prestataires choisis par la plateforme, par devise.</summary>
    public List<OnlinePaymentRoute> Routes { get; } = [];

    /// <summary>Couples « prestataire:DEVISE » qui ne peuvent pas encaisser (les autres le peuvent).</summary>
    public HashSet<string> NotReady { get; } = [];

    public CheckoutDesk Desk => new(Workspace, this, this, this, Clock);

    public ListOnlinePaymentRoutesHandler ListRoutes => new(Workspace, this, this);

    public SetOnlinePaymentRouteHandler SetRoute => new(this, this, this, Clock);

    public AdminGrantComplimentaryHandler Grant => new(Workspace, Clock);

    public AdminEndComplimentaryHandler EndOffer => new(Workspace, Clock);

    public NoticeSubscriptionEndingHandler NoticeEnding => new(Workspace, Clock);

    public GetBillingOverviewHandler Overview => new(Workspace, this, Clock);

    public StartSubscriptionCheckoutHandler StartCheckout => new(Desk, Clock);

    public RefreshSubscriptionCheckoutHandler RefreshCheckout => new(Desk);

    public SettleSubscriptionCheckoutHandler SettleCheckout => new(Desk);

    public GetOnlinePaymentHandler OnlinePayment => new(Desk, Clock);

    public GetMyInvoiceHandler Invoice => new(Workspace, this);

    private int _invoiceRank;

    public BillingWorkspace Workspace => new(this, this, this, this, this, this, this, this, this, this, Settings, this);

    public ActivateTenantAddOnHandler ActivateAddOn => new(Workspace, Clock);

    public ChangeTenantAddOnQuantityHandler ChangeAddOnQuantity => new(Workspace, Clock);

    public CancelTenantAddOnHandler CancelAddOn => new(Workspace, Clock);

    public ListMyAddOnsHandler ListMyAddOns => new(Workspace);

    public SubscribeAddOnHandler SubscribeAddOn => new(Workspace, Clock);

    public ChangeMyAddOnQuantityHandler ChangeMyAddOnQuantity => new(Workspace, Clock);

    public CancelMyAddOnHandler CancelMyAddOn => new(Workspace, Clock);

    public GetSubscriptionChargeHandler GetCharge => new(Workspace);

    public ListActiveAddOnsHandler ListActiveAddOns => new(Workspace);

    public ListTenantAddOnsHandler ListTenantAddOns => new(Workspace);

    public CreatePlanHandler CreatePlan => new(Workspace, Clock);

    public ChangePlanPriceHandler ChangePrice => new(Workspace, Clock);

    public SetPlanEntitlementHandler SetEntitlement => new(Workspace, Clock);

    public ArchivePlanHandler ArchivePlan => new(Workspace, Clock);

    public StartSubscriptionHandler Start => new(Workspace, Clock);

    public ChangeSubscriptionPlanHandler ChangePlan => new(Workspace, Clock);

    public ScheduleSubscriptionCancellationHandler ScheduleCancellation => new(Workspace, Clock);

    public UnscheduleSubscriptionCancellationHandler UnscheduleCancellation => new(Workspace, Clock);

    public UnscheduleSubscriptionPlanChangeHandler UnschedulePlanChange => new(Workspace, Clock);

    public AdminStartSubscriptionHandler AdminStart => new(Workspace, Clock);

    public RecordSubscriptionPaymentHandler RecordPayment => new(Workspace, Clock);

    public AdminMarkSubscriptionPastDueHandler MarkPastDue => new(Workspace, Clock);

    public AdminSuspendSubscriptionHandler AdminSuspend => new(Workspace, Clock);

    public AdminCancelSubscriptionHandler AdminCancel => new(Workspace, Clock);

    public ProcessDueSubscriptionHandler ProcessDue => new(Workspace, Clock);

    public ConsumeFeatureHandler Consume => new(Workspace, Clock);

    public ListPlansHandler ListPlans => new(Workspace);

    public ListMyPlansHandler ListMyPlans => new(Workspace);

    public GetPlanHandler GetPlan => new(Workspace);

    public GetMySubscriptionHandler GetMine => new(Workspace, Clock);

    public GetFeatureLimitHandler GetLimit => new(Workspace);

    public ListSubscriptionsHandler ListSubscriptions => new(Workspace);

    public GetSubscriptionHandler GetSubscription => new(Workspace);

    public ListSubscriptionPaymentsHandler ListPayments => new(Workspace);

    public ListMySubscriptionPaymentsHandler ListMyPayments => new(Workspace);

    /// <summary>Un plan actif, 29 EUR par mois, 100 SMS par mois et e-mails sans limite (sauf indication contraire).</summary>
    public Task<Guid> PlanAsync(string name = "Essentiel", decimal price = 29m, BillingInterval interval = BillingInterval.Monthly, params EntitlementInput[] entitlements) =>
        CreatePlan.HandleAsync(new CreatePlanCommand(
            name,
            price,
            "EUR",
            interval,
            entitlements.Length > 0
                ? entitlements
                : new[]
                {
                    new EntitlementInput(BillingFeatures.NotificationsSmsMonthly, true, 100),
                    new EntitlementInput(BillingFeatures.NotificationsEmailMonthly, true),
                }));

    public SaasSubscription SubscriptionById(Guid id) => Subscriptions.Single(subscription => subscription.Id.Value == id);

    /// <summary>Comme la persistance : un paiement nouveau reçoit le numéro de facture suivant.</summary>
    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        foreach (var payment in Payments.Where(payment => payment.InvoiceNumber is null))
        {
            payment.AssignInvoiceNumber(SubscriptionPayment.FormatInvoiceNumber(payment.RecordedAtUtc.Year, ++_invoiceRank));
        }

        return Task.CompletedTask;
    }

    Task<string?> IBillingTenants.NameAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(tenantId == TenantA ? "Chez A" : null);

    InvoiceIssuer IInvoiceIssuer.Current => new("RestauSpace SAS", "Cotonou", "IFU 123", null, "factures@example.com", "TVA non applicable");

    Task<SubscriptionCheckout?> ISubscriptionCheckoutRepository.FindAsync(TenantId tenantId, SubscriptionCheckoutId id, CancellationToken cancellationToken) =>
        Task.FromResult(Checkouts.SingleOrDefault(checkout => checkout.TenantId == tenantId && checkout.Id == id));

    Task<SubscriptionCheckout?> ISubscriptionCheckoutRepository.FindPendingAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Checkouts.Where(checkout => checkout.TenantId == tenantId && checkout.Status == SubscriptionCheckoutStatus.Pending)
            .OrderByDescending(checkout => checkout.CreatedAtUtc).FirstOrDefault());

    Task<IReadOnlyList<(TenantId TenantId, SubscriptionCheckoutId Id)>> ISubscriptionCheckoutRepository.ListPendingAsync(int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<(TenantId, SubscriptionCheckoutId)>>(Checkouts.Where(checkout => checkout.Status == SubscriptionCheckoutStatus.Pending)
            .OrderBy(checkout => checkout.CreatedAtUtc).Take(take).Select(checkout => (checkout.TenantId, checkout.Id)).ToList());

    Task<(TenantId TenantId, SubscriptionCheckoutId Id)?> ISubscriptionCheckoutRepository.FindPendingByReferenceAsync(
        string provider, string providerReference, CancellationToken cancellationToken) =>
        Task.FromResult(Checkouts
            .Where(checkout => checkout.Status == SubscriptionCheckoutStatus.Pending && checkout.Provider == provider && checkout.ProviderReference == providerReference)
            .Select(checkout => ((TenantId, SubscriptionCheckoutId)?)(checkout.TenantId, checkout.Id))
            .FirstOrDefault());

    void ISubscriptionCheckoutRepository.Add(SubscriptionCheckout checkout) => Checkouts.Add(checkout);

    string? ISubscriptionCollector.DefaultProvider => CollectorProvider;

    Task<bool> ISubscriptionCollector.IsReadyAsync(string provider, Currency currency, CancellationToken cancellationToken) =>
        Task.FromResult(!NotReady.Contains($"{provider}:{currency.Code}"));

    Task<CollectionStart> ISubscriptionCollector.StartAsync(
        string provider, TenantId tenantId, SubscriptionCheckoutId checkoutId, Money amount, string label, string? payerPhone, CancellationToken cancellationToken)
    {
        if (ProviderDown)
        {
            throw new DomainException(new DomainError("payments.provider.call_failed", "down"));
        }

        Started.Add((checkoutId, amount, label, payerPhone));
        return Task.FromResult(new CollectionStart(provider, "ref-" + Started.Count, new Uri("https://pay.example.com/" + Started.Count)));
    }

    Task<CollectionStatus> ISubscriptionCollector.StatusAsync(string provider, string reference, CancellationToken cancellationToken) =>
        Task.FromResult(ProviderStatuses.GetValueOrDefault(reference, new CollectionStatus(CollectionState.Pending, null)));

    Task<SubscriptionPayment?> ISubscriptionPaymentRepository.FindAsync(TenantId tenantId, SubscriptionPaymentId id, CancellationToken cancellationToken) =>
        Task.FromResult(Payments.SingleOrDefault(payment => payment.TenantId == tenantId && payment.Id == id));

    Task<bool> IBillingTenants.ExistsAsync(TenantId tenantId, CancellationToken cancellationToken) => Task.FromResult(Writable.ContainsKey(tenantId));

    Task<bool> IBillingTenants.AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Writable.TryGetValue(tenantId, out var writable) && writable);

    Task<Currency?> IBillingTenants.DefaultCurrencyAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Writable.ContainsKey(tenantId) ? Currencies.GetValueOrDefault(tenantId, Currency.EUR) : null);

    Task<Plan?> IPlanRepository.FindAsync(PlanId id, CancellationToken cancellationToken) =>
        Task.FromResult<Plan?>(Plans.SingleOrDefault(plan => plan.Id == id));

    Task<IReadOnlyList<Plan>> IPlanRepository.ListAsync(bool activeOnly, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Plan>>(Plans
            .Where(plan => !activeOnly || plan.Status == PlanStatus.Active)
            .OrderBy(plan => plan.Name, StringComparer.Ordinal)
            .ToList());

    void IPlanRepository.Add(Plan plan) => Plans.Add(plan);

    Task<SaasSubscription?> ISubscriptionRepository.FindLiveAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<SaasSubscription?>(Subscriptions.SingleOrDefault(s => s.TenantId == tenantId && s.Status != SubscriptionStatus.Cancelled));

    Task<SaasSubscription?> ISubscriptionRepository.FindAsync(TenantId tenantId, SubscriptionId id, CancellationToken cancellationToken) =>
        Task.FromResult<SaasSubscription?>(Subscriptions.SingleOrDefault(s => s.TenantId == tenantId && s.Id == id));

    Task<bool> ISubscriptionRepository.HasOtherAsync(TenantId tenantId, SubscriptionId? except, CancellationToken cancellationToken) =>
        Task.FromResult(Subscriptions.Any(s => s.TenantId == tenantId && s.Id != except));

    void ISubscriptionRepository.Add(SaasSubscription subscription) => Subscriptions.Add(subscription);

    Task<SaasSubscription?> IPlatformSubscriptions.FindAsync(SubscriptionId id, CancellationToken cancellationToken) =>
        Task.FromResult<SaasSubscription?>(Subscriptions.SingleOrDefault(s => s.Id == id));

    Task<IReadOnlyList<SaasSubscription>> IPlatformSubscriptions.ListAsync(
        TenantId? tenantId, SubscriptionStatus? status, int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SaasSubscription>>(Subscriptions
            .Where(s => (tenantId is null || s.TenantId == tenantId) && (status is null || s.Status == status))
            .OrderByDescending(s => s.StartedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToList());

    Task<IReadOnlyList<DueSubscription>> IPlatformSubscriptions.ListDueAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DueSubscription>>(Subscriptions
            .Where(s => s.Status != SubscriptionStatus.Cancelled && s.CurrentPeriod.EndUtc <= nowUtc)
            .Select(s => new DueSubscription(s.TenantId, s.Id))
            .Take(take)
            .ToList());

    Task<UsageCounter?> IUsageCounterRepository.FindAsync(TenantId tenantId, FeatureCode featureCode, CancellationToken cancellationToken) =>
        Task.FromResult<UsageCounter?>(Counters.SingleOrDefault(c => c.TenantId == tenantId && c.FeatureCode == featureCode));

    Task<IReadOnlyList<UsageCounter>> IUsageCounterRepository.ListAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UsageCounter>>(Counters.Where(c => c.TenantId == tenantId).ToList());

    void IUsageCounterRepository.Add(UsageCounter counter) => Counters.Add(counter);

    Task<TenantAddOn?> ITenantAddOnRepository.FindAsync(TenantId tenantId, TenantAddOnId id, CancellationToken cancellationToken) =>
        Task.FromResult(AddOns.SingleOrDefault(addOn => addOn.TenantId == tenantId && addOn.Id == id));

    Task<TenantAddOn?> ITenantAddOnRepository.FindActiveAsync(TenantId tenantId, PlanId planId, CancellationToken cancellationToken) =>
        Task.FromResult(AddOns.SingleOrDefault(addOn => addOn.TenantId == tenantId && addOn.PlanId == planId && addOn.IsActive));

    Task<IReadOnlyList<TenantAddOn>> ITenantAddOnRepository.ListAsync(TenantId tenantId, bool activeOnly, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TenantAddOn>>(AddOns.Where(addOn => addOn.TenantId == tenantId && (!activeOnly || addOn.IsActive)).ToList());

    Task<IReadOnlyList<TenantAddOn>> ITenantAddOnRepository.ListActiveForPlatformAsync(int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TenantAddOn>>(AddOns.Where(addOn => addOn.IsActive).OrderByDescending(addOn => addOn.ActivatedAtUtc).Skip(skip).Take(take).ToList());

    void ITenantAddOnRepository.Add(TenantAddOn addOn) => AddOns.Add(addOn);

    Task<bool> ILiveSubscriptionChecker.HasNoLiveSubscriptionAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(!Subscriptions.Any(s => s.TenantId == tenantId && s.Status != SubscriptionStatus.Cancelled));

    Task<bool> IUsageCounterUniquenessChecker.IsUniqueAsync(TenantId tenantId, FeatureCode featureCode, CancellationToken cancellationToken) =>
        Task.FromResult(!Counters.Any(c => c.TenantId == tenantId && c.FeatureCode == featureCode));

    void ISubscriptionPaymentRepository.Add(SubscriptionPayment payment) => Payments.Add(payment);

    Task<IReadOnlyList<SubscriptionPayment>> ISubscriptionPaymentRepository.ListAsync(
        TenantId tenantId, int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionPayment>>(Payments
            .Where(p => p.TenantId == tenantId)
            .OrderByDescending(p => p.RecordedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToList());

    Task<IReadOnlyList<SubscriptionPayment>> ISubscriptionPaymentRepository.ListAllAsync(
        TenantId? tenantId, SubscriptionId? subscriptionId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int skip, int take,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SubscriptionPayment>>(Payments
            .Where(p => (tenantId is null || p.TenantId == tenantId) && (subscriptionId is null || p.SubscriptionId == subscriptionId))
            .Where(p => (fromUtc is null || p.RecordedAtUtc >= fromUtc) && (toUtc is null || p.RecordedAtUtc < toUtc))
            .OrderByDescending(p => p.RecordedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToList());

    Task<IReadOnlyList<DueSubscription>> IPlatformSubscriptions.ListEndingAsync(DateTimeOffset nowUtc, TimeSpan window, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DueSubscription>>(Subscriptions
            .Where(s => (s.Status == SubscriptionStatus.Trialing || (s.IsComplimentary && !s.IsComplimentaryUnlimited)) && !s.CancelAtPeriodEnd
                && s.CurrentPeriod.EndUtc > nowUtc && s.CurrentPeriod.EndUtc <= nowUtc + window && s.EndingNoticeSentForUtc != s.CurrentPeriod.EndUtc)
            .Select(s => new DueSubscription(s.TenantId, s.Id))
            .Take(take)
            .ToList());

    Task<OnlinePaymentRoute?> IOnlinePaymentRouteRepository.FindAsync(string currencyCode, CancellationToken cancellationToken) =>
        Task.FromResult(Routes.SingleOrDefault(route => route.CurrencyCode == currencyCode));

    Task<IReadOnlyList<OnlinePaymentRoute>> IOnlinePaymentRouteRepository.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OnlinePaymentRoute>>(Routes.OrderBy(route => route.CurrencyCode, StringComparer.Ordinal).ToList());

    void IOnlinePaymentRouteRepository.Add(OnlinePaymentRoute route) => Routes.Add(route);

    void IOnlinePaymentRouteRepository.Remove(OnlinePaymentRoute route) => Routes.Remove(route);

    Task<IReadOnlyList<SaasSubscription>> IBillingOverviewReader.ListLiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SaasSubscription>>(Subscriptions.Where(s => s.Status != SubscriptionStatus.Cancelled).ToList());

    Task<IReadOnlyList<SaasSubscription>> IBillingOverviewReader.ListStartedSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SaasSubscription>>(Subscriptions.Where(s => s.StartedAtUtc >= sinceUtc).ToList());

    Task<int> IBillingOverviewReader.CountCancelledSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken) =>
        Task.FromResult(Subscriptions.Count(s => s.Status == SubscriptionStatus.Cancelled && s.CancelledAtUtc >= sinceUtc));

    Task<IReadOnlyList<OverviewPayment>> IBillingOverviewReader.ListPaymentsSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OverviewPayment>>(Payments.Where(p => p.RecordedAtUtc >= sinceUtc)
            .Select(p => new OverviewPayment(p.TenantId.Value, p.SubscriptionId.Value, p.Amount.Amount, p.Amount.Currency.Code, p.RecordedAtUtc)).ToList());

    Task<IReadOnlyList<OverviewFailedCheckout>> IBillingOverviewReader.ListFailedCheckoutsSinceAsync(DateTimeOffset sinceUtc, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OverviewFailedCheckout>>(Checkouts
            .Where(c => c.Status is SubscriptionCheckoutStatus.Failed or SubscriptionCheckoutStatus.Expired && c.CreatedAtUtc >= sinceUtc)
            .OrderByDescending(c => c.CreatedAtUtc).Take(take)
            .Select(c => new OverviewFailedCheckout(c.TenantId.Value, c.Amount.Amount, c.Amount.Currency.Code, c.Provider, c.Status, c.CreatedAtUtc)).ToList());

    Task<IReadOnlyList<(TenantAddOn AddOn, Plan Plan)>> IBillingOverviewReader.ListActiveAddOnsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<(TenantAddOn, Plan)>>(AddOns.Where(addOn => addOn.IsActive)
            .Select(addOn => (addOn, Plans.Single(plan => plan.Id == addOn.PlanId))).ToList());
}
