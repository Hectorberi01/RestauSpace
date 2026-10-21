using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Identity.Infrastructure.Persistence.Configurations;
using Npgsql;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Identity.Infrastructure.Persistence;

internal sealed class IdentityUnitOfWork(IdentityDbContext context)
    : OutboxUnitOfWork<IdentityDbContext>(context), IIdentityUnitOfWork
{
    protected override DomainError ConcurrentUpdate => IdentityApplicationErrors.ConcurrentUpdate;

    /// <summary>
    /// Une rotation, une révocation ou un nouveau lien envoyé par email n'écrit que des lignes de session ou de jeton.
    /// La ligne de l'utilisateur est touchée aussi, pour que sa version protège tout ce qui a été décidé d'après
    /// l'utilisateur tel que chargé : deux rotations concurrentes d'un même jeton ne peuvent pas réussir toutes les deux,
    /// et un lien émis pour un compte dont l'email a changé entre-temps est refusé.
    /// </summary>
    protected override void BeforeSave()
    {
        var owners = Context.ChangeTracker.Entries<RefreshSession>()
            .Where(session => session.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(session => session.Property<UserId>("UserId"))
            .Select(owner => owner.EntityEntry.State == EntityState.Deleted ? owner.OriginalValue : owner.CurrentValue)
            .ToHashSet();
        owners.UnionWith(Context.ChangeTracker.Entries<UserToken>()
            .Where(token => token.State is EntityState.Added or EntityState.Deleted)
            .Select(token => token.Entity.UserId));

        foreach (var user in Context.ChangeTracker.Entries<User>())
        {
            if (user.State == EntityState.Unchanged && owners.Contains(user.Entity.Id))
            {
                user.Property(candidate => candidate.CreatedAtUtc).IsModified = true;
            }
        }
    }

    protected override Exception? Translate(DbUpdateException exception) =>
        exception.InnerException switch
        {
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UserConfiguration.EmailIndex } =>
                new DomainException(UserErrors.EmailAlreadyUsed),
            // Deux requêtes ont émis le même type de lien en même temps : l'une d'elles a perdu.
            PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UserTokenConfiguration.UserPurposeIndex } =>
                new ConcurrencyException(ConcurrentUpdate, exception),
            _ => null,
        };
}
