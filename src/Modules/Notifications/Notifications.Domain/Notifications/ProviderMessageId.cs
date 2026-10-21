namespace Notifications.Domain;

/// <summary>
/// L'identifiant que le prestataire donne au message qu'il a accepté (id Resend, SID Twilio, wamid WhatsApp...). C'est
/// par lui qu'un webhook retrouve la <see cref="NotificationDelivery"/> concernée. Opaque : gardé tel quel, sans espace ni
/// caractère de contrôle, 256 caractères au plus. Ce n'est PAS un secret.
/// </summary>
public sealed class ProviderMessageId : ValueObject
{
    public const int MaxLength = 256;

    private ProviderMessageId(string value) => Value = value;

    public string Value { get; }

    public static ProviderMessageId Create(string? value)
    {
        DomainException.ThrowIf(
            string.IsNullOrEmpty(value)
            || value.Length > MaxLength
            || value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)),
            NotificationErrors.ProviderMessageIdInvalid);
        return new ProviderMessageId(value!);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
