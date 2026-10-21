using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Notifications.Application.Abstractions;
using Notifications.Infrastructure.Persistence;

namespace Notifications.Infrastructure;

/// <summary>
/// Enregistrement et contexte de tenant du DbContext, sans base de données (aucune requête n'est exécutée). Le
/// mapping, les migrations et les filtres par tenant sur un vrai PostgreSQL sont testés à l'étape 11.
/// </summary>
public sealed class NotificationsDbContextTests
{
    private const string ConnectionString = "Host=localhost;Database=notifications_tests";

    private static ServiceProvider Services(ITenantContext? hostTenant = null)
    {
        var services = new ServiceCollection();
        if (hostTenant is { } tenant)
        {
            services.AddScoped<ITenantContext>(_ => tenant);
        }

        return services.AddNotificationsInfrastructure(ConnectionString).BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void The_module_owns_its_schema() => Assert.Equal("notifications", DependencyInjection.Schema);

    [Fact]
    public void The_context_and_the_unit_of_work_are_scoped_to_the_request()
    {
        using var services = Services();
        using var first = services.CreateScope();
        using var second = services.CreateScope();

        var context = first.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        Assert.Same(context, first.ServiceProvider.GetRequiredService<NotificationsDbContext>());
        Assert.NotSame(context, second.ServiceProvider.GetRequiredService<NotificationsDbContext>());
        Assert.NotNull(first.ServiceProvider.GetRequiredService<INotificationsUnitOfWork>());
        Assert.Equal(ConnectionString, context.Database.GetConnectionString());
    }

    [Fact]
    public void Without_a_host_tenant_context_nothing_acts_for_a_tenant()
    {
        using var services = Services();
        using var scope = services.CreateScope();

        Assert.Null(scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().CurrentTenant);
    }

    [Fact]
    public void The_tenant_context_of_the_host_is_kept_and_read_at_each_use()
    {
        var tenant = new MutableTenant();
        using var services = Services(tenant);
        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var first = TenantId.New();
        var second = TenantId.New();

        tenant.Current = first;
        var seenFirst = context.CurrentTenant;
        tenant.Current = second;

        Assert.Same(tenant, scope.ServiceProvider.GetRequiredService<ITenantContext>());
        Assert.Equal(first, seenFirst);
        Assert.Equal(second, context.CurrentTenant);
    }

    [Fact]
    public void A_connection_string_is_required()
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddNotificationsInfrastructure(" "));
        Assert.Throws<ArgumentNullException>(() => DependencyInjection.AddNotificationsInfrastructure(null!, ConnectionString));
    }

    [Fact]
    public void The_design_time_factory_reads_its_variable_and_acts_for_no_tenant()
    {
        var previous = Environment.GetEnvironmentVariable(NotificationsDbContextFactory.ConnectionStringVariable);
        try
        {
            Environment.SetEnvironmentVariable(NotificationsDbContextFactory.ConnectionStringVariable, "Host=db;Database=design");

            using var context = new NotificationsDbContextFactory().CreateDbContext([]);

            Assert.Equal("Host=db;Database=design", context.Database.GetConnectionString());
            Assert.Null(context.CurrentTenant);
        }
        finally
        {
            Environment.SetEnvironmentVariable(NotificationsDbContextFactory.ConnectionStringVariable, previous);
        }
    }

    private sealed class MutableTenant : ITenantContext
    {
        public TenantId? Current { get; set; }

        public TenantId? CurrentTenantId => Current;
    }
}
