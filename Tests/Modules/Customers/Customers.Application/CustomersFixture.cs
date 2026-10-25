using Customers.Application.Abstractions;
using Customers.Application.Commands;
using Customers.Application.Queries;
using Customers.Domain;

namespace Customers.Application;

/// <summary>
/// Ports en mémoire du module Customers. Comme les vrais, ils ne répondent jamais que dans le tenant qui leur est donné.
/// Le tenant de « la requête » est <see cref="CurrentTenantId"/>.
/// </summary>
public sealed class CustomersFixture
    : ITenantContext, ICustomersUnitOfWork, ICustomersTenants, ICustomerRepository, IConsentRepository, IConsentUniquenessChecker,
      ICustomerSettingsStore, IPlanFeatureGate
{
    public CustomersFixture()
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

    public List<Customer> Customers { get; } = [];

    public List<Consent> Consents { get; } = [];

    public List<CustomerNote> Notes { get; } = [];

    public Dictionary<TenantId, ContactRequirementPolicy> Policies { get; } = [];

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public CustomersWorkspace Workspace => new(this, this, this, this, this, this, this);

    /// <summary>Fonctionnalités refusées par le plan (faux gate) : vide = tout est permis.</summary>
    public HashSet<string> DeniedFeatures { get; } = [];

    public CreateCustomerHandler Create => new(Workspace, Clock);

    public RenameCustomerHandler Rename => new(Workspace);

    public UpdateCustomerContactHandler UpdateContact => new(Workspace, Clock);

    public ApplyContactPolicyHandler ApplyPolicy => new(Workspace, Clock);

    public AddCustomerTagHandler AddTag => new(Workspace);

    public RemoveCustomerTagHandler RemoveTag => new(Workspace);

    public UpdateCustomerPreferencesHandler UpdatePreferences => new(Workspace);

    public AnonymizeCustomerHandler Anonymize => new(Workspace, new NoteStore(this), Clock);

    public AddCustomerNoteHandler AddNote => new(Workspace, new NoteStore(this), Clock);

    public DeleteCustomerNoteHandler DeleteNote => new(Workspace, new NoteStore(this));

    public ListCustomerNotesHandler ListNotes => new(Workspace, new NoteStore(this));

    public MergeCustomersHandler Merge => new(Workspace, new NoteStore(this), this, Clock);

    public ExportCustomerDataHandler Export => new(Workspace, new NoteStore(this), Clock);

    public ImportCustomersHandler Import => new(Workspace, this, Clock);

    public GrantConsentHandler Grant => new(Workspace, this, Clock);

    public WithdrawConsentHandler Withdraw => new(Workspace, Clock);

    public SetContactPolicyHandler SetPolicy => new(Workspace, Clock);

    public GetCustomerByIdHandler GetById => new(Workspace);

    public SearchCustomersHandler Search => new(Workspace);

    public ListCustomerConsentsHandler ListConsents => new(Workspace);

    public GetContactPolicyHandler GetPolicy => new(Workspace);

    public GetCustomerReferenceHandler GetReference => new(Workspace);

    public GetMarketingPermissionHandler MarketingPermission => new(Workspace);

    public Customer CustomerById(Guid id) => Customers.Single(customer => customer.Id.Value == id);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    Task<bool> IPlanFeatureGate.AllowsAsync(TenantId tenantId, string feature, CancellationToken cancellationToken) =>
        Task.FromResult(!DeniedFeatures.Contains(feature));

    Task<bool> ICustomersTenants.AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Writable.TryGetValue(tenantId, out var writable) && writable);

    Task<Customer?> ICustomerRepository.FindAsync(TenantId tenantId, CustomerId id, CancellationToken cancellationToken) =>
        Task.FromResult(Customers.SingleOrDefault(customer => customer.TenantId == tenantId && customer.Id == id));

    Task<IReadOnlyList<Customer>> ICustomerRepository.SearchAsync(TenantId tenantId, CustomerSearch search, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Customer>>(Customers
            .Where(customer => customer.TenantId == tenantId && (search.IncludeAnonymized || customer.Status == CustomerStatus.Active))
            .Where(customer => search.Name is null || customer.Name.Contains(search.Name, StringComparison.OrdinalIgnoreCase))
            .Where(customer => search.Email is null || customer.Email == search.Email)
            .Where(customer => search.Phone is null || customer.Phone == search.Phone)
            .Where(customer => search.Tag is null || customer.Tags.Contains(search.Tag))
            .OrderBy(customer => customer.Name.ToUpperInvariant(), StringComparer.Ordinal)
            .ThenBy(customer => customer.Id.Value)
            .Skip(search.Skip)
            .Take(search.Take)
            .ToList());

    void ICustomerRepository.Add(Customer customer) => Customers.Add(customer);

    Task<Consent?> IConsentRepository.FindAsync(
        TenantId tenantId, CustomerId customerId, CommunicationChannel channel, CancellationToken cancellationToken) =>
        Task.FromResult(Consents.SingleOrDefault(consent =>
            consent.TenantId == tenantId && consent.CustomerId == customerId && consent.Channel == channel));

    Task<IReadOnlyList<Consent>> IConsentRepository.ListForCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Consent>>(Consents
            .Where(consent => consent.TenantId == tenantId && consent.CustomerId == customerId)
            .OrderBy(consent => consent.Channel)
            .ToList());

    void IConsentRepository.Add(Consent consent) => Consents.Add(consent);

    Task<bool> IConsentUniquenessChecker.IsUniqueAsync(
        TenantId tenantId, CustomerId customerId, CommunicationChannel channel, CancellationToken cancellationToken) =>
        Task.FromResult(!Consents.Any(consent => consent.TenantId == tenantId && consent.CustomerId == customerId && consent.Channel == channel));

    Task<ContactRequirementPolicy> ICustomerSettingsStore.GetContactPolicyAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(Policies.GetValueOrDefault(tenantId) ?? ContactRequirementPolicy.Default);

    Task ICustomerSettingsStore.SetContactPolicyAsync(
        TenantId tenantId, ContactRequirementPolicy policy, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken)
    {
        Policies[tenantId] = policy;
        return Task.CompletedTask;
    }

    private sealed class NoteStore(CustomersFixture fixture) : ICustomerNoteRepository
    {
        public Task<CustomerNote?> FindAsync(TenantId tenantId, CustomerNoteId id, CancellationToken cancellationToken = default) =>
            Task.FromResult(fixture.Notes.SingleOrDefault(note => note.TenantId == tenantId && note.Id == id));

        public Task<IReadOnlyList<CustomerNote>> ListForCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CustomerNote>>(fixture.Notes.Where(note => note.TenantId == tenantId && note.CustomerId == customerId).ToList());

        public void Add(CustomerNote note) => fixture.Notes.Add(note);

        public void Remove(CustomerNote note) => fixture.Notes.Remove(note);
    }
}
