using Identity.Application.Abstractions;

namespace Identity.Infrastructure.Persistence;

internal sealed class UserTraces(IdentityDbContext context) : IUserTraces
{
    public async Task ForgetAsync(UserId userId, Email previousEmail, CancellationToken cancellationToken = default)
    {
        await context.PasswordAttempts
            .Where(attempt => attempt.Email == previousEmail.Value)
            .ExecuteDeleteAsync(cancellationToken);
        await context.UserAvatars.Where(avatar => avatar.UserId == userId).ExecuteDeleteAsync(cancellationToken);

        // Chaque événement de l'utilisateur écrit jusqu'ici peut porter son adresse. Seul le fait de l'effacement reste.
        var id = userId.Value.ToString();
        await context.Database.ExecuteSqlAsync(
            $"""
            DELETE FROM identity.outbox_messages
            WHERE type <> 'UserErased' AND content ->> 'UserId' = {id}
            """,
            cancellationToken);
    }
}
