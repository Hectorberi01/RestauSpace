namespace Payments.Application;

/// <summary>
/// Paiement par carte cadeau ou avoir : le prestataire « sur place » <see cref="ProviderCode"/>. La référence du
/// paiement désigne la carte et le mouvement (<c>gc:{carte}:{mouvement}</c>), jamais le code saisi.
/// </summary>
public static class GiftCards
{
    public const string ProviderCode = "gift_card";
    private const string Prefix = "gc:";

    public static string PaymentMovement(Guid movementId) => "pay:" + movementId.ToString("N");

    public static string RefundMovement(Guid refundId) => "refund:" + refundId.ToString("N");

    public static string PaymentReference(Guid cardId, Guid movementId) => $"{Prefix}{cardId:N}:{movementId:N}";

    public static (Guid CardId, Guid MovementId)? Parse(string? reference)
    {
        if (reference is null || !reference.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = reference[Prefix.Length..].Split(':');
        return parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out var card) && Guid.TryParseExact(parts[1], "N", out var movement)
            ? (card, movement)
            : null;
    }
}
