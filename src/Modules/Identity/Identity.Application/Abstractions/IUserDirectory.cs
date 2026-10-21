using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

public interface IUserDirectory
{
    Task<IReadOnlyList<User>> ListAsync(string? emailContains, UserStatus? status, int skip, int take, CancellationToken cancellationToken = default);
}
