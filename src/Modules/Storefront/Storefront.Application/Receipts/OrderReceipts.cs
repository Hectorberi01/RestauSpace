using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Storefront.Application.Abstractions;
using Storefront.Domain;

namespace Storefront.Application.Receipts;

/// <summary>
/// Lien du reçu d'une commande : il porte le restaurant et la commande, signés en HMAC-SHA256 (un usage distinct du suivi),
/// et rien d'autre. Il ouvre la LECTURE du reçu de cette commande seulement, avec la clé publique de son restaurant :
/// ni suivi, ni annulation, ni paiement.
/// </summary>
public sealed class ReceiptLinks
{
    private const int IdLength = 16;
    private const int SignatureLength = 16;
    private static readonly byte[] Purpose = "storefront.order-receipt.v1"u8.ToArray();

    private readonly byte[] _key;

    public ReceiptLinks(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length < 32)
        {
            throw new InvalidOperationException("The receipt link key must be at least 32 bytes.");
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

    /// <summary>La commande du lien, s'il est authentique et de ce restaurant ; null sinon.</summary>
    public OrderId? Read(string? token, TenantId tenantId)
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
        return tenant == tenantId.Value && order != Guid.Empty ? OrderId.From(order) : null;
    }

    private byte[] Sign(ReadOnlySpan<byte> ids)
    {
        var message = new byte[Purpose.Length + ids.Length];
        Purpose.CopyTo(message, 0);
        ids.CopyTo(message.AsSpan(Purpose.Length));
        return HMACSHA256.HashData(_key, message);
    }
}

public static class ReceiptErrors
{
    // Aussi la réponse pour un lien faux, d'un autre restaurant, ou une commande en brouillon.
    public static readonly DomainError NotFound = new("storefront.receipt.not_found", "This receipt does not exist.");

    public static readonly DomainError RecipientRequired =
        new("storefront.receipt.recipient_required", "Enter the customer's phone number, or attach a customer with a phone number to the order.");

    public static readonly DomainError ChannelInvalid = new("storefront.receipt.channel_invalid", "A receipt is sent by WhatsApp or SMS.");
}

/// <summary>Le reçu d'une commande, déjà formaté (montants dans la devise de la commande).</summary>
public sealed record PublicReceiptDto(
    string RestaurantName,
    string LocationName,
    string? Address,
    string OrderNumber,
    string ServiceMode,
    string Date,
    bool IsCancelled,
    IReadOnlyList<ReceiptItem> Lines,
    IReadOnlyList<ReceiptAmount> Totals,
    string Total,
    IReadOnlyList<ReceiptAmount> Payments,
    string? AmountDue,
    IReadOnlyList<string> Footer,
    string? QrCode,
    bool PricesIncludeTax)
{
    public static PublicReceiptDto From(StorefrontReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return new PublicReceiptDto(
            receipt.RestaurantName, receipt.LocationName, receipt.Address, receipt.OrderNumber, receipt.ServiceMode, receipt.Date, receipt.IsCancelled,
            receipt.Lines, receipt.Totals, receipt.Total, receipt.Payments, receipt.AmountDue, receipt.Footer, receipt.QrCode, receipt.PricesIncludeTax);
    }
}

/// <summary>
/// Le lien du reçu (null si le restaurant n'a pas de site actif) et le reçu en texte, prêt pour un message WhatsApp
/// envoyé depuis le téléphone de l'équipier.
/// </summary>
public sealed record ReceiptLinkDto(string? Url, string Text, bool CanSendAutomatically);

public enum ReceiptSendStatus
{
    Sent = 1,

    /// <summary>Le module de notification a écarté l'envoi (pas de fournisseur, pas de modèle validé...) : voir la raison.</summary>
    Skipped = 2,

    /// <summary>Pas de site actif : pas de lien de reçu à envoyer automatiquement.</summary>
    NoLink = 3,
}

/// <param name="Text">Le reçu en texte, pour envoyer soi-même par WhatsApp quand l'envoi automatique n'a pas lieu.</param>
public sealed record ReceiptSendResult(ReceiptSendStatus Status, string? Reason, string? Url, string Text);

/// <summary>Le reçu par son lien (clé publique du restaurant).</summary>
public sealed record GetPublicReceiptQuery(string? Token) : IQuery<PublicReceiptDto>
{
    public override string ToString() => "GetPublicReceiptQuery (***)";
}

public sealed class GetPublicReceiptHandler(StorefrontWorkspace workspace, IStorefrontReceipts receipts, ReceiptLinks links)
    : IQueryHandler<GetPublicReceiptQuery, PublicReceiptDto>
{
    public async Task<PublicReceiptDto> HandleAsync(GetPublicReceiptQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var receipt = links.Read(query.Token, tenant) is { } orderId ? await receipts.FindAsync(tenant, orderId, cancellationToken) : null;
        return PublicReceiptDto.From(receipt ?? throw new NotFoundException(ReceiptErrors.NotFound));
    }
}

/// <summary>Console : le lien du reçu d'une commande et son texte, pour l'envoyer soi-même.</summary>
public sealed record GetReceiptLinkQuery(Guid OrderId) : IQuery<ReceiptLinkDto>;

public sealed class GetReceiptLinkHandler(StorefrontWorkspace workspace, IStorefrontReceipts receipts, ReceiptLinks links)
    : IQueryHandler<GetReceiptLinkQuery, ReceiptLinkDto>
{
    public async Task<ReceiptLinkDto> HandleAsync(GetReceiptLinkQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (receipt, url) = await OrderReceipts.LoadAsync(workspace, receipts, links, query.OrderId, cancellationToken);
        return new ReceiptLinkDto(url, ReceiptText.Compose(receipt, url), url is not null);
    }
}

/// <summary>
/// Console : envoie le lien du reçu au client par le compte WhatsApp (ou SMS) du restaurant, via le module de
/// notification (type <c>order.receipt</c>). Destinataire : le numéro saisi, sinon le client de la commande. Le numéro
/// n'est jamais journalisé ni gardé ailleurs que dans la notification.
/// </summary>
public sealed record SendReceiptCommand(Guid OrderId, ReceiptChannel Channel, string? Phone = null) : ICommand<ReceiptSendResult>
{
    public override string ToString() => $"SendReceiptCommand ({OrderId}, {Channel}, ***)";
}

public sealed class SendReceiptHandler(
    StorefrontWorkspace workspace, IStorefrontReceipts receipts, IStorefrontReceiptMessages messages, ReceiptLinks links, IClock clock)
    : ICommandHandler<SendReceiptCommand, ReceiptSendResult>
{
    public async Task<ReceiptSendResult> HandleAsync(SendReceiptCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        DomainException.ThrowIf(!Enum.IsDefined(command.Channel), ReceiptErrors.ChannelInvalid);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var (receipt, url) = await OrderReceipts.LoadAsync(workspace, receipts, links, command.OrderId, cancellationToken);
        var phone = string.IsNullOrWhiteSpace(command.Phone) ? null : PhoneNumber.Create(command.Phone);
        DomainException.ThrowIf(phone is null && receipt.CustomerId is null, ReceiptErrors.RecipientRequired);
        var text = ReceiptText.Compose(receipt, url);
        if (url is null)
        {
            return new ReceiptSendResult(ReceiptSendStatus.NoLink, null, null, text);
        }

        // Un double clic ne part qu'une fois ; renvoyer le reçu une minute plus tard reste possible.
        var recipient = phone?.Value ?? receipt.CustomerId!.Value.Value.ToString("N");
        var key = "receipt:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{receipt.OrderId.Value:N}|{command.Channel}|{recipient}|{clock.UtcNow:yyyyMMddHHmm}")))[..32];
        var outcome = await messages.SendAsync(
            workspace.CurrentTenant,
            new ReceiptMessage(receipt.LocationId, phone is null ? receipt.CustomerId : null, phone, command.Channel, receipt.RestaurantName, receipt.LocationName,
                receipt.OrderNumber, receipt.Total, url, key),
            cancellationToken);
        return outcome.Sent
            ? new ReceiptSendResult(ReceiptSendStatus.Sent, null, url, text)
            : new ReceiptSendResult(ReceiptSendStatus.Skipped, outcome.SkipReason, url, text);
    }
}

internal static class OrderReceipts
{
    public const string PagePath = "/recu";

    /// <summary>Le reçu d'une commande du restaurant courant (404 sinon, brouillon compris) et l'adresse de son lien.</summary>
    public static async Task<(StorefrontReceipt Receipt, string? Url)> LoadAsync(
        StorefrontWorkspace workspace, IStorefrontReceipts receipts, ReceiptLinks links, Guid orderId, CancellationToken cancellationToken)
    {
        var tenant = workspace.CurrentTenant;
        var receipt = (orderId == Guid.Empty ? null : await receipts.FindAsync(tenant, OrderId.From(orderId), cancellationToken))
            ?? throw new NotFoundException(ReceiptErrors.NotFound);
        var site = (await workspace.Apps.ListAsync(tenant, cancellationToken))
            .Where(app => app.Kind == SalesAppKind.Web && app.Status == SalesAppStatus.Active)
            .SelectMany(app => app.AllowedOrigins)
            .OrderBy(origin => origin.StartsWith("https://", StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(origin => origin, StringComparer.Ordinal)
            .FirstOrDefault();
        var url = site is null ? null : $"{site}{PagePath}?jeton={Uri.EscapeDataString(links.Issue(tenant, receipt.OrderId))}";
        return (receipt, url);
    }
}

/// <summary>Le reçu en texte pour un message : en-tête, lignes, totaux, paiements, mentions fiscales et lien.</summary>
public static class ReceiptText
{
    public static string Compose(StorefrontReceipt receipt, string? url)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var text = new StringBuilder();
        text.Append('*').Append(receipt.RestaurantName).Append("*\n");
        text.Append(receipt.LocationName).Append('\n');
        text.Append(receipt.IsCancelled ? "Commande annulée n° " : "Reçu n° ").Append(receipt.OrderNumber).Append(" · ").Append(receipt.Date).Append('\n');
        text.Append('\n');
        foreach (var line in receipt.Lines)
        {
            text.Append(line.Quantity.ToString(CultureInfo.InvariantCulture)).Append(" x ").Append(line.Name).Append(" : ").Append(line.Amount).Append('\n');
            if (line.Details is { Length: > 0 } details)
            {
                text.Append("   ").Append(details).Append('\n');
            }
        }

        text.Append('\n');
        foreach (var total in receipt.Totals)
        {
            text.Append(total.Label).Append(" : ").Append(total.Amount).Append('\n');
        }

        text.Append("*TOTAL : ").Append(receipt.Total).Append("*\n");
        foreach (var payment in receipt.Payments)
        {
            text.Append(payment.Label).Append(" : ").Append(payment.Amount).Append('\n');
        }

        if (receipt.AmountDue is { } due)
        {
            text.Append("Reste à payer : ").Append(due).Append('\n');
        }

        if (receipt.Footer.Count > 0)
        {
            text.Append('\n');
            foreach (var line in receipt.Footer)
            {
                text.Append(line).Append('\n');
            }
        }

        if (url is not null)
        {
            text.Append('\n').Append("Votre reçu : ").Append(url).Append('\n');
        }

        text.Append('\n').Append("Merci de votre visite !");
        return text.ToString();
    }
}
