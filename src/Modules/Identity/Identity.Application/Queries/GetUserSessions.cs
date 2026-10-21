using Identity.Application.Abstractions;

namespace Identity.Application.Queries;

public sealed record GetUserSessionsQuery(Guid UserId) : IQuery<IReadOnlyList<SessionDto>>;

public sealed class GetUserSessionsHandler(IUserRepository users, IClock clock): IQueryHandler<GetUserSessionsQuery, IReadOnlyList<SessionDto>>
{
    public async Task<IReadOnlyList<SessionDto>> HandleAsync(GetUserSessionsQuery query, CancellationToken cancellationToken = default)
    {
        var user = await users.FindAsync(UserId.From(query.UserId), cancellationToken);
        if (user == null)
        {
            throw new NotFoundException(IdentityApplicationErrors.NotFound);
        }

        return user.ActiveSessionsAt(clock.UtcNow)
            .OrderByDescending(session => session.CreatedAtUtc)
            .Select(SessionDto.From)
            .ToList();
    }
}
