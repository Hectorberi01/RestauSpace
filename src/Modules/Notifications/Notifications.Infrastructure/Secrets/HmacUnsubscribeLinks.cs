using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Notifications.Application.Abstractions;
using Notifications.Domain;

namespace Notifications.Infrastructure.Secrets;

/// <summary>
/// Jeton d'un lien de désinscription : base64url(version | tenant | client | canal) « . » base64url(HMAC-SHA256). La
/// clé est dérivée (HKDF) de la clé OTP de l'hôte pour ce seul usage : aucun nouveau secret à gérer, et un jeton ne
/// sert à rien d'autre. Comparaison à temps constant.
/// </summary>
internal sealed class HmacUnsubscribeLinks : IUnsubscribeLinks
{
    private const byte Version = 1;
    private const int PayloadSize = 1 + 16 + 16 + 4;
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("notifications.unsubscribe-link.v1");

    private readonly byte[] _key;

    public HmacUnsubscribeLinks(NotificationsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.OtpKey is not { Length: >= 32 } master)
        {
            throw new InvalidOperationException("Notifications OTP key must be at least 32 bytes.");
        }

        _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, master, 32, info: Purpose);
    }

    public string Issue(UnsubscribeClaim claim)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var payload = new byte[PayloadSize];
        payload[0] = Version;
        claim.TenantId.Value.TryWriteBytes(payload.AsSpan(1, 16));
        claim.CustomerId.Value.TryWriteBytes(payload.AsSpan(17, 16));
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(33, 4), (int)claim.Channel);
        return $"{Base64Url.EncodeToString(payload)}.{Base64Url.EncodeToString(HMACSHA256.HashData(_key, payload))}";
    }

    public UnsubscribeClaim? Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200)
        {
            return null;
        }

        var parts = token.Trim().Split('.');
        if (parts.Length != 2)
        {
            return null;
        }

        byte[] payload;
        byte[] signature;
        try
        {
            payload = Base64Url.DecodeFromChars(parts[0]);
            signature = Base64Url.DecodeFromChars(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        if (payload.Length != PayloadSize || payload[0] != Version
            || !CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(_key, payload)))
        {
            return null;
        }

        var tenant = new Guid(payload.AsSpan(1, 16));
        var customer = new Guid(payload.AsSpan(17, 16));
        var channel = (NotificationChannel)BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(33, 4));
        if (tenant == Guid.Empty || customer == Guid.Empty || !Enum.IsDefined(channel) || channel.ToConsentChannel() is null)
        {
            return null;
        }

        return new UnsubscribeClaim(TenantId.From(tenant), CustomerId.From(customer), channel);
    }
}
