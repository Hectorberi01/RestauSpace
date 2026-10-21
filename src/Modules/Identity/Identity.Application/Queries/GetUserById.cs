using Identity.Application.Abstractions;

namespace Identity.Application.Queries;

public sealed record GetUserByIdQuery(Guid UserId) : IQuery<UserDto>;

public sealed class GetUserByIdHandler(IUserRepository users) : IQueryHandler<GetUserByIdQuery, UserDto>
{
    public async Task<UserDto> HandleAsync(GetUserByIdQuery query, CancellationToken cancellationToken = default)
    {
        var user = await users.FindAsync(UserId.From(query.UserId), cancellationToken);
        if (user == null)
        {
            throw new NotFoundException(IdentityApplicationErrors.NotFound);
        }

        return UserDto.From(user);
    }
}
