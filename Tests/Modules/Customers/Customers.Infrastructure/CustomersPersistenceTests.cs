using Customers.Application.Abstractions;
using Customers.Domain;
using Customers.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Customers.Infrastructure;

public sealed class CustomersPersistenceTests(CustomersDatabase database) : IClassFixture<CustomersDatabase>
{
    private FakeClock Clock => database.Clock;

    private async Task<T> InScopeAsync<T>(TenantId? tenantId, Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = database.NewScope(tenantId);
        return await work(scope.ServiceProvider);
    }

    private async Task SaveAsync(TenantId tenantId, Action<IServiceProvider> change)
    {
        await using var scope = database.NewScope(tenantId);
        change(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<ICustomersUnitOfWork>().SaveChangesAsync();
    }

    private async Task<Customer> GivenCustomerAsync(
        TenantId tenantId, string name = "Awa Dossou", string? email = "awa@example.com", string? phone = null, params string[] tags)
    {
        var customer = Customer.Create(
            tenantId, name, email is null ? null : Email.Create(email), phone is null ? null : PhoneNumber.Create(phone), Clock);
        foreach (var tag in tags)
        {
            customer.AddTag(CustomerTag.Create(tag));
        }

        await SaveAsync(tenantId, services => services.GetRequiredService<ICustomerRepository>().Add(customer));
        return customer;
    }

    private Task<Customer?> LoadAsync(TenantId tenantId, CustomerId id) =>
        InScopeAsync(tenantId, services => services.GetRequiredService<ICustomerRepository>().FindAsync(tenantId, id));

    private Task<IReadOnlyList<Customer>> SearchAsync(TenantId tenantId, CustomerSearch search) =>
        InScopeAsync(tenantId, services => services.GetRequiredService<ICustomerRepository>().SearchAsync(tenantId, search));

    [Fact]
    public async Task A_customer_comes_back_with_contact_tags_preferences_and_policy_and_its_events_in_the_outbox()
    {
        var tenant = TenantId.New();
        var customer = Customer.Create(
            tenant,
            "Awa Dossou",
            Email.Create("awa@example.com"),
            PhoneNumber.Create("+22997000000"),
            Clock,
            ContactRequirementPolicy.Of(ContactRequirement.Email));
        customer.AddTag(CustomerTag.Create("VIP"));
        customer.AddTag(CustomerTag.Create("terrasse"));
        customer.UpdatePreferences(CustomerPreference.Create("fr-BJ", [CommunicationChannel.Sms, CommunicationChannel.Email]));
        await SaveAsync(tenant, services => services.GetRequiredService<ICustomerRepository>().Add(customer));

        var loaded = await LoadAsync(tenant, customer.Id);
        var events = await InScopeAsync(null, services =>
            services.GetRequiredService<CustomersDbContext>().OutboxMessages.Select(message => message.Type).ToListAsync());

        Assert.NotNull(loaded);
        Assert.Equal((Email.Create("awa@example.com"), PhoneNumber.Create("+22997000000")), (loaded.Email!, loaded.Phone!));
        Assert.Equal(ContactRequirement.Email, loaded.ContactPolicy.Requirement);
        Assert.Equal(new[] { "terrasse", "vip" }, loaded.Tags.Select(tag => tag.Value).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(customer.Preferences, loaded.Preferences);
        Assert.Equal(customer.CreatedAtUtc, loaded.CreatedAtUtc);
        Assert.Contains(nameof(CustomerCreated), events);
        // Un autre restaurant ne la voit pas, même avec son id.
        var other = TenantId.New();
        Assert.Null(await LoadAsync(other, customer.Id));
        Assert.Null(await InScopeAsync(other, services => services.GetRequiredService<ICustomerRepository>().FindAsync(tenant, customer.Id)));
    }

    [Fact]
    public async Task Two_simultaneous_tag_changes_on_the_same_customer_cannot_both_win()
    {
        var tenant = TenantId.New();
        var customer = await GivenCustomerAsync(tenant);
        await using var first = database.NewScope(tenant);
        await using var second = database.NewScope(tenant);
        var mine = await first.ServiceProvider.GetRequiredService<ICustomerRepository>().FindAsync(tenant, customer.Id);
        var theirs = await second.ServiceProvider.GetRequiredService<ICustomerRepository>().FindAsync(tenant, customer.Id);

        mine!.AddTag(CustomerTag.Create("vip"));
        theirs!.AddTag(CustomerTag.Create("terrasse"));
        await first.ServiceProvider.GetRequiredService<ICustomersUnitOfWork>().SaveChangesAsync();

        // Seul le tag a changé : la fiche est réécrite quand même, pour que sa version protège le maximum de tags.
        await Assert.ThrowsAsync<ConcurrencyException>(() => second.ServiceProvider.GetRequiredService<ICustomersUnitOfWork>().SaveChangesAsync());
        Assert.Equal(new[] { "vip" }, (await LoadAsync(tenant, customer.Id))!.Tags.Select(tag => tag.Value).ToArray());
    }

    [Fact]
    public async Task Removing_a_tag_and_anonymizing_delete_the_tag_rows()
    {
        var tenant = TenantId.New();
        var customer = await GivenCustomerAsync(tenant, tags: ["vip", "terrasse", "anniversaire"]);

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<ICustomerRepository>().FindAsync(tenant, customer.Id);
            loaded!.RemoveTag(CustomerTag.Create("terrasse"));
            await scope.ServiceProvider.GetRequiredService<ICustomersUnitOfWork>().SaveChangesAsync();
        }

        Assert.Equal(2, (await LoadAsync(tenant, customer.Id))!.Tags.Count);

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<ICustomerRepository>().FindAsync(tenant, customer.Id);
            loaded!.Anonymize(Clock);
            await scope.ServiceProvider.GetRequiredService<ICustomersUnitOfWork>().SaveChangesAsync();
        }

        var erased = await LoadAsync(tenant, customer.Id);
        var rows = await InScopeAsync(null, services => services.GetRequiredService<CustomersDbContext>().Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM customers.customer_tags WHERE customer_id = {customer.Id.Value}")
            .SingleAsync());
        Assert.NotNull(erased);
        Assert.True(erased.IsAnonymized);
        Assert.Equal((Customer.AnonymizedName, (Email?)null, (PhoneNumber?)null, (CustomerPreference?)null), (erased.Name, erased.Email, erased.Phone, erased.Preferences));
        Assert.Equal(0, rows);
    }

    [Fact]
    public async Task Search_matches_part_of_the_name_whatever_the_case_and_treats_wildcards_literally()
    {
        var tenant = TenantId.New();
        var awa = await GivenCustomerAsync(tenant, "Awa Dossou", "awa@example.com", tags: ["vip"]);
        var kofi = await GivenCustomerAsync(tenant, "Kofi Mensah", null, "+22997000002");
        var percent = await GivenCustomerAsync(tenant, "Café 100% Bio", "cafe@example.com");
        await GivenCustomerAsync(TenantId.New(), "Awa Elsewhere", "awa@example.com");

        Assert.Equal(new[] { awa.Id }, (await SearchAsync(tenant, new CustomerSearch(Name: "DOSS"))).Select(customer => customer.Id).ToArray());
        Assert.Equal(new[] { percent.Id }, (await SearchAsync(tenant, new CustomerSearch(Name: "100%"))).Select(customer => customer.Id).ToArray());
        Assert.Empty(await SearchAsync(tenant, new CustomerSearch(Name: "a_a")));
        Assert.Equal(new[] { awa.Id }, (await SearchAsync(tenant, new CustomerSearch(Email: Email.Create("AWA@example.com")))).Select(customer => customer.Id).ToArray());
        Assert.Equal(new[] { kofi.Id }, (await SearchAsync(tenant, new CustomerSearch(Phone: PhoneNumber.Create("+22997000002")))).Select(customer => customer.Id).ToArray());
        Assert.Equal(new[] { awa.Id }, (await SearchAsync(tenant, new CustomerSearch(Tag: CustomerTag.Create("VIP")))).Select(customer => customer.Id).ToArray());
        // Par nom : Awa, Café, Kofi ; puis une page.
        Assert.Equal(
            new[] { awa.Id, percent.Id, kofi.Id },
            (await SearchAsync(tenant, new CustomerSearch())).Select(customer => customer.Id).ToArray());
        Assert.Equal(new[] { percent.Id }, (await SearchAsync(tenant, new CustomerSearch(Skip: 1, Take: 1))).Select(customer => customer.Id).ToArray());
    }

    [Fact]
    public async Task A_consent_comes_back_with_its_history_and_is_unique_per_customer_and_channel()
    {
        var tenant = TenantId.New();
        var customer = await GivenCustomerAsync(tenant);
        var consent = Consent.Grant(customer.ToReference(), CommunicationChannel.Email, ConsentSource.Create("pos"), true, Clock);
        await SaveAsync(tenant, services => services.GetRequiredService<IConsentRepository>().Add(consent));

        await using (var scope = database.NewScope(tenant))
        {
            var loaded = await scope.ServiceProvider.GetRequiredService<IConsentRepository>().FindAsync(tenant, customer.Id, CommunicationChannel.Email);
            Clock.Advance(TimeSpan.FromMinutes(1));
            loaded!.Withdraw(ConsentSource.Create("unsubscribe-link"), WithdrawalReason.UnsubscribeLink, Clock);
            Clock.Advance(TimeSpan.FromMinutes(1));
            loaded.Regrant(customer.ToReference(), ConsentSource.Create("web-signup-form"), Clock);
            await scope.ServiceProvider.GetRequiredService<ICustomersUnitOfWork>().SaveChangesAsync();
        }

        var reloaded = (await InScopeAsync(tenant, services =>
            services.GetRequiredService<IConsentRepository>().ListForCustomerAsync(tenant, customer.Id))).Single();
        var unique = await InScopeAsync(tenant, services =>
            services.GetRequiredService<IConsentUniquenessChecker>().IsUniqueAsync(tenant, customer.Id, CommunicationChannel.Email));
        // Le contrôle a été contourné (deux octrois simultanés) : l'index unique refuse le second.
        var duplicate = Consent.Grant(customer.ToReference(), CommunicationChannel.Email, ConsentSource.Create("pos"), true, Clock);
        var refused = await Assert.ThrowsAsync<DomainException>(() =>
            SaveAsync(tenant, services => services.GetRequiredService<IConsentRepository>().Add(duplicate)));

        Assert.True(reloaded.IsGranted);
        Assert.Equal("web-signup-form", reloaded.Source.Value);
        Assert.Null(reloaded.WithdrawalReason);
        Assert.Equal(
            new[] { (ConsentStatus.Granted, "pos"), (ConsentStatus.Withdrawn, "unsubscribe-link"), (ConsentStatus.Granted, "web-signup-form") },
            reloaded.History.OrderBy(change => change.OccurredAtUtc).Select(change => (change.Status, change.Source.Value)).ToArray());
        Assert.False(unique);
        Assert.Equal(ConsentErrors.NotUnique, refused.Error);
    }

    [Fact]
    public async Task The_contact_policy_of_a_restaurant_defaults_then_is_stored_once_per_restaurant()
    {
        var tenant = TenantId.New();

        async Task<ContactRequirement> PolicyAsync(TenantId tenantId) =>
            (await InScopeAsync(tenantId, services => services.GetRequiredService<ICustomerSettingsStore>().GetContactPolicyAsync(tenantId))).Requirement;

        async Task SetAsync(ContactRequirement requirement)
        {
            await using var scope = database.NewScope(tenant);
            await scope.ServiceProvider.GetRequiredService<ICustomerSettingsStore>()
                .SetContactPolicyAsync(tenant, ContactRequirementPolicy.Of(requirement), Clock.UtcNow);
            await scope.ServiceProvider.GetRequiredService<ICustomersUnitOfWork>().SaveChangesAsync();
        }

        Assert.Equal(ContactRequirement.EmailOrPhone, await PolicyAsync(tenant));
        await SetAsync(ContactRequirement.None);
        await SetAsync(ContactRequirement.Phone);

        Assert.Equal(ContactRequirement.Phone, await PolicyAsync(tenant));
        Assert.Equal(ContactRequirement.EmailOrPhone, await PolicyAsync(TenantId.New()));
    }

    [Fact]
    public async Task The_database_refuses_an_anonymized_row_that_still_holds_personal_data()
    {
        var tenant = TenantId.New();
        var customer = await GivenCustomerAsync(tenant);

        var refused = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => InScopeAsync(null, services =>
            services.GetRequiredService<CustomersDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE customers.customers SET status = 2, anonymized_at_utc = now() WHERE id = {customer.Id.Value}")));

        Assert.Equal(
            (Npgsql.PostgresErrorCodes.CheckViolation, "ck_customers_anonymized_has_no_personal_data"),
            (refused.SqlState, refused.ConstraintName));
    }
}
