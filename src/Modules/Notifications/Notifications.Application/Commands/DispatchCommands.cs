using Notifications.Application.Abstractions;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Application.Commands;

// Commandes du worker et des webhooks : exécutées dans un scope qui agit pour le tenant de la notification
// (BackgroundTenant), ou sans tenant pour une notification de la plateforme.

/// <summary>
/// Envoie une notification arrivée à échéance (§ 30). La tentative ouverte est SAUVEGARDÉE avant l'appel au prestataire :
/// deux workers ne peuvent pas envoyer la même notification (le second échoue sur la version de la ligne et abandonne).
/// </summary>
public sealed record DispatchNotificationCommand(Guid NotificationId) : ICommand;

public sealed class DispatchNotificationHandler(NotificationsWorkspace workspace, NotificationSending sending)
    : ICommandHandler<DispatchNotificationCommand>
{
    public async Task HandleAsync(DispatchNotificationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var notification = await workspace.Notifications.FindAsync(
            workspace.CurrentTenantOrPlatform, NotificationId.From(command.NotificationId), cancellationToken);
        if (notification is null)
        {
            return;
        }

        await sending.SendAsync(notification, workspace.SaveAsync, cancellationToken);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Une tentative restée en cours trop longtemps (worker arrêté pendant l'appel) : comptée en échec temporaire puis
/// relancée selon la politique. Le prestataire a PEUT-ÊTRE reçu le message : un doublon est possible, une perte non.
/// </summary>
public sealed record RecoverStuckNotificationCommand(Guid NotificationId) : ICommand;

public sealed class RecoverStuckNotificationHandler(NotificationsWorkspace workspace, NotificationsSettings settings, IClock clock)
    : ICommandHandler<RecoverStuckNotificationCommand>
{
    public const string ProcessingTimeout = "PROCESSING_TIMEOUT";

    public async Task HandleAsync(RecoverStuckNotificationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var notification = await workspace.Notifications.FindAsync(
            workspace.CurrentTenantOrPlatform, NotificationId.From(command.NotificationId), cancellationToken);
        if (notification is not { Status: NotificationStatus.Processing }
            || notification.Deliveries.Last().StartedAtUtc > clock.UtcNow - settings.ProcessingTimeout)
        {
            return;
        }

        notification.MarkAsFailed(NotificationFailure.Temporary(ProcessingTimeout), clock);
        if (notification.CanRetry)
        {
            notification.ScheduleRetry(clock);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Rétention (§ 31-33) : efface adresse, contenu et messages des prestataires d'une notification terminée.</summary>
public sealed record RedactNotificationCommand(Guid NotificationId) : ICommand;

public sealed class RedactNotificationHandler(NotificationsWorkspace workspace, IClock clock) : ICommandHandler<RedactNotificationCommand>
{
    public async Task HandleAsync(RedactNotificationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var notification = await workspace.Notifications.FindAsync(
            workspace.CurrentTenantOrPlatform, NotificationId.From(command.NotificationId), cancellationToken);
        if (notification is null || notification.IsRedacted)
        {
            return;
        }

        notification.Redact(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Ce qu'un webhook dit d'un message. Valeurs explicites et définitives.</summary>
public enum DeliveryReportKind
{
    Delivered = 1,
    PartiallyDelivered = 2,
    Failed = 3,
}

/// <summary>
/// Applique un rapport de remise d'un prestataire (webhook vérifié, § 29) à la tentative qui porte cet id de message.
/// Rejouer le même rapport ne change rien. Un échec temporaire est relancé selon la politique.
/// </summary>
public sealed record ApplyDeliveryReportCommand(
    Guid NotificationId, string Provider, string ProviderMessageId, DeliveryReportKind Kind, string? FailureCode = null, bool IsPermanent = false)
    : ICommand;

public sealed class ApplyDeliveryReportHandler(NotificationsWorkspace workspace, IClock clock) : ICommandHandler<ApplyDeliveryReportCommand>
{
    public const string ReportedFailure = "PROVIDER_REPORTED_FAILURE";

    public async Task HandleAsync(ApplyDeliveryReportCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var notification = await workspace.Notifications.FindAsync(
            workspace.CurrentTenantOrPlatform, NotificationId.From(command.NotificationId), cancellationToken)
            ?? throw new NotFoundException(NotificationsApplicationErrors.NotificationNotFound);
        var messageId = ProviderMessageId.Create(command.ProviderMessageId);

        switch (command.Kind)
        {
            case DeliveryReportKind.Delivered:
                notification.MarkAsDelivered(clock, messageId);
                NotificationsTelemetry.RecordDelivered(notification.ProviderCode, notification.Channel);
                break;
            case DeliveryReportKind.PartiallyDelivered:
                notification.MarkAsPartiallyDelivered(clock, messageId);
                break;
            case DeliveryReportKind.Failed when notification.Status is NotificationStatus.Processing or NotificationStatus.Sent:
                var failure = NotificationFailure.Create(
                    command.FailureCode ?? ReportedFailure,
                    command.IsPermanent ? NotificationFailureKind.Permanent : NotificationFailureKind.Temporary);
                notification.MarkAsFailed(failure, clock, messageId);
                if (notification.CanRetry)
                {
                    notification.ScheduleRetry(clock);
                }

                break;
            default:
                // Échec annoncé après une remise, ou rapport en double : sans effet.
                return;
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Le restaurant annule un envoi en attente, programmé ou en échec (jamais pendant une tentative).</summary>
public sealed record CancelNotificationCommand(Guid NotificationId) : ICommand;

public sealed class CancelNotificationHandler(NotificationsWorkspace workspace, IClock clock) : ICommandHandler<CancelNotificationCommand>
{
    public async Task HandleAsync(CancelNotificationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var notification = await workspace.LoadNotificationAsync(workspace.CurrentTenant, command.NotificationId, cancellationToken);
        notification.Cancel(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Administration de la plateforme (« dead letter », § 26) : retry manuel d'une notification abandonnée, quel que soit
/// son tenant. L'abandon définitif est <see cref="AbandonDeadLetterCommand"/>.
/// </summary>
public sealed record RetryDeadLetterCommand(Guid NotificationId) : ICommand;

public sealed class RetryDeadLetterHandler(INotificationDirectory directory, INotificationsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<RetryDeadLetterCommand>
{
    public async Task HandleAsync(RetryDeadLetterCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var notification = (command.NotificationId == Guid.Empty ? null : await directory.LoadAnyAsync(NotificationId.From(command.NotificationId), cancellationToken))
            ?? throw new NotFoundException(NotificationsApplicationErrors.NotificationNotFound);
        notification.RetryManually(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Abandon définitif d'une notification en dead letter : annulée, puis expurgée.</summary>
public sealed record AbandonDeadLetterCommand(Guid NotificationId) : ICommand;

public sealed class AbandonDeadLetterHandler(INotificationDirectory directory, INotificationsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<AbandonDeadLetterCommand>
{
    public async Task HandleAsync(AbandonDeadLetterCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var notification = (command.NotificationId == Guid.Empty ? null : await directory.LoadAnyAsync(NotificationId.From(command.NotificationId), cancellationToken))
            ?? throw new NotFoundException(NotificationsApplicationErrors.NotificationNotFound);
        DomainException.ThrowIf(!notification.IsDeliveryAbandoned, NotificationErrors.NotAbandoned);
        notification.Cancel(clock);
        notification.Redact(clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
