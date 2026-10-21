using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Queries;

public sealed record ListUsersQuery(string? Search, UserStatus? Status, int Skip, int Take) : IQuery<IReadOnlyList<UserDto>>
{
    public const int MaxTake = 100;

    public const int MaxSearchLength = 100;
}

public sealed class ListUsersHandler(IUserDirectory directory) : IQueryHandler<ListUsersQuery, IReadOnlyList<UserDto>>
{
    public async Task<IReadOnlyList<UserDto>> HandleAsync(ListUsersQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var search = query.Search?.Trim();
        DomainException.ThrowIf(
            query.Skip < 0 || query.Take is < 1 or > ListUsersQuery.MaxTake || search is { Length: > ListUsersQuery.MaxSearchLength },
            IdentityApplicationErrors.ListInvalid);
        var found = await directory.ListAsync(string.IsNullOrEmpty(search) ? null : search, query.Status, query.Skip, query.Take, cancellationToken);
        return found.Select(UserDto.From).ToList();
    }
}
