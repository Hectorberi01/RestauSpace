using System.Security.Cryptography;
using System.Text;

namespace Storefront.Application.Checkout;

/// <summary>
/// Limites de la plateforme contre les commandes en ligne laissées impayées (section <c>Storefront:CheckoutLimits</c>).
/// Une commande en attente de paiement en ligne tient son créneau et son coupon jusqu'à son échéance : ce délai est
/// plafonné à <see cref="PaymentHoldMinutes"/>, puis la commande expire et les rend.
/// </summary>
public sealed class CheckoutLimits
{
    public const int DefaultMaxPendingPayments = 3;
    public const int DefaultPaymentHoldMinutes = 10;

    /// <summary>Commandes en attente de paiement en ligne au plus, par application, par adresse IP et par client.</summary>
    public int MaxPendingPayments { get; set; } = DefaultMaxPendingPayments;

    /// <summary>Délai de paiement en ligne au plus (minutes), quel que soit le délai choisi par l'établissement.</summary>
    public int PaymentHoldMinutes { get; set; } = DefaultPaymentHoldMinutes;

    /// <summary>Le délai de paiement appliqué : celui de l'établissement, plafonné (1 minute au moins).</summary>
    public TimeSpan UnpaidDelay(int settingsMinutes) => TimeSpan.FromMinutes(Math.Max(1, Math.Min(settingsMinutes, PaymentHoldMinutes)));
}

/// <summary>Empreinte (HMAC-SHA256) de l'adresse IP du client : on compte ses commandes sans garder l'adresse.</summary>
public sealed class ClientAddressKeys
{
    private static readonly byte[] Purpose = "storefront.client-address.v1"u8.ToArray();

    private readonly byte[] _key;

    /// <param name="key">32 octets au moins.</param>
    public ClientAddressKeys(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32)
        {
            throw new InvalidOperationException("The client address key must be at least 32 bytes.");
        }

        _key = HMACSHA256.HashData(key, Purpose);
    }

    /// <summary>32 caractères hexadécimaux ; null sans adresse.</summary>
    public string? For(string? address) =>
        string.IsNullOrWhiteSpace(address)
            ? null
            : Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(address.Trim())).AsSpan(0, 16));
}
