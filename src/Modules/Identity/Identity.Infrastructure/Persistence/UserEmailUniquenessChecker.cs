using Identity.Domain.Users;

namespace Identity.Infrastructure.Persistence;

internal sealed class UserEmailUniquenessChecker(IdentityDbContext context) : IUserEmailUniquenessChecker
{
    public async Task<bool> IsUniqueAsync(Email email, UserId? excludedUserId, CancellationToken cancellationToken = default)
    {
        var sameEmail = context.Users.Where(user => user.Email == email);
        if (excludedUserId is { } excluded)
        {
            sameEmail = sameEmail.Where(user => user.Id != excluded);
        }

        return !await sameEmail.AnyAsync(cancellationToken);
    }
}
