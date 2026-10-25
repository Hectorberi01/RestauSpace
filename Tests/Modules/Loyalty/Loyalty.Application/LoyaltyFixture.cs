using Loyalty.Application.Abstractions;
using Loyalty.Application.Commands;
using Loyalty.Application.Queries;
using Loyalty.Domain;

namespace Loyalty.Application;

/// <summary>
/// Ports en mémoire du module Loyalty. Comme les vrais, ils ne répondent jamais que dans le tenant qui leur est donné.
/// Le registre « stocké » est l'ensemble des écritures des comptes connus (les agrégats ne sont jamais rechargés ici).
/// Le tenant de « la requête » est <see cref="CurrentTenantId"/>.
/// </summary>
public sealed class LoyaltyFixture
    : ITenantContext, ILoyaltyUnitOfWork, ILoyaltyTenants, ILoyaltyAccountRepository, IRewardRepository, ILoyaltyLedgerLookup,
      ILoyaltyAccountUniquenessChecker, ILoyaltyProgramStore, ILoyaltyHistory, ILoyaltyExpiryCandidates, ILoyaltyCustomers, ILoyaltyOrders,
      IPlanFeatureGate
{
    public LoyaltyFixture()
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

    public Dictionary<TenantId, bool> Writable { get; } = [];

    public List<LoyaltyAccount> Accounts { get; } = [];

    public List<Reward> Rewards { get; } = [];

    public Dictionary<TenantId, LoyaltyProgram> Programs { get; } = [];

    public Dictionary<(TenantId, CustomerId), CustomerReference> Customers { get; } = [];

    public Dictionary<(TenantId, OrderId), LoyaltyOrder> Orders { get; } = [];

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public LoyaltyWorkspace Workspace => new(this, this, this, this, this, this, this, this, this);

    /// <summary>Fonctionnalités refusées par le plan (faux gate) : vide = tout est permis.</summary>
    public HashSet<string> DeniedFeatures { get; } = [];

    Task<bool> IPlanFeatureGate.AllowsAsync(TenantId tenantId, string feature, CancellationToken cancellationToken) =>
        Task.FromResult(!DeniedFeatures.Contains(feature));

    public OpenLoyaltyAccountHandler Open => new(Workspace, this, Clock);

    public AdjustLoyaltyPointsHandler Adjust => new(Workspace, Clock);

    public SuspendLoyaltyAccountHandler Suspend => new(Workspace, Clock);

    public ReactivateLoyaltyAccountHandler Reactivate => new(Workspace, Clock);

    public CloseLoyaltyAccountHandler Close => new(Workspace, Clock);

    public RedeemRewardForOrderHandler Redeem => new(Workspace, this, Clock);

    public EarnPointsForOrderHandler Earn => new(Workspace, this, Clock);

    public ReverseOrderLoyaltyHandler ReverseOrder => new(Workspace, Clock);

    public CancelRewardRedemptionHandler CancelRedemption => new(Workspace, Clock);

    public CloseCustomerLoyaltyAccountHandler CloseCustomer => new(Workspace, Clock);

    public MergeCustomerLoyaltyAccountsHandler MergeCustomers => new(Workspace, this, Clock);

    public ExpireInactivePointsHandler Expire => new(Workspace, this, Clock);

    public CreateRewardHandler CreateReward => new(Workspace, Clock);

    public ChangeRewardCostHandler ChangeCost => new(Workspace, Clock);

    public DeactivateRewardHandler DeactivateReward => new(Workspace, Clock);

    public SetLoyaltyProgramHandler SetProgram => new(Workspace, Clock);

    public GetLoyaltyAccountHandler GetAccount => new(Workspace);

    public GetLoyaltyAccountByCustomerHandler GetByCustomer => new(Workspace);

    public ListLoyaltyAccountsHandler ListAccounts => new(Workspace);

    public ListLoyaltyTransactionsHandler ListTransactions => new(Workspace, this);

    public ListRewardsHandler ListRewards => new(Workspace);

    public GetLoyaltyProgramHandler GetProgram => new(Workspace);

    public IEnumerable<LoyaltyTransaction> Ledger => Accounts.SelectMany(account => account.Transactions);

    /// <summary>Un client du tenant courant.</summary>
    public Guid NewCustomer(bool anonymized = false, TenantId? tenant = null)
    {
        var tenantId = tenant ?? CurrentTenantId!.Value;
        var customer = CustomerId.New();
        Customers[(tenantId, customer)] = new CustomerReference(tenantId, customer, anonymized);
        return customer.Value;
    }

    /// <summary>Une commande du tenant courant, en euros, en brouillon sauf indication contraire.</summary>
    public Guid NewOrder(Guid? customerId, bool acceptsDiscounts = true, string currency = "EUR")
    {
        var order = OrderId.New();
        Orders[(CurrentTenantId!.Value, order)] = new LoyaltyOrder(
            order, customerId is { } customer ? CustomerId.From(customer) : null, currency, acceptsDiscounts);
        return order.Value;
    }

    /// <summary>Barème activé : un point par euro, inscription automatique, expiration facultative.</summary>
    public void EnableProgram(decimal rate = 1m, bool autoEnroll = true, int? expiryDays = null) =>
        Programs[CurrentTenantId!.Value] = LoyaltyProgram.Create(true, rate, autoEnroll, expiryDays);

    public LoyaltyAccount AccountById(Guid id) => Accounts.Single(account => account.Id.Value == id);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    Task<bool> ILoyaltyTenants.AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Writable.TryGetValue(tenantId, out var writable) && writable);

    Task<LoyaltyAccount?> ILoyaltyAccountRepository.FindAsync(TenantId tenantId, LoyaltyAccountId id, CancellationToken cancellationToken) =>
        Task.FromResult<LoyaltyAccount?>(Accounts.SingleOrDefault(account => account.TenantId == tenantId && account.Id == id));

    Task<LoyaltyAccount?> ILoyaltyAccountRepository.FindByCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken) =>
        Task.FromResult<LoyaltyAccount?>(Accounts.SingleOrDefault(account => account.TenantId == tenantId && account.CustomerId == customerId));

    Task<IReadOnlyList<LoyaltyAccount>> ILoyaltyAccountRepository.ListAsync(
        TenantId tenantId, LoyaltyAccountStatus? status, int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LoyaltyAccount>>(Accounts
            .Where(account => account.TenantId == tenantId && (status is null || account.Status == status))
            .OrderByDescending(account => account.OpenedAtUtc)
            .Skip(skip)
            .Take(take)
            .ToList());

    void ILoyaltyAccountRepository.Add(LoyaltyAccount account) => Accounts.Add(account);

    Task<Reward?> IRewardRepository.FindAsync(TenantId tenantId, RewardId id, CancellationToken cancellationToken) =>
        Task.FromResult<Reward?>(Rewards.SingleOrDefault(reward => reward.TenantId == tenantId && reward.Id == id));

    Task<IReadOnlyList<Reward>> IRewardRepository.ListAsync(TenantId tenantId, bool activeOnly, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Reward>>(Rewards
            .Where(reward => reward.TenantId == tenantId && (!activeOnly || reward.Status == RewardStatus.Active))
            .OrderBy(reward => reward.PointCost.Value)
            .ToList());

    void IRewardRepository.Add(Reward reward) => Rewards.Add(reward);

    Task<bool> ILoyaltyLedgerLookup.IsReferencePostedAsync(
        TenantId tenantId, LoyaltyAccountId accountId, LoyaltyTransactionType type, TransactionReference reference, CancellationToken cancellationToken) =>
        Task.FromResult(Ledger.Any(entry =>
            entry.TenantId == tenantId && entry.AccountId == accountId && entry.Type == type && reference.Equals(entry.Reference)));

    Task<PostedTransaction?> ILoyaltyLedgerLookup.FindPostedAsync(
        TenantId tenantId, LoyaltyAccountId accountId, LoyaltyTransactionType type, TransactionReference reference, CancellationToken cancellationToken)
    {
        var original = Ledger.FirstOrDefault(entry =>
            entry.TenantId == tenantId && entry.AccountId == accountId && entry.Type == type && reference.Equals(entry.Reference));
        if (original is null)
        {
            return Task.FromResult<PostedTransaction?>(null);
        }

        var originalId = original.Id;
        return Task.FromResult<PostedTransaction?>(original.ToPostedReference(Ledger.Any(entry => entry.ReversedTransactionId == originalId)));
    }

    Task<bool> ILoyaltyAccountUniquenessChecker.IsUniqueAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken) =>
        Task.FromResult(!Accounts.Any(account => account.TenantId == tenantId && account.CustomerId == customerId));

    Task<LoyaltyProgram> ILoyaltyProgramStore.GetAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Programs.GetValueOrDefault(tenantId) ?? LoyaltyProgram.Default);

    Task ILoyaltyProgramStore.SetAsync(TenantId tenantId, LoyaltyProgram program, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken)
    {
        Programs[tenantId] = program;
        return Task.CompletedTask;
    }

    Task<IReadOnlyList<LoyaltyTransaction>> ILoyaltyHistory.ListAsync(
        TenantId tenantId, LoyaltyAccountId accountId, int skip, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LoyaltyTransaction>>(Ledger
            .Where(entry => entry.TenantId == tenantId && entry.AccountId == accountId)
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .Skip(skip)
            .Take(take)
            .ToList());

    Task<IReadOnlyList<ExpiryCandidate>> ILoyaltyExpiryCandidates.ListAsync(
        TenantId tenantId, DateTimeOffset cutoffUtc, int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExpiryCandidate>>(Accounts
            .Where(account => account.TenantId == tenantId && account.Status != LoyaltyAccountStatus.Closed && account.Balance > 0)
            .Select(account => new ExpiryCandidate(account.Id, account.Transactions
                .Where(entry => entry.Type is LoyaltyTransactionType.Earn or LoyaltyTransactionType.Redeem)
                .Select(entry => (DateTimeOffset?)entry.OccurredAtUtc)
                .Max() ?? account.OpenedAtUtc))
            .Where(candidate => candidate.LastActivityUtc < cutoffUtc)
            .OrderBy(candidate => candidate.LastActivityUtc)
            .Take(take)
            .ToList());

    Task<CustomerReference?> ILoyaltyCustomers.FindAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken) =>
        Task.FromResult<CustomerReference?>(Customers.GetValueOrDefault((tenantId, customerId)));

    Task<LoyaltyOrder?> ILoyaltyOrders.FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult<LoyaltyOrder?>(Orders.GetValueOrDefault((tenantId, orderId)));
}
