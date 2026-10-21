using System.Text.RegularExpressions;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Notifications.Application.Sending;
using Notifications.Domain;

namespace Notifications.Infrastructure.Senders;

/// <summary>
/// Email par n'importe quel serveur SMTP (Brevo, Mailjet, OVH, Gmail...). Le compte fournit le serveur, le port, la
/// sécurité de connexion (<c>starttls</c>, <c>ssl</c> ou <c>none</c>), l'identifiant, le mot de passe et l'expéditeur.
/// SMTP ne renvoie aucun accusé de remise : un message accepté par le serveur reste « envoyé ».
/// </summary>
internal sealed partial class SmtpSenderFactory(ISmtpTransport transport) : INotificationSenderFactory
{
    public const string Code = "SMTP";
    public const string HostField = "host";
    public const string PortField = "port";
    public const string SecurityField = "security";
    public const string UsernameField = "username";
    public const string PasswordField = "password";
    public const string FromEmailField = "from_email";
    public const string FromNameField = "from_name";

    public NotificationProviderCode Provider { get; } = NotificationProviderCode.Create(Code);

    public IReadOnlyList<NotificationChannel> Channels { get; } = [NotificationChannel.Email];

    public IReadOnlyList<string> CredentialFields { get; } =
        [HostField, PortField, SecurityField, UsernameField, PasswordField, FromEmailField, FromNameField];

    public INotificationSender Create(NotificationChannel channel, IReadOnlyDictionary<string, string> credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return new SmtpSender(Provider, transport, SmtpAccount.From(credentials));
    }

    /// <summary>Un corps qui commence par une balise HTML courante part en HTML, sinon en texte.</summary>
    internal static bool LooksLikeHtml(string body) => HtmlStart().IsMatch(body);

    [GeneratedRegex(@"^\s*<(!doctype|html|body|div|p|table|h[1-6]|br|span|a)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HtmlStart();

    internal static MimeMessage Build(SmtpAccount account, NotificationSendRequest request)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(account.FromName, account.FromEmail));
        message.To.Add(MailboxAddress.Parse(request.Recipient.Address));
        message.Subject = request.Content.Subject ?? string.Empty;
        // Identifiant de message stable par tentative : c'est l'id renvoyé au module.
        message.MessageId = $"{request.DeliveryId.Value:N}@{account.MessageIdDomain}";
        var builder = new BodyBuilder();
        if (LooksLikeHtml(request.Content.Body))
        {
            builder.HtmlBody = request.Content.Body;
        }
        else
        {
            builder.TextBody = request.Content.Body;
        }

        message.Body = builder.ToMessageBody();
        return message;
    }

    /// <summary>
    /// Traduit une erreur SMTP en échec du module. Le message brut n'est JAMAIS repris : il peut citer l'adresse du
    /// destinataire ou l'identifiant du compte.
    /// </summary>
    internal static NotificationFailure Translate(Exception exception) => exception switch
    {
        SmtpCommandException { ErrorCode: SmtpErrorCode.RecipientNotAccepted } => NotificationFailure.Permanent(NotificationFailure.InvalidEmail),
        SmtpCommandException { ErrorCode: SmtpErrorCode.SenderNotAccepted } => NotificationFailure.Temporary("SENDER_REJECTED"),
        SmtpCommandException { StatusCode: SmtpStatusCode.MailboxUnavailable or SmtpStatusCode.MailboxNameNotAllowed or SmtpStatusCode.UserNotLocalTryAlternatePath }
            => NotificationFailure.Permanent(NotificationFailure.InvalidEmail),
        SmtpCommandException { StatusCode: SmtpStatusCode.ExceededStorageAllocation or SmtpStatusCode.InsufficientStorage }
            => NotificationFailure.Temporary(NotificationFailure.RateLimited),
        SmtpCommandException command when (int)command.StatusCode >= 500 => NotificationFailure.Permanent(NotificationFailure.Rejected),
        AuthenticationException => NotificationFailure.Temporary("PROVIDER_AUTH_FAILED"),
        TimeoutException or OperationCanceledException => NotificationFailure.Temporary(NotificationFailure.Timeout),
        _ => NotificationFailure.Temporary(NotificationFailure.ProviderUnavailable),
    };

    private sealed class SmtpSender(NotificationProviderCode provider, ISmtpTransport transport, SmtpAccount account) : INotificationSender
    {
        public NotificationProviderCode Provider => provider;

        public NotificationChannel Channel => NotificationChannel.Email;

        public async Task<NotificationSendResult> SendAsync(NotificationSendRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            MimeMessage message;
            try
            {
                message = Build(account, request);
            }
            catch (ParseException)
            {
                return NotificationSendResult.Failed(NotificationFailure.Permanent(NotificationFailure.InvalidEmail));
            }

            try
            {
                await transport.SendAsync(account, message, cancellationToken);
                return NotificationSendResult.Accepted(ProviderMessageId.Create(message.MessageId));
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                return NotificationSendResult.Failed(Translate(exception));
            }
        }
    }
}

/// <summary>Un compte SMTP lu au coffre, en mémoire le temps de l'envoi. <see cref="ToString"/> ne montre aucun secret.</summary>
internal sealed record SmtpAccount(
    string Host, int Port, SecureSocketOptions Security, string Username, string Password, string FromEmail, string FromName)
{
    public string MessageIdDomain => FromEmail[(FromEmail.IndexOf('@', StringComparison.Ordinal) + 1)..];

    public static SmtpAccount From(IReadOnlyDictionary<string, string> credentials)
    {
        string Read(string field) => credentials.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new InvalidOperationException($"The SMTP account has no {field}.");

        var port = int.TryParse(Read(SmtpSenderFactory.PortField), out var parsed) && parsed is > 0 and < 65536
            ? parsed
            : throw new InvalidOperationException("The SMTP port is invalid.");
        var security = Read(SmtpSenderFactory.SecurityField).ToLowerInvariant() switch
        {
            "ssl" or "tls" or "sslonconnect" => SecureSocketOptions.SslOnConnect,
            "none" => SecureSocketOptions.None,
            "starttls" => SecureSocketOptions.StartTls,
            _ => throw new InvalidOperationException("The SMTP security must be starttls, ssl or none."),
        };
        var from = Read(SmtpSenderFactory.FromEmailField);
        if (!from.Contains('@', StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The SMTP sender address is invalid.");
        }

        // Le mot de passe est gardé tel quel : un espace peut en faire partie.
        var password = credentials.TryGetValue(SmtpSenderFactory.PasswordField, out var secret) && secret.Length > 0
            ? secret
            : throw new InvalidOperationException("The SMTP account has no password.");
        return new SmtpAccount(
            Read(SmtpSenderFactory.HostField), port, security, Read(SmtpSenderFactory.UsernameField), password, from,
            Read(SmtpSenderFactory.FromNameField));
    }

    public override string ToString() => $"SMTP {Host}:{Port}";
}

/// <summary>La connexion au serveur SMTP, isolée pour les tests.</summary>
internal interface ISmtpTransport
{
    Task SendAsync(SmtpAccount account, MimeMessage message, CancellationToken cancellationToken);
}

internal sealed class MailKitSmtpTransport : ISmtpTransport
{
    public async Task SendAsync(SmtpAccount account, MimeMessage message, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient { Timeout = 20_000 };
        await client.ConnectAsync(account.Host, account.Port, account.Security, cancellationToken);
        await client.AuthenticateAsync(account.Username, account.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}
