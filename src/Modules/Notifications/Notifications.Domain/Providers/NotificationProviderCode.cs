namespace Notifications.Domain;

/// <summary>
/// Code technique stable d'un prestataire (« RESEND », « TWILIO », « WHATSAPP_META »), en majuscules : une lettre puis
/// lettres, chiffres ou « _ », 2 à 32 caractères. Le domaine ne connaît qu'un seul code : <see cref="InApp"/>, le
/// prestataire interne ; tous les autres sont des données du catalogue (ajouter un prestataire ne touche pas au domaine).
/// </summary>
public sealed class NotificationProviderCode : ValueObject
{
    public const int MinLength = 2;
    public const int MaxLength = 32;

    /// <summary>Notifications affichées dans l'application, livrées par la plateforme elle-même.</summary>
    public static readonly NotificationProviderCode InApp = new("IN_APP");

    private NotificationProviderCode(string value) => Value = value;

    public string Value { get; }

    public static NotificationProviderCode Create(string? value)
    {
        var normalized = value?.Trim().ToUpperInvariant();
        DomainException.ThrowIf(
            normalized is null
            || normalized.Length is < MinLength or > MaxLength
            || normalized[0] is not (>= 'A' and <= 'Z')
            || normalized.Any(character => character is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_')),
            NotificationProviderErrors.CodeInvalid);
        return new NotificationProviderCode(normalized!);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
