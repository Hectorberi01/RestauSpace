using Customers.Application.Abstractions;
using Customers.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Customers.Infrastructure;

/// <summary>Notes et fusion sur un vrai PostgreSQL : contraintes de la fiche fusionnée, événement dans l'outbox.</summary>
public sealed class CrmPersistenceTests(CustomersDatabase database) : IClassFixture<CustomersDatabase>
{
    private FakeClock Clock => database.Clock;

    private async Task SaveAsync(TenantId tenant, Action<IServiceProvider> change)
    {
        await using var scope = database.NewScope(tenant);
        change(scope.ServiceProvider);
        await scope.ServiceProvider.GetRequiredService<ICustomersUnitOfWork>().SaveChangesAsync();
    }

    [Fact]
    public async Task A_merge_keeps_the_link_and_moves_the_notes()
    {
        var tenant = TenantId.New();
        var target = Customer.Create(tenant, "Awa Dossou", Email.Create("awa@example.com"), null, Clock);
        var duplicate = Customer.Create(tenant, "Awa D.", null, PhoneNumber.Create("+22997000001"), Clock);
        duplicate.AddTag(CustomerTag.Create("terrasse"));
        var note = CustomerNote.Write(duplicate.ToReference(), "Vient le dimanche", UserId.New(), Clock);
        await SaveAsync(tenant, services =>
        {
            services.GetRequiredService<ICustomerRepository>().Add(target);
            services.GetRequiredService<ICustomerRepository>().Add(duplicate);
            services.GetRequiredService<ICustomerNoteRepository>().Add(note);
        });

        await using (var scope = database.NewScope(tenant))
        {
            var repository = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
            var notes = scope.ServiceProvider.GetRequiredService<ICustomerNoteRepository>();
            var loadedTarget = (await repository.FindAsync(tenant, target.Id))!;
            var loadedDuplicate = (await repository.FindAsync(tenant, duplicate.Id))!;
            loadedTarget.Absorb(loadedDuplicate, Clock);
            foreach (var moved in await notes.ListForCustomerAsync(tenant, duplicate.Id))
            {
                moved.MoveTo(loadedTarget.ToReference());
            }

            await scope.ServiceProvider.GetRequiredService<ICustomersUnitOfWork>().SaveChangesAsync();
        }

        await using var check = database.NewScope(tenant);
        var customers = check.ServiceProvider.GetRequiredService<ICustomerRepository>();
        var kept = (await customers.FindAsync(tenant, target.Id))!;
        var merged = (await customers.FindAsync(tenant, duplicate.Id))!;
        var search = await customers.SearchAsync(tenant, new CustomerSearch(Name: "awa"));
        var keptNotes = await check.ServiceProvider.GetRequiredService<ICustomerNoteRepository>().ListForCustomerAsync(tenant, target.Id);

        Assert.Equal("+22997000001", kept.Phone!.Value);
        Assert.Equal(["terrasse"], kept.Tags.Select(tag => tag.Value));
        Assert.Equal((CustomerStatus.Merged, target.Id), (merged.Status, merged.MergedIntoId!.Value));
        Assert.Equal([target.Id], search.Select(customer => customer.Id));
        Assert.Equal("Vient le dimanche", Assert.Single(keptNotes).Text);
    }
}
