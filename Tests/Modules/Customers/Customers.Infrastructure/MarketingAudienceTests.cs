using Customers.Application.Abstractions;
using Customers.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Customers.Infrastructure;

/// <summary>Audience d'une campagne marketing, calculée en base : consentement accordé, adresse, tag, tenant.</summary>
public sealed class MarketingAudienceTests(CustomersDatabase database) : IClassFixture<CustomersDatabase>
{
    private static readonly ConsentSource Pos = ConsentSource.Create("pos");

    private FakeClock Clock => database.Clock;

    private async Task<Customer> GivenAsync(TenantId tenant, string? email, string? phone, CommunicationChannel? consent, bool withdrawn = false, string? tag = null)
    {
        var customer = Customer.Create(tenant, "Client " + Guid.NewGuid().ToString("N")[..6], email is null ? null : Email.Create(email), phone is null ? null : PhoneNumber.Create(phone), Clock);
        if (tag is not null)
        {
            customer.AddTag(CustomerTag.Create(tag));
        }

        await using var scope = database.NewScope(tenant);
        scope.ServiceProvider.GetRequiredService<ICustomerRepository>().Add(customer);
        if (consent is { } channel)
        {
            var granted = Consent.Grant(customer.ToReference(), channel, Pos, isUniqueForCustomerAndChannel: true, Clock);
            if (withdrawn)
            {
                granted.Withdraw(Pos, WithdrawalReason.CustomerRequest, Clock);
            }

            scope.ServiceProvider.GetRequiredService<IConsentRepository>().Add(granted);
        }

        await scope.ServiceProvider.GetRequiredService<ICustomersUnitOfWork>().SaveChangesAsync();
        return customer;
    }

    [Fact]
    public async Task Only_customers_with_a_granted_consent_and_an_address_are_in_the_audience()
    {
        var tenant = TenantId.New();
        var vip = await GivenAsync(tenant, "vip@example.com", null, CommunicationChannel.Email, tag: "vip");
        var regular = await GivenAsync(tenant, "habitue@example.com", "+22997000001", CommunicationChannel.Email);
        await GivenAsync(tenant, "retire@example.com", null, CommunicationChannel.Email, withdrawn: true);
        await GivenAsync(tenant, "sms@example.com", null, CommunicationChannel.Sms);
        await GivenAsync(tenant, "rien@example.com", null, consent: null);
        await GivenAsync(TenantId.New(), "autre@example.com", null, CommunicationChannel.Email);

        await using var scope = database.NewScope(tenant);
        var audience = scope.ServiceProvider.GetRequiredService<IMarketingAudienceReader>();
        var all = await audience.ListAsync(tenant, CommunicationChannel.Email, null, 0, 10);
        var tagged = await audience.ListAsync(tenant, CommunicationChannel.Email, CustomerTag.Create("VIP"), 0, 10);
        var sms = await audience.CountAsync(tenant, CommunicationChannel.Sms, null);
        var paged = await audience.ListAsync(tenant, CommunicationChannel.Email, null, 1, 10);

        Assert.Equal(new[] { vip.Id, regular.Id }.OrderBy(id => id.Value), all);
        Assert.Equal([vip.Id], tagged);
        Assert.Equal(0, sms);
        Assert.Single(paged);
    }
}
