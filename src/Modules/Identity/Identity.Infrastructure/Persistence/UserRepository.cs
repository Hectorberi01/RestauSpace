using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Infrastructure.Persistence;

internal sealed class UserRepository(IdentityDbContext context) : IUserRepository
{
    // L'agrégat est toujours chargé en entier : ses règles doivent voir toutes les sessions.
    private IQueryable<User> Users => context.Users.Include(user => user.Sessions);

    public Task<User?> FindAsync(UserId id, CancellationToken cancellationToken = default) =>
        Users.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default) =>
        Users.FirstOrDefaultAsync(user => user.Email == email, cancellationToken);

    public Task<User?> FindBySessionTokenAsync(TokenHash tokenHash, CancellationToken cancellationToken = default) =>
        Users.FirstOrDefaultAsync(user => user.Sessions.Any(session => session.Hash == tokenHash), cancellationToken);

    public void Add(User user) => context.Users.Add(user);
}
