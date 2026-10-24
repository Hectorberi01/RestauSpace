using System.Net;
using System.Net.Sockets;

namespace SaasRestaurant.BuildingBlocks.Infrastructure;

/// <summary>
/// Anti-SSRF : seules les adresses publiques routables sont joignables. À appliquer à CHAQUE adresse résolue, juste avant
/// la connexion (DNS rebinding).
/// </summary>
public static class PublicAddresses
{
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPublicV4(address.GetAddressBytes()),
            AddressFamily.InterNetworkV6 => IsPublicV6(address.GetAddressBytes()),
            _ => false,
        };
    }

    /// <summary>
    /// Résout le nom, refuse si UNE adresse n'est pas publique, puis se connecte à ces adresses validées (aucune seconde
    /// résolution). Le socket rendu appartient à l'appelant.
    /// </summary>
    /// <exception cref="NonPublicAddressException">Le nom se résout vers une adresse non publique (ou vers aucune).</exception>
    public static async Task<Socket> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);
        // Un nom « mixte » (public + privé) est le signe d'un rebinding : refusé.
        if (addresses.Length == 0 || !addresses.All(IsPublic))
        {
            throw new NonPublicAddressException();
        }

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, port, cancellationToken);
            return socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool IsPublicV4(byte[] b) => b[0] switch
    {
        0 or 10 or 127 => false,                     // « ce réseau », privé, loopback
        100 => b[1] is < 64 or > 127,                // CGNAT 100.64.0.0/10
        169 => b[1] != 254,                          // link-local (métadonnées des clouds : 169.254.169.254)
        172 => b[1] is < 16 or > 31,                 // privé 172.16.0.0/12
        192 => !(b[1] == 168 || (b[1] == 0 && b[2] is 0 or 2)), // privé, IETF 192.0.0.0/24, documentation
        198 => b[1] is not (18 or 19) && !(b[1] == 51 && b[2] == 100), // bancs d'essai, documentation
        203 => !(b[1] == 0 && b[2] == 113),          // documentation
        >= 224 => false,                             // multicast, réservé, diffusion
        _ => true,
    };

    private static bool IsPublicV6(byte[] b)
    {
        // Seul l'espace unicast global 2000::/3 est routable sur Internet.
        if ((b[0] & 0xE0) != 0x20)
        {
            return false;
        }

        // Documentation 2001:db8::/32, Teredo 2001::/32 (tunnel vers une IPv4 quelconque).
        if (b[0] == 0x20 && b[1] == 0x01 && ((b[2] == 0x0D && b[3] == 0xB8) || (b[2] == 0 && b[3] == 0)))
        {
            return false;
        }

        // 6to4 2002::/16 : l'adresse IPv4 embarquée décide.
        if (b[0] == 0x20 && b[1] == 0x02)
        {
            return IsPublicV4([b[2], b[3], b[4], b[5]]);
        }

        return true;
    }
}

/// <summary>Connexion refusée : le nom se résout vers une adresse non publique.</summary>
public sealed class NonPublicAddressException() : Exception("The host resolves to a non-public address.");
