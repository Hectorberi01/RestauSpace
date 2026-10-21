using Identity.Application.Abstractions;

namespace Identity.Application.Queries;

/// <summary>
/// Le compte utilisant cet email, s'il existe. Pour les autres modules agissant sur une personne par email (invitations
/// du personnel) : jamais exposé en HTTP, où il révélerait quelles adresses ont un compte.
/// </summary>
public sealed record FindUserIdByEmailQuery(string? Email) : IQuery<Guid?>;

public sealed class FindUserIdByEmailHandler(IUserRepository users) : IQueryHandler<FindUserIdByEmailQuery, Guid?>
{
    public async Task<Guid?> HandleAsync(FindUserIdByEmailQuery query, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByEmailAsync(Email.Create(query.Email), cancellationToken);
        return user?.Id.Value;
    }
}
