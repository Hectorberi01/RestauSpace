using System.Buffers.Text;
using System.Security.Cryptography;

namespace Storefront.Application.Checkout;

/// <summary>La commande désignée par un jeton de suivi vérifié.</summary>
public sealed record TrackedOrder(TenantId TenantId, OrderId OrderId);

/// <summary>
/// Jeton de suivi remis au client à la validation : il porte le restaurant et la commande, signés en HMAC-SHA256, et rien
/// d'autre (aucune donnée personnelle). Il ne s'invente pas et ne se modifie pas ; il ouvre le suivi et l'annulation de
/// CETTE commande seulement, avec la clé publique de son restaurant.
/// </summary>
public sealed class OrderTrackingTokens
{
    private const int IdLength = 16;
    private const int SignatureLength = 16;
    private static readonly byte[] Purpose = "storefront.order-tracking.v1"u8.ToArray();

    private readonly byte[] _key;

    /// <param name="key">32 octets au moins.</param>
    public OrderTrackingTokens(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32)
        {
            throw new InvalidOperationException("The order tracking key must be at least 32 bytes.");
        }

        _key = key;
    }

    public string Issue(TenantId tenantId, OrderId orderId)
    {
        var token = new byte[IdLength * 2 + SignatureLength];
        tenantId.Value.TryWriteBytes(token.AsSpan(0, IdLength), bigEndian: true, out _);
        orderId.Value.TryWriteBytes(token.AsSpan(IdLength, IdLength), bigEndian: true, out _);
        Sign(token.AsSpan(0, IdLength * 2)).AsSpan(0, SignatureLength).CopyTo(token.AsSpan(IdLength * 2));
        return Base64Url.EncodeToString(token);
    }

    /// <summary>La commande du jeton ; null s'il est faux.</summary>
    public TrackedOrder? Read(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 128)
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Base64Url.DecodeFromChars(token);
        }
        catch (FormatException)
        {
            return null;
        }

        if (bytes.Length != IdLength * 2 + SignatureLength
            || !CryptographicOperations.FixedTimeEquals(Sign(bytes.AsSpan(0, IdLength * 2)).AsSpan(0, SignatureLength), bytes.AsSpan(IdLength * 2)))
        {
            return null;
        }

        var tenant = new Guid(bytes.AsSpan(0, IdLength), bigEndian: true);
        var order = new Guid(bytes.AsSpan(IdLength, IdLength), bigEndian: true);
        return tenant == Guid.Empty || order == Guid.Empty ? null : new TrackedOrder(TenantId.From(tenant), OrderId.From(order));
    }

    private byte[] Sign(ReadOnlySpan<byte> ids)
    {
        var message = new byte[Purpose.Length + ids.Length];
        Purpose.CopyTo(message, 0);
        ids.CopyTo(message.AsSpan(Purpose.Length));
        return HMACSHA256.HashData(_key, message);
    }
}
