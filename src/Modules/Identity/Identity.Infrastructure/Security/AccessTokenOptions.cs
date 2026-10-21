namespace Identity.Infrastructure.Security;

public sealed class AccessTokenOptions
{
    public const int MinSigningKeyBytes = 32;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>Clé HMAC-SHA256 en Base64, au moins 32 octets. Un secret : JAMAIS dans le contrôle de source.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(15);

    public byte[] SigningKeyBytes()
    {
        byte[] key;
        try
        {
            key = Convert.FromBase64String(SigningKey);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("The access token signing key must be base64.");
        }

        if (key.Length < MinSigningKeyBytes)
        {
            throw new InvalidOperationException($"The access token signing key must be at least {MinSigningKeyBytes} bytes.");
        }

        return key;
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer) || string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("The access token issuer and audience are required.");
        }

        if (Lifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("The access token lifetime must be positive.");
        }

        _ = SigningKeyBytes();
    }
}
