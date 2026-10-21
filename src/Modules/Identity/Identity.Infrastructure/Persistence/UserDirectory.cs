using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Infrastructure.Persistence;

internal sealed class UserDirectory(IdentityDbContext context) : IUserDirectory
{
    public async Task<IReadOnlyList<User>> ListAsync(
        string? emailContains, UserStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var users = emailContains is null
            ? context.Users.AsNoTracking()
            : context.Users
                // xmin (version de ligne) est une colonne système : « * » ne la renvoie pas, elle est demandée explicitement.
                .FromSql($"SELECT *, xmin FROM identity.users WHERE email ILIKE {"%" + Escape(emailContains) + "%"} ESCAPE '\\'")
                .AsNoTracking();
        if (status is { } wanted)
        {
            users = users.Where(user => user.Status == wanted);
        }

        return await users
            .OrderByDescending(user => user.CreatedAtUtc)
            .ThenBy(user => user.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
