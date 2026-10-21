namespace Identity.Infrastructure.Notifications;

public sealed class IdentityEmailOptions
{
    public const string TokenPlaceholder = "{token}";

    public string ProductName { get; set; } = "RestauSpace";

    /// <summary>Pages du front end, chacune avec <c>{token}</c> à l'endroit du jeton.</summary>
    public string VerifyEmailUrl { get; set; } = string.Empty;

    public string ResetPasswordUrl { get; set; } = string.Empty;

    public string AcceptInvitationUrl { get; set; } = string.Empty;

    internal string Link(string template, string token) =>
        template.Replace(TokenPlaceholder, Uri.EscapeDataString(token), StringComparison.Ordinal);

    internal void Validate()
    {
        foreach (var (name, template) in new[]
                 {
                     (nameof(VerifyEmailUrl), VerifyEmailUrl),
                     (nameof(ResetPasswordUrl), ResetPasswordUrl),
                     (nameof(AcceptInvitationUrl), AcceptInvitationUrl),
                 })
        {
            if (!template.Contains(TokenPlaceholder, StringComparison.Ordinal)
                || !Uri.TryCreate(template.Replace(TokenPlaceholder, "t", StringComparison.Ordinal), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException(
                    $"Identity email link '{name}' must be an absolute http(s) URL containing {TokenPlaceholder}.");
            }
        }
    }
}

public enum SmtpSecurity
{
    StartTls,
    SslOnConnect,

    /// <summary>En clair : uniquement pour un serveur de test local.</summary>
    None,
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string FromAddress { get; set; } = string.Empty;

    public string? FromName { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) || Port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("The SMTP host and port are required.");
        }

        if (!MimeKit.MailboxAddress.TryParse(FromAddress, out _))
        {
            throw new InvalidOperationException("The SMTP sender address ('FromAddress') is missing or invalid.");
        }
    }
}
