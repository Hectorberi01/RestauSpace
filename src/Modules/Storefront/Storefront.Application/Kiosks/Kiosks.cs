using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Storefront.Application.Abstractions;
using Storefront.Application.Queries;
using Storefront.Domain;

namespace Storefront.Application.Kiosks;

/// <summary>
/// Jetons d'appareil des bornes (<c>kd_</c> + 24 octets aléatoires), secrets et codes d'appairage. Seules leurs empreintes
/// SHA-256 sont gardées ; rien de tout cela n'est jamais journalisé.
/// </summary>
public static class KioskSecrets
{
    public const string TokenPrefix = "kd_";
    public const string HeaderName = "X-Kiosk-Device";

    public static string NewToken() => TokenPrefix + Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24));

    public static string NewSecret() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24));

    /// <summary>Code à 6 chiffres affiché par la borne.</summary>
    public static string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public static bool LooksLikeToken(string? token) =>
        token is { Length: > 20 and < 100 } && token.StartsWith(TokenPrefix, StringComparison.Ordinal)
        && token.Skip(TokenPrefix.Length).All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static bool LooksLikeCode(string? code) => code is { Length: 6 } && code.All(char.IsAsciiDigit);

    public static PublishableKeyHash Hash(string value) =>
        PublishableKeyHash.Create(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value))));

    public static string Hint(string token) => token[^KioskDevice.TokenHintLength..];
}

public static class KioskApplicationErrors
{
    public static readonly DomainError DeviceRequired =
        new("storefront.kiosk.device_required", "This kiosk is not paired: show a pairing code and enter it in the console.");
    public static readonly DomainError LimitReached =
        new("storefront.kiosk.limit_reached", "All the kiosks of your subscription are paired: revoke one or add kiosks to your option.");
    public static readonly DomainError KioskNotFound = new("storefront.kiosk.not_found", "The kiosk does not exist.");
}

/// <param name="Code">À afficher en grand sur la borne ; à saisir dans la console.</param>
/// <param name="Secret">Gardé par la borne seulement : il lui permet de retirer son jeton une fois le code saisi.</param>
public sealed record KioskPairingStartedDto(Guid PairingId, string Code, string Secret, DateTimeOffset ExpiresAtUtc)
{
    public override string ToString() => $"KioskPairingStartedDto({PairingId}, ***)";
}

public enum KioskPairingState
{
    Pending = 1,
    Paired = 2,
    Expired = 3,
}

/// <param name="DeviceToken">Seulement une fois, à l'appairage : à garder par la borne et à envoyer dans l'en-tête X-Kiosk-Device.</param>
public sealed record KioskPairingStateDto(KioskPairingState State, string? DeviceToken = null, Guid? LocationId = null, string? DeviceName = null)
{
    public override string ToString() => $"KioskPairingStateDto({State}, ***)";
}

/// <summary>Une borne non appairée (clé de l'application borne seulement) affiche un code d'appairage.</summary>
public sealed record StartKioskPairingCommand : ICommand<KioskPairingStartedDto>;

public sealed class StartKioskPairingHandler(StorefrontWorkspace workspace, IStorefrontClient client, IKioskPairingRepository pairings, IStorefrontUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<StartKioskPairingCommand, KioskPairingStartedDto>
{
    public async Task<KioskPairingStartedDto> HandleAsync(StartKioskPairingCommand command, CancellationToken cancellationToken = default)
    {
        var appId = client.AppId ?? throw new DomainException(StorefrontApplicationErrors.ClientRequired);
        var app = await workspace.Apps.FindAsync(workspace.CurrentTenant, appId, cancellationToken)
            ?? throw new NotFoundException(StorefrontApplicationErrors.AppNotFound);
        var code = KioskSecrets.NewCode();
        var secret = KioskSecrets.NewSecret();
        var pairing = KioskPairing.Start(app, KioskSecrets.Hash($"{app.TenantId.Value:N}:{code}"), KioskSecrets.Hash(secret), clock);
        pairings.Add(pairing);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new KioskPairingStartedDto(pairing.Id.Value, code, secret, pairing.ExpiresAtUtc);
    }
}

/// <summary>
/// La borne demande où en est son appairage, avec son secret. Approuvé : son appareil est créé et son jeton remis, une
/// seule fois. Un secret faux répond comme un appairage expiré.
/// </summary>
public sealed record ClaimKioskPairingCommand(Guid PairingId, string? Secret) : ICommand<KioskPairingStateDto>
{
    public override string ToString() => $"ClaimKioskPairingCommand({PairingId}, ***)";
}

public sealed class ClaimKioskPairingHandler(
    StorefrontWorkspace workspace,
    IStorefrontClient client,
    IKioskPairingRepository pairings,
    IKioskDeviceRepository devices,
    IStorefrontUnitOfWork unitOfWork,
    IClock clock) : ICommandHandler<ClaimKioskPairingCommand, KioskPairingStateDto>
{
    private static readonly KioskPairingStateDto Expired = new(KioskPairingState.Expired);

    public async Task<KioskPairingStateDto> HandleAsync(ClaimKioskPairingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        var pairing = command.PairingId == Guid.Empty ? null : await pairings.FindAsync(tenant, KioskPairingId.From(command.PairingId), cancellationToken);
        if (pairing is null || client.AppId != pairing.AppId || string.IsNullOrEmpty(command.Secret)
            || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(KioskSecrets.Hash(command.Secret).Value), Encoding.ASCII.GetBytes(pairing.SecretHash.Value))
            || pairing.IsExpired(clock.UtcNow) || pairing.Status == KioskPairingStatus.Completed)
        {
            return Expired;
        }

        if (pairing.Status == KioskPairingStatus.Pending)
        {
            return new KioskPairingStateDto(KioskPairingState.Pending);
        }

        var app = await workspace.Apps.FindAsync(tenant, pairing.AppId, cancellationToken) ?? throw new NotFoundException(StorefrontApplicationErrors.AppNotFound);
        var token = KioskSecrets.NewToken();
        var device = KioskDevice.Pair(app, new LocationReference(tenant, pairing.LocationId!.Value), pairing.DeviceName, KioskSecrets.Hash(token), KioskSecrets.Hint(token), clock);
        pairing.Complete(device, clock);
        devices.Add(device);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new KioskPairingStateDto(KioskPairingState.Paired, token, device.LocationId.Value, device.Name);
    }
}

/// <summary>
/// Console : le code lu sur la borne, l'établissement où elle est posée et son nom. Refusé au-delà du nombre de bornes
/// de l'option, ou pour un établissement que l'application ne sert pas.
/// </summary>
public sealed record ApproveKioskPairingCommand(string? Code, Guid LocationId, string? Name) : ICommand;

public sealed class ApproveKioskPairingHandler(
    StorefrontWorkspace workspace,
    IKioskPairingRepository pairings,
    IKioskDeviceRepository devices,
    IClock clock) : ICommandHandler<ApproveKioskPairingCommand>
{
    public async Task HandleAsync(ApproveKioskPairingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var code = command.Code?.Replace(" ", string.Empty, StringComparison.Ordinal);
        var pairing = KioskSecrets.LooksLikeCode(code)
            ? await pairings.FindPendingByCodeAsync(tenant, KioskSecrets.Hash($"{tenant.Value:N}:{code}"), clock.UtcNow, cancellationToken)
            : null;
        if (pairing is null)
        {
            throw new DomainException(KioskErrors.PairingInvalid);
        }

        var location = (await workspace.RequireLocationsAsync([command.LocationId], clock.UtcNow, cancellationToken)).Single();
        var app = await workspace.Apps.FindAsync(tenant, pairing.AppId, cancellationToken) ?? throw new NotFoundException(StorefrontApplicationErrors.AppNotFound);
        DomainException.ThrowIf(!app.Serves(location.LocationId), KioskErrors.LocationNotServed);
        await KioskLimits.EnsureRoomAsync(workspace, devices, pairings, tenant, clock, cancellationToken);
        pairing.Approve(location, command.Name, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

internal static class KioskLimits
{
    public static async Task EnsureRoomAsync(
        StorefrontWorkspace workspace, IKioskDeviceRepository devices, IKioskPairingRepository pairings, TenantId tenant, IClock clock, CancellationToken cancellationToken)
    {
        DomainException.ThrowIf(!await workspace.Entitlements.AllowsAsync(tenant, SalesAppKind.Kiosk, cancellationToken), StorefrontApplicationErrors.OptionRequired);
        if (await workspace.Entitlements.LimitAsync(tenant, SalesAppKind.Kiosk, cancellationToken) is { } limit)
        {
            var used = await devices.CountActiveAsync(tenant, cancellationToken) + await pairings.CountApprovedAsync(tenant, clock.UtcNow, cancellationToken);
            DomainException.ThrowIf(used >= limit, KioskApplicationErrors.LimitReached);
        }
    }

    /// <summary>
    /// Gel des bornes, calculé à l'usage : les bornes actives sont rangées par date d'appairage ; au-delà de la limite de
    /// l'option, elles sont gelées (authentification refusée).
    /// </summary>
    public static async Task<IReadOnlySet<KioskDeviceId>> FrozenAsync(
        StorefrontWorkspace workspace, IKioskDeviceRepository devices, TenantId tenant, CancellationToken cancellationToken)
    {
        if (await workspace.Entitlements.LimitAsync(tenant, SalesAppKind.Kiosk, cancellationToken) is not { } limit)
        {
            return new HashSet<KioskDeviceId>();
        }

        return (await devices.ListAsync(tenant, cancellationToken))
            .Where(device => device.IsActive)
            .OrderBy(device => device.PairedAtUtc)
            .ThenBy(device => device.Id.Value)
            .Skip((int)Math.Clamp(limit, 0, int.MaxValue))
            .Select(device => device.Id)
            .ToHashSet();
    }
}

/// <param name="IsFrozen">Au-delà de la limite de l'option borne : elle ne s'authentifie plus.</param>
public sealed record KioskDto(
    Guid Id, Guid AppId, string AppName, Guid LocationId, string Name, KioskDeviceStatus Status, string TokenHint, DateTimeOffset PairedAtUtc, DateTimeOffset? RevokedAtUtc,
    bool IsFrozen = false);

/// <param name="Limit">Nombre de bornes de l'option ; null = sans limite.</param>
public sealed record KiosksDto(long? Limit, int Active, IReadOnlyList<KioskDto> Kiosks);

public sealed record ListKiosksQuery : IQuery<KiosksDto>;

public sealed class ListKiosksHandler(StorefrontWorkspace workspace, IKioskDeviceRepository devices) : IQueryHandler<ListKiosksQuery, KiosksDto>
{
    public async Task<KiosksDto> HandleAsync(ListKiosksQuery query, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var apps = (await workspace.Apps.ListAsync(tenant, cancellationToken)).ToDictionary(app => app.Id);
        var list = await devices.ListAsync(tenant, cancellationToken);
        var frozen = await KioskLimits.FrozenAsync(workspace, devices, tenant, cancellationToken);
        return new KiosksDto(
            await workspace.Entitlements.LimitAsync(tenant, SalesAppKind.Kiosk, cancellationToken),
            list.Count(device => device.IsActive),
            list.Select(device => new KioskDto(
                    device.Id.Value, device.AppId.Value, apps.GetValueOrDefault(device.AppId)?.Name ?? string.Empty, device.LocationId.Value, device.Name,
                    device.Status, device.TokenHint, device.PairedAtUtc, device.RevokedAtUtc, frozen.Contains(device.Id)))
                .ToList());
    }
}

/// <summary>Révoque une borne (volée, remplacée) : elle ne passe plus aucune commande et revient à l'écran d'appairage.</summary>
public sealed record RevokeKioskCommand(Guid KioskId) : ICommand;

public sealed class RevokeKioskHandler(StorefrontWorkspace workspace, IKioskDeviceRepository devices, IClock clock) : ICommandHandler<RevokeKioskCommand>
{
    public async Task HandleAsync(RevokeKioskCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var device = (command.KioskId == Guid.Empty ? null : await devices.FindAsync(workspace.CurrentTenant, KioskDeviceId.From(command.KioskId), cancellationToken))
            ?? throw new NotFoundException(KioskApplicationErrors.KioskNotFound);
        device.Revoke(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public static class KioskTicketTexts
{
    public const string PayAtCounter = "À PAYER AU COMPTOIR";
    public const string Paid = "PAYÉE";
}

/// <summary>Le ticket client d'une commande de borne : enseigne, numéro, lignes, total, paiement, heure locale.</summary>
internal static class KioskTickets
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public static async Task<bool> PrintAsync(
        StorefrontWorkspace workspace,
        IStorefrontTickets tickets,
        StorefrontOrderView view,
        string paymentText,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var tenant = workspace.CurrentTenant;
        var restaurant = await workspace.Tenants.FindAsync(tenant, cancellationToken);
        var location = (await workspace.Locations.ListAsync(tenant, nowUtc, cancellationToken)).SingleOrDefault(item => item.LocationId == view.LocationId);
        if (restaurant is null || location is null)
        {
            return false;
        }

        var ticket = new KioskTicket(
            restaurant.Name,
            location.Name,
            view.Number,
            view.Mode == ServiceMode.DineIn ? "Sur place" : "À emporter",
            view.Lines.Select(line => new KioskTicketLine(
                    line.Quantity,
                    line.VariantName is null ? line.Name : $"{line.Name} · {line.VariantName}",
                    Details(line),
                    Money(line.LineTotal, view.CurrencyCode)))
                .ToList(),
            Money(view.Total, view.CurrencyCode),
            paymentText,
            LocalTime(nowUtc, location.TimeZoneId));
        return await tickets.PrintAsync(tenant, view.LocationId, $"order-{view.OrderId.Value:N}", ticket, cancellationToken);
    }

    /// <summary>Options, produits choisis d'une formule et note, en une ligne de détail ; null s'il n'y a rien.</summary>
    private static string? Details(CartQuoteLine line)
    {
        var parts = new List<string>();
        if (line.Modifiers.Count > 0)
        {
            parts.Add(string.Join(", ", line.Modifiers.Select(modifier => modifier.Name)));
        }

        parts.AddRange((line.Components ?? []).Select(component =>
            $"{component.StepName} : {component.Name}" + (component.Modifiers.Count > 0 ? " + " + string.Join(", ", component.Modifiers.Select(modifier => modifier.Name)) : string.Empty)));
        var details = parts.Count == 0 ? null : string.Join(" ; ", parts);
        return line.Note is null ? details : details is null ? line.Note : $"{details} ({line.Note})";
    }

    public static string Money(decimal amount, string currency)
    {
        var (digits, symbol) = currency switch
        {
            "XOF" or "XAF" => (0, "F CFA"),
            "EUR" => (2, "€"),
            "USD" => (2, "$"),
            _ => (2, currency),
        };
        return amount.ToString(digits == 0 ? "N0" : "N2", French).Replace(' ', ' ').Replace(' ', ' ') + " " + symbol;
    }

    private static string LocalTime(DateTimeOffset nowUtc, string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.ConvertTime(nowUtc, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return nowUtc.ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);
        }
    }
}
