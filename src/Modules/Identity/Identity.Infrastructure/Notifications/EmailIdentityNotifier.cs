using Identity.Application.Abstractions;

namespace Identity.Infrastructure.Notifications;

internal sealed class EmailIdentityNotifier(EmailQueue queue, IdentityEmailOptions options) : IIdentityNotifier
{
    public Task SendEmailVerificationAsync(Email email, string token, CancellationToken cancellationToken = default) =>
        Enqueue(email, IdentityEmails.Verification(options, options.Link(options.VerifyEmailUrl, token)));

    public Task SendInvitationAsync(Email email, string token, CancellationToken cancellationToken = default) =>
        Enqueue(email, IdentityEmails.Invitation(options, options.Link(options.AcceptInvitationUrl, token)));

    public Task SendPasswordResetAsync(Email email, string token, CancellationToken cancellationToken = default) =>
        Enqueue(email, IdentityEmails.PasswordReset(options, options.Link(options.ResetPasswordUrl, token)));

    private Task Enqueue(Email to, (string Subject, string Body) content)
    {
        queue.Enqueue(new EmailMessage(to.Value, content.Subject, content.Body));
        return Task.CompletedTask;
    }
}
