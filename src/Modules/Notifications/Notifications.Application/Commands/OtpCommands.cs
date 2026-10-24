using Notifications.Application.Abstractions;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Application.Commands;

/// <summary>Le code a été envoyé : l'id à présenter avec lui, et sa fin de validité. JAMAIS le code.</summary>
public sealed record OtpRequested(Guid ChallengeId, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Envoie un code à usage unique (§ 22) par email, SMS ou WhatsApp. Demandes successives limitées (délai minimal, nombre
/// par heure, par destinataire et usage, sur l'empreinte du destinataire) ; un nouveau code annule le précédent.
/// SÉCURITÉ : le code est envoyé IMMÉDIATEMENT puis le contenu de la notification est effacé AVANT toute sauvegarde ;
/// il n'est donc jamais écrit en base, dans l'outbox ni dans un journal. Seule son empreinte HMAC est gardée.
/// </summary>
/// <param name="Anonymous">Demandé par un visiteur du site : SMS et WhatsApp vers le pays du restaurant seulement, plafond horaire.</param>
public sealed record RequestOtpCommand(string Purpose, NotificationChannel Channel, string Address, string? Language = null, bool Anonymous = false)
    : ICommand<OtpRequested>;

public sealed class RequestOtpHandler(
    ITenantContext tenantContext,
    IOtpHasher hasher,
    IOtpChallengeRepository challenges,
    INotificationRepository notifications,
    NotificationPolicy policy,
    NotificationSending sending,
    INotificationsUnitOfWork unitOfWork,
    NotificationsSettings settings,
    IClock clock) : ICommandHandler<RequestOtpCommand, OtpRequested>
{
    public async Task<OtpRequested> HandleAsync(RequestOtpCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = tenantContext.CurrentTenantId;
        var purpose = OtpPurpose.Create(command.Purpose);
        var recipient = NotificationRecipient.Create(command.Channel, command.Address);
        var subject = hasher.HashSubject(recipient.Channel, recipient.Address);
        var now = clock.UtcNow;

        var recent = await challenges.ListRecentAsync(subject, purpose, now - TimeSpan.FromHours(1), cancellationToken);
        if (recent.Count >= settings.OtpMaxPerHour)
        {
            throw new TooManyRequestsException(NotificationsApplicationErrors.OtpTooManyRequests, recent.Min(c => c.CreatedAtUtc) + TimeSpan.FromHours(1) - now);
        }

        if (recent.Count > 0 && recent.Max(c => c.CreatedAtUtc) + settings.OtpCooldown > now)
        {
            throw new TooManyRequestsException(
                NotificationsApplicationErrors.OtpTooManyRequests, recent.Max(c => c.CreatedAtUtc) + settings.OtpCooldown - now);
        }

        foreach (var previous in recent)
        {
            previous.Cancel();
        }

        var code = hasher.NewCode(settings.OtpDigits);
        var challenge = OtpChallenge.Issue(
            tenant, purpose, recipient.Channel, subject, hasher.HashCode(subject, purpose, code), settings.OtpLifetime, settings.OtpMaxAttempts, clock);
        var payload = NotificationPayload.Create(new Dictionary<string, string>
        {
            ["otp.code"] = code,
            ["otp.expiresInMinutes"] = ((int)settings.OtpLifetime.TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
        var decision = await policy.DecideAsync(
            new NotificationIntent(
                tenant, NotificationTypes.SecurityOtp, recipient, null, null,
                NotificationLanguage.Create(command.Language ?? settings.DefaultLanguage), payload, Anonymous: command.Anonymous),
            cancellationToken);
        if (decision.Notification is not { } notification)
        {
            if (decision.Skipped == NotificationSkipReason.AnonymousPhoneLimitReached && tenant is { } owner)
            {
                throw new TooManyRequestsException(
                    NotificationsApplicationErrors.OtpPhoneLimitReached,
                    await policy.AnonymousRetryAfterAsync(owner, NotificationTypes.SecurityOtp, cancellationToken));
            }

            throw new DomainException(decision.Skipped == NotificationSkipReason.PhoneCountryNotAllowed
                ? NotificationsApplicationErrors.OtpPhoneCountryNotAllowed
                : NotificationsApplicationErrors.OtpNotSent);
        }

        // Envoi immédiat, SANS sauvegarde intermédiaire : le code n'existe qu'en mémoire.
        var result = await sending.SendAsync(notification, afterStart: null, cancellationToken);
        if (!notification.Status.IsHandedOver() && notification.Status != NotificationStatus.Cancelled)
        {
            notification.Cancel(clock);
        }

        notification.Redact(clock);
        challenge.AttachNotification(notification.Id);
        if (result is not { IsAccepted: true })
        {
            challenge.Cancel();
        }

        notifications.Add(notification);
        challenges.Add(challenge);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return result is { IsAccepted: true }
            ? new OtpRequested(challenge.Id.Value, challenge.ExpiresAtUtc)
            : throw new DomainException(NotificationsApplicationErrors.OtpNotSent);
    }
}

/// <summary>
/// Vérifie un code (§ 22). Chaque essai compte, même faux, et est SAUVEGARDÉ avant de répondre : un essai faux ne peut
/// pas être rejoué indéfiniment. Expiré, déjà utilisé, épuisé : refusé avec une erreur distincte (422).
/// </summary>
public sealed record VerifyOtpCommand(Guid ChallengeId, string Code) : ICommand;

public sealed class VerifyOtpHandler(
    ITenantContext tenantContext, IOtpHasher hasher, IOtpChallengeRepository challenges, INotificationsUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<VerifyOtpCommand>
{
    public async Task HandleAsync(VerifyOtpCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var challenge = (command.ChallengeId == Guid.Empty
                ? null
                : await challenges.FindAsync(tenantContext.CurrentTenantId, OtpChallengeId.From(command.ChallengeId), cancellationToken))
            ?? throw new NotFoundException(NotificationsApplicationErrors.OtpNotFound);

        var code = command.Code?.Trim() ?? string.Empty;
        var outcome = challenge.Verify(hasher.HashCode(challenge.SubjectKey, challenge.Purpose, code), clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var error = outcome switch
        {
            OtpVerification.Verified => null,
            OtpVerification.Expired => NotificationsApplicationErrors.OtpExpired,
            OtpVerification.AlreadyUsed => NotificationsApplicationErrors.OtpAlreadyUsed,
            OtpVerification.Exhausted => NotificationsApplicationErrors.OtpExhausted,
            _ => NotificationsApplicationErrors.OtpInvalid,
        };
        if (error is not null)
        {
            throw new DomainException(error);
        }
    }
}
