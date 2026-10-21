using Customers.Application.Abstractions;
using Customers.Domain;
using Customers.Infrastructure.Persistence.Configurations;

namespace Customers.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte : deux verrous sur la même porte.

internal sealed class CustomerRepository(CustomersDbContext context) : ICustomerRepository
{
    public Task<Customer?> FindAsync(TenantId tenantId, CustomerId id, CancellationToken cancellationToken = default) =>
        context.Customers.SingleOrDefaultAsync(customer => customer.TenantId == tenantId && customer.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Customer>> SearchAsync(TenantId tenantId, CustomerSearch search, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(search);
        var query = context.Customers.AsNoTracking().Where(customer => customer.TenantId == tenantId);
        if (!search.IncludeAnonymized)
        {
            query = query.Where(customer => customer.Status == CustomerStatus.Active);
        }

        if (search.Name is { } name)
        {
            // Sur la copie en majuscules, motif échappé : « 50% » ou « a_b » cherchent ces caractères tels quels.
            var pattern = "%" + EscapeLike(name.ToUpperInvariant()) + "%";
            query = query.Where(customer =>
                EF.Functions.Like(EF.Property<string>(customer, CustomerConfiguration.NormalizedNameProperty), pattern, "\\"));
        }

        if (search.Email is { } email)
        {
            query = query.Where(customer => customer.Email == email);
        }

        if (search.Phone is { } phone)
        {
            query = query.Where(customer => customer.Phone == phone);
        }

        if (search.Tag is { } tag)
        {
            query = query.Where(customer => customer.Tags.Any(candidate => candidate.Value == tag.Value));
        }

        return await query
            .OrderBy(customer => EF.Property<string>(customer, CustomerConfiguration.NormalizedNameProperty))
            .ThenBy(customer => customer.Id)
            .Skip(search.Skip)
            .Take(search.Take)
            .ToListAsync(cancellationToken);
    }

    public void Add(Customer customer) => context.Customers.Add(customer);

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}

internal sealed class ConsentRepository(CustomersDbContext context) : IConsentRepository, IConsentUniquenessChecker
{
    public Task<Consent?> FindAsync(
        TenantId tenantId, CustomerId customerId, CommunicationChannel channel, CancellationToken cancellationToken = default) =>
        context.Consents.SingleOrDefaultAsync(
            consent => consent.TenantId == tenantId && consent.CustomerId == customerId && consent.Channel == channel, cancellationToken);

    public async Task<IReadOnlyList<Consent>> ListForCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
        await context.Consents
            .Where(consent => consent.TenantId == tenantId && consent.CustomerId == customerId)
            .OrderBy(consent => consent.Channel)
            .ToListAsync(cancellationToken);

    public void Add(Consent consent) => context.Consents.Add(consent);

    public async Task<bool> IsUniqueAsync(
        TenantId tenantId, CustomerId customerId, CommunicationChannel channel, CancellationToken cancellationToken = default) =>
        !await context.Consents.AnyAsync(
            consent => consent.TenantId == tenantId && consent.CustomerId == customerId && consent.Channel == channel, cancellationToken);
}

internal sealed class CustomerNoteRepository(CustomersDbContext context) : ICustomerNoteRepository
{
    public Task<CustomerNote?> FindAsync(TenantId tenantId, CustomerNoteId id, CancellationToken cancellationToken = default) =>
        context.Notes.SingleOrDefaultAsync(note => note.TenantId == tenantId && note.Id == id, cancellationToken);

    public async Task<IReadOnlyList<CustomerNote>> ListForCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
        await context.Notes
            .Where(note => note.TenantId == tenantId && note.CustomerId == customerId)
            .OrderByDescending(note => note.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public void Add(CustomerNote note) => context.Notes.Add(note);

    public void Remove(CustomerNote note) => context.Notes.Remove(note);
}

internal sealed class CustomerSettingsStore(CustomersDbContext context) : ICustomerSettingsStore
{
    public async Task<ContactRequirementPolicy> GetContactPolicyAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await FindAsync(tenantId, cancellationToken);
        return settings is null ? ContactRequirementPolicy.Default : ContactRequirementPolicy.Of(settings.ContactRequirement);
    }

    public async Task SetContactPolicyAsync(
        TenantId tenantId, ContactRequirementPolicy policy, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var settings = await FindAsync(tenantId, cancellationToken);
        if (settings is null)
        {
            context.Settings.Add(new CustomerSettings { TenantId = tenantId, ContactRequirement = policy.Requirement, UpdatedAtUtc = updatedAtUtc });
            return;
        }

        if (settings.ContactRequirement != policy.Requirement)
        {
            settings.ContactRequirement = policy.Requirement;
            settings.UpdatedAtUtc = updatedAtUtc;
        }
    }

    // Suivi : la même ligne sert à lire puis à modifier dans la requête.
    private Task<CustomerSettings?> FindAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        context.Settings.SingleOrDefaultAsync(settings => settings.TenantId == tenantId, cancellationToken);
}

internal sealed class MarketingAudienceReader(CustomersDbContext context) : IMarketingAudienceReader
{
    public Task<int> CountAsync(
        TenantId tenantId, CommunicationChannel channel, CustomerTag? tag, IReadOnlyCollection<CustomerId>? among = null, CancellationToken cancellationToken = default) =>
        Query(tenantId, channel, tag, among).CountAsync(cancellationToken);

    public async Task<IReadOnlyList<CustomerId>> ListAsync(
        TenantId tenantId, CommunicationChannel channel, CustomerTag? tag, int skip, int take, IReadOnlyCollection<CustomerId>? among = null,
        CancellationToken cancellationToken = default) =>
        await Query(tenantId, channel, tag, among)
            .OrderBy(customer => customer.Id)
            .Skip(skip)
            .Take(take)
            .Select(customer => customer.Id)
            .ToListAsync(cancellationToken);

    private IQueryable<Customer> Query(TenantId tenantId, CommunicationChannel channel, CustomerTag? tag, IReadOnlyCollection<CustomerId>? among)
    {
        var query = context.Customers.AsNoTracking()
            .Where(customer => customer.TenantId == tenantId && customer.Status == CustomerStatus.Active)
            .Where(customer => context.Consents.Any(consent => consent.TenantId == tenantId
                && consent.CustomerId == customer.Id
                && consent.Channel == channel
                && consent.Status == ConsentStatus.Granted));
        query = channel switch
        {
            CommunicationChannel.Email => query.Where(customer => customer.Email != null),
            CommunicationChannel.Sms or CommunicationChannel.WhatsApp => query.Where(customer => customer.Phone != null),
            _ => query,
        };
        if (tag is { } wanted)
        {
            query = query.Where(customer => customer.Tags.Any(candidate => candidate.Value == wanted.Value));
        }

        if (among is not null)
        {
            var ids = among.ToList();
            query = query.Where(customer => ids.Contains(customer.Id));
        }

        return query;
    }
}
