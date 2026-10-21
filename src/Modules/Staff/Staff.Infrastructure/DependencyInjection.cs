using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Infrastructure;
using Staff.Application.Abstractions;
using Staff.Domain;
using Staff.Infrastructure.Persistence;
using Staff.Infrastructure.Security;

namespace Staff.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Schéma PostgreSQL du module. Aucun autre module ne lit ses tables ni ne fait de jointure dessus.</summary>
    public const string Schema = "staff";

    /// <summary>
    /// Enregistre la persistance du module. L'hôte fournit <see cref="ITenantContext"/>, <c>IStaffTenants</c>,
    /// <c>ILocationDirectory</c> et <c>IUserAccounts</c>.
    /// </summary>
    public static IServiceCollection AddStaffInfrastructure(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.TryAddScoped<ITenantContext>(_ => new FixedTenantContext(null));
        services.AddDbContext<StaffDbContext>(options => options.UseStaffNpgsql(connectionString));
        services.AddScoped<IStaffMemberRepository, StaffMemberRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IStaffMemberUniquenessChecker, StaffMemberUniquenessChecker>();
        services.AddScoped<IRoleNameUniquenessChecker, RoleNameUniquenessChecker>();
        services.AddScoped<IStaffUnitOfWork, StaffUnitOfWork>();
        services.AddScoped<ITillRepository, TillRepository>();
        services.AddScoped<ITimeEntryRepository, TimeEntryRepository>();
        services.AddScoped<IScheduleRepository, ScheduleRepository>();
        services.AddScoped<ITimeOffRepository, TimeOffRepository>();
        services.AddScoped<IShiftSwapRepository, ShiftSwapRepository>();
        services.AddSingleton<IPinHasher, Pbkdf2PinHasher>();
        services.AddSingleton<ITillSecrets, RandomTillSecrets>();
        services.AddOutboxDispatcher<StaffDbContext>();
        return services;
    }

    internal static DbContextOptionsBuilder UseStaffNpgsql(this DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema));
}
