using SaasRestaurant.BuildingBlocks.Infrastructure;
using Staff.Domain;

namespace Staff.Infrastructure.Persistence;

/// <summary>
/// Base de données du module Staff : son propre schéma. Le query filter de tenant protège les requêtes ad hoc ; les
/// repositories précisent toujours leur tenant explicitement car Staff est aussi lu hors d'une requête de tenant
/// (sélection du restaurant, onboarding, « mes restaurants »).
/// </summary>
public sealed class StaffDbContext(DbContextOptions<StaffDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    public DbSet<StaffMember> Members => Set<StaffMember>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Till> Tills => Set<Till>();

    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();

    public DbSet<ScheduleWeek> ScheduleWeeks => Set<ScheduleWeek>();

    public DbSet<ScheduleSettings> ScheduleSettings => Set<ScheduleSettings>();

    public DbSet<TimeOffRequest> TimeOffRequests => Set<TimeOffRequest>();

    public DbSet<ShiftSwapRequest> ShiftSwapRequests => Set<ShiftSwapRequest>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DependencyInjection.Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StaffDbContext).Assembly);
        modelBuilder.ApplyOutbox();
        ApplyTenantQueryFilters(modelBuilder);
    }
}
