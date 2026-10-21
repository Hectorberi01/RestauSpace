using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;
using Staff.Application;
using Staff.Application.Abstractions;
using Staff.Domain;
using Staff.Infrastructure.Persistence.Configurations;

namespace Staff.Infrastructure.Persistence;

internal sealed class StaffUnitOfWork(StaffDbContext context) : OutboxUnitOfWork<StaffDbContext>(context), IStaffUnitOfWork
{
    protected override DomainError ConcurrentUpdate => StaffApplicationErrors.ConcurrentUpdate;

    /// <summary>
    /// Une affectation ou une permission est une ligne à part : ne modifier que ces lignes réécrit quand même le membre
    /// ou le rôle, pour que sa version protège ce qui a été décidé d'après lui tel que chargé.
    /// </summary>
    protected override void BeforeSave()
    {
        var members = Context.ChangeTracker.Entries<StaffAssignment>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Deleted)
            .Select(entry => entry.Property<StaffMemberId>(StaffMemberConfiguration.OwnerProperty))
            .Select(owner => owner.EntityEntry.State == EntityState.Deleted ? owner.OriginalValue : owner.CurrentValue)
            .ToHashSet();
        foreach (var member in Context.ChangeTracker.Entries<StaffMember>())
        {
            if (member.State == EntityState.Unchanged && members.Contains(member.Entity.Id))
            {
                member.Property(candidate => candidate.InvitedAtUtc).IsModified = true;
            }
        }

        var roles = Context.ChangeTracker.Entries<PermissionCode>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Deleted)
            .Select(entry => entry.Property<RoleId>(RoleConfiguration.OwnerProperty))
            .Select(owner => owner.EntityEntry.State == EntityState.Deleted ? owner.OriginalValue : owner.CurrentValue)
            .ToHashSet();
        foreach (var role in Context.ChangeTracker.Entries<Role>())
        {
            if (role.State == EntityState.Unchanged && roles.Contains(role.Entity.Id))
            {
                role.Property(candidate => candidate.IsSystem).IsModified = true;
            }
        }
    }

    protected override Exception? Translate(DbUpdateException exception) =>
        (exception.InnerException as PostgresException) switch
        {
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: StaffMemberConfiguration.TenantUserIndex } =>
                new DomainException(StaffMemberErrors.UserAlreadyStaffInTenant),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: StaffMemberConfiguration.MemberLocationIndex } =>
                new DomainException(StaffMemberErrors.AlreadyAssignedToLocation),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: RoleConfiguration.TenantNameIndex } =>
                new DomainException(RoleErrors.NameAlreadyUsed),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: TimeEntryConfiguration.OpenEntryIndex } =>
                new DomainException(TimeEntryErrors.AlreadyOpen),
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ScheduleWeekConfiguration.WeekIndex } =>
                new DomainException(StaffApplicationErrors.ConcurrentUpdate),
            _ => null,
        };
}
