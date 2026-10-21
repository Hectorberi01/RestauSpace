using Identity.Domain.Users;

namespace Identity.Application.Abstractions;

public interface ISessionStatus
{
    Task<bool> IsActiveAsync(UserId userId, SessionId sessionId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}
