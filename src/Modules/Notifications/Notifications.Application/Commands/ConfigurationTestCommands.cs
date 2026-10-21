using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Application.Commands;

/// <summary>
/// Le restaurant envoie un message de test, au contenu fixe, par une de SES configurations (même inactive) à l'adresse
/// ou au numéro qu'il choisit. Rien n'est enregistré dans l'historique ni compté dans le quota. La réponse ne contient
/// ni identifiant ni message brut du prestataire : seulement le résultat et le code d'échec stable.
/// </summary>
public sealed record TestNotificationConfigurationCommand(Guid ConfigurationId, string? Address) : ICommand<NotificationConfigurationTestDto>;

/// <param name="Outcome"><c>sent</c>, <c>delivered</c>, <c>failed</c>, <c>credentials_missing</c> ou <c>credentials_invalid</c>.</param>
/// <param name="FailureCode">Code stable de l'échec traduit par le prestataire (ex. <c>PROVIDER_AUTH_FAILED</c>).</param>
public sealed record NotificationConfigurationTestDto(string Outcome, string? FailureCode, bool IsPermanent, DateTimeOffset TestedAtUtc)
{
    public bool Succeeded => Outcome is NotificationConfigurationTestOutcomes.Sent or NotificationConfigurationTestOutcomes.Delivered;
}

public static class NotificationConfigurationTestOutcomes
{
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Failed = "failed";
    public const string CredentialsMissing = "credentials_missing";
    public const string CredentialsInvalid = "credentials_invalid";
}

public sealed class TestNotificationConfigurationHandler(NotificationsWorkspace workspace, INotificationSenderResolver resolver, IClock clock)
    : ICommandHandler<TestNotificationConfigurationCommand, NotificationConfigurationTestDto>
{
    public const string TestType = "configuration.test";
    public const string Subject = "Message de test";
    public const string Body =
        "Bonjour,\n\nCe message de test a été envoyé depuis la console de votre restaurant : ce canal de communication est bien configuré.\n";

    public async Task<NotificationConfigurationTestDto> HandleAsync(TestNotificationConfigurationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var configuration = await workspace.LoadConfigurationAsync(command.ConfigurationId, cancellationToken);
        var channel = configuration.Channel;
        DomainException.ThrowIf(
            channel is not (NotificationChannel.Email or NotificationChannel.Sms or NotificationChannel.WhatsApp),
            NotificationsApplicationErrors.TestNotSupported);
        var recipient = NotificationRecipient.Create(channel, command.Address?.Trim());

        if (configuration.Status != NotificationConfigurationStatus.Configured)
        {
            return Result(NotificationConfigurationTestOutcomes.CredentialsMissing);
        }

        INotificationSender? sender;
        try
        {
            sender = await resolver.ResolveAsync(configuration.Provider, channel, configuration, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return Result(NotificationConfigurationTestOutcomes.CredentialsInvalid);
        }

        if (sender is null)
        {
            return Result(NotificationConfigurationTestOutcomes.CredentialsMissing);
        }

        var request = new NotificationSendRequest(
            NotificationId.New(),
            NotificationDeliveryId.New(),
            workspace.CurrentTenant,
            TestType,
            NotificationPriority.High,
            recipient,
            NotificationContent.Create(channel, channel == NotificationChannel.Email ? Subject : null, Body));

        NotificationSendResult sent;
        try
        {
            sent = await sender.SendAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException or IOException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Result(NotificationConfigurationTestOutcomes.Failed, NotificationFailure.ProviderUnavailable);
        }

        return sent switch
        {
            { IsDelivered: true } => Result(NotificationConfigurationTestOutcomes.Delivered),
            { IsAccepted: true } => Result(NotificationConfigurationTestOutcomes.Sent),
            _ => Result(NotificationConfigurationTestOutcomes.Failed, sent.Failure?.Code, sent.Failure?.IsPermanent ?? false),
        };
    }

    private NotificationConfigurationTestDto Result(string outcome, string? failureCode = null, bool isPermanent = false) =>
        new(outcome, failureCode, isPermanent, clock.UtcNow);
}
