using System.Text.Json;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Notifications;

/// <summary>
/// Prévient le propriétaire quand le mot de passe ou l'email du compte change, à partir des événements de l'outbox :
/// l'avis part même si la requête à l'origine du changement est terminée depuis longtemps. Un changement d'email est
/// annoncé à l'ANCIENNE adresse, la seule qu'un pirate ne contrôle pas.
/// </summary>
internal sealed class SecurityNotificationConsumer(IdentityDbContext context, EmailQueue queue, IdentityEmailOptions? options = null)
    : IOutboxConsumer
{
    private const string ErasedDomain = "@erased.invalid";

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        if (message.Module != IdentityDbContext.Schema || message.Type is not ("PasswordChanged" or "UserEmailChanged"))
        {
            return;
        }

        var productName = options?.ProductName ?? new IdentityEmailOptions().ProductName;
        using var content = JsonDocument.Parse(message.Content);
        if (message.Type == "UserEmailChanged")
        {
            var previous = content.RootElement.GetProperty("PreviousEmail").GetString()!;
            var current = content.RootElement.GetProperty("NewEmail").GetString()!;
            var (subject, body) = IdentityEmails.EmailChanged(productName, current);
            queue.Enqueue(new EmailMessage(previous, subject, body));
            return;
        }

        var userId = UserId.From(content.RootElement.GetProperty("UserId").GetGuid());
        var email = await context.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => user.Email)
            .FirstOrDefaultAsync(cancellationToken);
        if (email is not null && !email.Value.EndsWith(ErasedDomain, StringComparison.Ordinal))
        {
            var (subject, body) = IdentityEmails.PasswordChanged(productName);
            queue.Enqueue(new EmailMessage(email.Value, subject, body));
        }
    }
}
