using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SaasRestaurant.BuildingBlocks.Infrastructure;
using Tenants.Application.Abstractions;
using Tenants.Application.Commands;
using Tenants.Application.Queries;
using Tenants.Domain.Tenants;
using Tenants.Infrastructure.Persistence;

namespace Tenants.Infrastructure;

public sealed class TenantPersistenceTests(TenantsDatabase database) : IClassFixture<TenantsDatabase>
{
    private readonly FakeClock _clock = new();

    [Fact]
    public async Task A_saved_tenant_is_read_back_with_all_its_state()
    {
        var tenant = NewTenant(Branding.Create("Chez Hector", new Uri("https://cdn.example.com/logo.svg#dark"), "#abc"));
        tenant.ChangePriceTaxMode(false, _clock);
        _clock.Advance(TimeSpan.FromDays(3));
        tenant.Suspend(TenantSuspensionReason.PaymentOverdue, _clock);
        await SaveNewAsync(tenant);

        var loaded = await LoadAsync(tenant.Id);

        Assert.NotNull(loaded);
        Assert.Equal(tenant.Id, loaded.Id);
        Assert.Equal("Chez Hector SARL", loaded.Name.Value);
        Assert.Equal(TenantStatus.Suspended, loaded.Status);
        Assert.Equal(Currency.XOF, loaded.DefaultCurrency);
        Assert.Equal("Africa/Porto-Novo", loaded.TimeZone.Value);
        Assert.Equal(tenant.Branding, loaded.Branding);
        Assert.Equal("https://cdn.example.com/logo.svg#dark", loaded.Branding.LogoUrl!.AbsoluteUri);
        Assert.Equal("#AABBCC", loaded.Branding.PrimaryColor);
        Assert.False(loaded.PricesIncludeTax);
        Assert.Equal(FakeClock.DefaultNow, loaded.CreatedAtUtc);
        Assert.Equal(TenantSuspensionReason.PaymentOverdue, loaded.SuspensionReason);
        Assert.Equal(FakeClock.DefaultNow.AddDays(3), loaded.SuspendedAtUtc);
        Assert.Null(loaded.ClosedAtUtc);
        Assert.Empty(loaded.DomainEvents);
    }

    [Fact]
    public async Task An_unknown_tenant_is_not_found()
    {
        Assert.Null(await LoadAsync(TenantId.New()));
    }

    [Fact]
    public async Task Changes_to_a_loaded_tenant_are_saved_including_cleared_values()
    {
        var tenant = NewTenant(Branding.Create("Chez Hector", new Uri("https://cdn.example.com/logo.png"), "#112233"));
        tenant.Suspend(TenantSuspensionReason.Other, _clock);
        await SaveNewAsync(tenant);

        await using (var scope = database.NewScope())
        {
            var loaded = await Repository(scope).FindAsync(tenant.Id);
            loaded!.Reactivate(_clock);
            loaded.UpdateBranding(Branding.Create("Hector & Fils"), _clock);
            await UnitOfWork(scope).SaveChangesAsync();
        }

        var reloaded = await LoadAsync(tenant.Id);
        Assert.Equal(TenantStatus.Active, reloaded!.Status);
        Assert.Null(reloaded.SuspensionReason);
        Assert.Null(reloaded.SuspendedAtUtc);
        Assert.Equal("Hector & Fils", reloaded.Branding.DisplayName);
        Assert.Null(reloaded.Branding.LogoUrl);
        Assert.Null(reloaded.Branding.PrimaryColor);
    }

    [Fact]
    public async Task Saving_stores_the_domain_events_in_the_outbox_and_clears_them()
    {
        var tenant = NewTenant();
        _clock.Advance(TimeSpan.FromMinutes(5));
        tenant.Suspend(TenantSuspensionReason.TrialExpired, _clock);

        await SaveNewAsync(tenant);

        Assert.Empty(tenant.DomainEvents);
        var messages = await OutboxOfAsync(tenant.Id);
        Assert.Equal(["TenantCreated", "TenantSuspended"], messages.Select(message => message.Type));
        Assert.All(messages, message => Assert.Null(message.ProcessedAtUtc));

        var suspended = messages[1];
        Assert.Equal(FakeClock.DefaultNow.AddMinutes(5), suspended.OccurredAtUtc);
        using var content = JsonDocument.Parse(suspended.Content);
        Assert.Equal(tenant.Id.Value, content.RootElement.GetProperty("TenantId").GetGuid());
        Assert.Equal("TrialExpired", content.RootElement.GetProperty("Reason").GetString());
        Assert.Equal("Trial", content.RootElement.GetProperty("PreviousStatus").GetString());
    }

    [Fact]
    public async Task A_logo_url_typed_with_a_control_character_is_stored_escaped_in_the_row_and_in_the_outbox()
    {
        var tenant = NewTenant();
        tenant.UpdateBranding(Branding.Create("Chez Hector", new Uri("https://cdn.example.com/a\0b.png")), _clock);

        await SaveNewAsync(tenant);

        Assert.Equal("https://cdn.example.com/a%00b.png", (await LoadAsync(tenant.Id))!.Branding.LogoUrl!.AbsoluteUri);
        var updated = (await OutboxOfAsync(tenant.Id)).Single(message => message.Type == "TenantBrandingUpdated");
        using var content = JsonDocument.Parse(updated.Content);
        Assert.Equal(
            "https://cdn.example.com/a%00b.png",
            content.RootElement.GetProperty("Branding").GetProperty("LogoUrl").GetString());
    }

    [Fact]
    public async Task A_save_without_any_change_writes_nothing_to_the_outbox()
    {
        var tenant = NewTenant();
        await SaveNewAsync(tenant);

        await using (var scope = database.NewScope())
        {
            var loaded = await Repository(scope).FindAsync(tenant.Id);
            loaded!.Rename(TenantName.Create("Chez Hector SARL"), _clock);
            await UnitOfWork(scope).SaveChangesAsync();
        }

        Assert.Single(await OutboxOfAsync(tenant.Id));
    }

    [Fact]
    public async Task Two_concurrent_changes_of_the_same_tenant_the_second_one_is_refused()
    {
        var tenant = NewTenant();
        await SaveNewAsync(tenant);

        await using var first = database.NewScope();
        await using var second = database.NewScope();
        var firstCopy = await Repository(first).FindAsync(tenant.Id);
        var secondCopy = await Repository(second).FindAsync(tenant.Id);

        firstCopy!.Activate(_clock);
        await UnitOfWork(first).SaveChangesAsync();
        secondCopy!.Close(_clock);
        var exception = await Assert.ThrowsAsync<ConcurrencyException>(() => UnitOfWork(second).SaveChangesAsync());

        Assert.Equal("tenants.tenant.concurrent_update", exception.Code);
        Assert.Equal(TenantStatus.Active, (await LoadAsync(tenant.Id))!.Status);
        // Rien de la modification refusée n'a été validé, et son événement est toujours sur l'agrégat.
        Assert.Equal(["TenantCreated", "TenantActivated"], (await OutboxOfAsync(tenant.Id)).Select(message => message.Type));
        Assert.IsType<TenantClosed>(Assert.Single(secondCopy.DomainEvents));
    }

    [Fact]
    public async Task The_application_handlers_work_on_the_real_ports()
    {
        Guid id;
        await using (var scope = database.NewScope())
        {
            id = await new CreateTenantHandler(Repository(scope), UnitOfWork(scope), _clock)
                .HandleAsync(new CreateTenantCommand("Le Maquis", "EUR", "Europe/Paris", PricesIncludeTax: false));
        }

        await using (var scope = database.NewScope())
        {
            await new RenameTenantHandler(Repository(scope), UnitOfWork(scope), _clock)
                .HandleAsync(new RenameTenantCommand(id, "Le Grand Maquis"));
        }

        await using (var scope = database.NewScope())
        {
            var dto = await new GetTenantByIdHandler(Repository(scope)).HandleAsync(new GetTenantByIdQuery(id));

            Assert.Equal("Le Grand Maquis", dto.Name);
            Assert.Equal("EUR", dto.DefaultCurrencyCode);
            Assert.Equal("Europe/Paris", dto.TimeZoneId);
            Assert.False(dto.PricesIncludeTax);
            Assert.Equal("Le Maquis", dto.Branding.DisplayName);
        }

        Assert.Equal(
            ["TenantCreated", "TenantPriceTaxModeChanged", "TenantRenamed"],
            (await OutboxOfAsync(TenantId.From(id))).Select(message => message.Type));
    }

    private Tenant NewTenant(Branding? branding = null) => Tenant.Create(
        TenantName.Create("Chez Hector SARL"), Currency.XOF, TimeZoneId.Create("Africa/Porto-Novo"), _clock, branding);

    private async Task SaveNewAsync(Tenant tenant)
    {
        await using var scope = database.NewScope();
        Repository(scope).Add(tenant);
        await UnitOfWork(scope).SaveChangesAsync();
    }

    private async Task<Tenant?> LoadAsync(TenantId id)
    {
        await using var scope = database.NewScope();
        return await Repository(scope).FindAsync(id);
    }

    private async Task<List<OutboxMessage>> OutboxOfAsync(TenantId id)
    {
        await using var scope = database.NewScope();
        var messages = await scope.ServiceProvider.GetRequiredService<TenantsDbContext>().OutboxMessages
            .AsNoTracking()
            .OrderBy(message => message.Id)
            .ToListAsync();
        // La base est partagée par la classe : ne garder que les messages de ce tenant.
        return messages.Where(message => message.Content.Contains(id.Value.ToString())).ToList();
    }

    private static ITenantRepository Repository(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ITenantRepository>();

    private static ITenantsUnitOfWork UnitOfWork(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ITenantsUnitOfWork>();

    [Fact]
    public async Task A_priced_catalog_is_recorded_once_per_tenant()
    {
        var tenant = TenantId.New();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var scope = database.NewScope();
            await scope.ServiceProvider.GetRequiredService<IPricedCatalogLedger>().RecordAsync(tenant);
        }

        await using var check = database.NewScope();
        var checker = check.ServiceProvider.GetRequiredService<ITenantPricedCatalogChecker>();
        Assert.True(await checker.HasPricedCatalogAsync(tenant));
        Assert.False(await checker.HasPricedCatalogAsync(TenantId.New()));
    }
}
