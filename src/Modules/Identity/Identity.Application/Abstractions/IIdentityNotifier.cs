namespace Identity.Application.Abstractions;

public interface IIdentityNotifier
{
    Task SendEmailVerificationAsync(Email email, string token, CancellationToken cancellationToken = default);

    Task SendInvitationAsync(Email email, string token, CancellationToken cancellationToken = default);

    Task SendPasswordResetAsync(Email email, string token, CancellationToken cancellationToken = default);
}
