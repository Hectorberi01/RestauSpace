using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> FindAsync(UserId id, CancellationToken cancellationToken = default);
    Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default);
    Task<User?> FindBySessionTokenAsync(TokenHash tokenHash, CancellationToken cancellationToken = default);
    void Add(User user);
}
