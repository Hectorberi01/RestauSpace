namespace Notifications.Domain;

/// <summary>
/// Le message RENDU tel qu'il part chez le destinataire : sujet (Email, obligatoire) ou titre (Push, InApp, facultatif),
/// et corps. Produit par l'Application à partir du template retenu et des variables ; il ne contient plus aucune
/// variable (<c>{{...}}</c>) : un rendu incomplet ne part jamais. Effacé avec la notification (il peut citer le nom ou
/// le code du destinataire).
/// </summary>
public sealed class NotificationContent : ValueObject
{
    public const int MaxSubjectLength = NotificationTemplate.MaxSubjectLength;

    /// <summary>Un rendu peut dépasser un peu le template (valeurs des variables).</summary>
    public const int MaxBodyLength = 40_000;

    private NotificationContent(NotificationChannel channel, string? subject, string body, bool isHtml)
    {
        Channel = channel;
        Subject = subject;
        Body = body;
        IsHtml = isHtml;
    }

    /// <summary>Le canal pour lequel ce contenu a été validé : il doit être celui du destinataire.</summary>
    public NotificationChannel Channel { get; }

    public string? Subject { get; }

    public string Body { get; }

    /// <summary>Corps HTML (Email seulement), décidé d'après le MODÈLE, jamais d'après le contenu rendu.</summary>
    public bool IsHtml { get; }

    /// <summary>Vrai une fois effacé. Un tel contenu ne se construit pas hors du domaine (un corps est obligatoire).</summary>
    public bool IsRedacted => Body.Length == 0;

    /// <summary>Jamais le contenu lui-même : il peut citer un OTP ou des données personnelles.</summary>
    public override string ToString() => IsRedacted ? $"{Channel}:[redacted]" : $"{Channel}:[{Body.Length} chars]";

    public static NotificationContent Create(NotificationChannel channel, string? subject, string? body, bool isHtml = false)
    {
        NotificationChannels.Require(channel);
        string? validSubject = null;
        switch (channel)
        {
            case NotificationChannel.Email:
                DomainException.ThrowIf(string.IsNullOrWhiteSpace(subject), NotificationErrors.SubjectRequired);
                validSubject = subject!.Trim();
                break;
            case NotificationChannel.Push or NotificationChannel.InApp:
                validSubject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
                break;
            default:
                DomainException.ThrowIf(!string.IsNullOrWhiteSpace(subject), NotificationErrors.SubjectNotSupported);
                break;
        }

        DomainException.ThrowIf(validSubject is { Length: > MaxSubjectLength }, NotificationErrors.SubjectTooLong);
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(body), NotificationErrors.BodyEmpty);
        DomainException.ThrowIf(body!.Length > MaxBodyLength, NotificationErrors.BodyTooLong);
        DomainException.ThrowIf(
            IsUnrendered(validSubject) || IsUnrendered(body),
            NotificationErrors.ContentNotRendered);

        return new NotificationContent(channel, validSubject, body, isHtml && channel == NotificationChannel.Email);
    }

    internal static NotificationContent Redacted(NotificationChannel channel) => new(channel, null, string.Empty, false);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Channel;
        yield return Subject;
        yield return Body;
        yield return IsHtml;
    }

    private static bool IsUnrendered(string? text) =>
        text is not null && (text.Contains("{{", StringComparison.Ordinal) || text.Contains("}}", StringComparison.Ordinal));
}
