namespace Notifications.Domain;

/// <summary>
/// OÙ joindre le destinataire, selon le canal : une adresse email pour Email, un numéro E.164 pour Sms et WhatsApp, un
/// jeton d'appareil opaque pour Push, l'id de l'utilisateur pour InApp. L'adresse est toujours cohérente avec le canal.
/// QUI est le destinataire (client, utilisateur) est ailleurs : <see cref="RecipientId"/> sur la notification. Seule
/// exception : le destinataire effacé laissé sur une notification expurgée, qui garde le canal et a une adresse vide,
/// valeur qu'aucun vrai destinataire ne peut avoir.
/// </summary>
public sealed class NotificationRecipient : ValueObject
{
    public const int MaxDeviceTokenLength = 512;

    private NotificationRecipient(NotificationChannel channel, string address)
    {
        Channel = channel;
        Address = address;
    }

    public NotificationChannel Channel { get; }

    public string Address { get; }

    /// <summary>Vrai quand l'adresse a été effacée. Un tel destinataire ne se construit pas hors du domaine.</summary>
    public bool IsRedacted => Address.Length == 0;

    public static NotificationRecipient ForEmail(Email email)
    {
        ArgumentNullException.ThrowIfNull(email);
        return new NotificationRecipient(NotificationChannel.Email, email.Value);
    }

    public static NotificationRecipient ForSms(PhoneNumber phoneNumber)
    {
        ArgumentNullException.ThrowIfNull(phoneNumber);
        return new NotificationRecipient(NotificationChannel.Sms, phoneNumber.Value);
    }

    public static NotificationRecipient ForWhatsApp(PhoneNumber phoneNumber)
    {
        ArgumentNullException.ThrowIfNull(phoneNumber);
        return new NotificationRecipient(NotificationChannel.WhatsApp, phoneNumber.Value);
    }

    public static NotificationRecipient ForPush(string? deviceToken)
    {
        DomainException.ThrowIf(
            string.IsNullOrEmpty(deviceToken)
            || deviceToken.Length > MaxDeviceTokenLength
            || deviceToken.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)),
            NotificationErrors.DeviceTokenInvalid);

        return new NotificationRecipient(NotificationChannel.Push, deviceToken!);
    }

    /// <summary>Affichée dans l'application de cet utilisateur (tableau de bord, application mobile).</summary>
    public static NotificationRecipient ForInApp(UserId userId) =>
        new(NotificationChannel.InApp, Guard.NotEmpty(userId).Value.ToString("N"));

    internal static NotificationRecipient Redacted(NotificationChannel channel) => new(channel, string.Empty);

    /// <summary>Construit un destinataire depuis une saisie brute, l'adresse validée selon la règle du canal.</summary>
    public static NotificationRecipient Create(NotificationChannel channel, string? address) => channel switch
    {
        NotificationChannel.Email => ForEmail(Email.Create(address)),
        NotificationChannel.Sms => ForSms(PhoneNumber.Create(address)),
        NotificationChannel.WhatsApp => ForWhatsApp(PhoneNumber.Create(address)),
        NotificationChannel.Push => ForPush(address),
        NotificationChannel.InApp => ForInApp(ParseUser(address)),
        _ => throw new DomainException(NotificationErrors.ChannelInvalid),
    };

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Channel;
        yield return Address;
    }

    public override string ToString() => IsRedacted ? $"{Channel}:[redacted]" : $"{Channel}:{Address}";

    private static UserId ParseUser(string? address) =>
        Guid.TryParse(address, out var id) && id != Guid.Empty
            ? UserId.From(id)
            : throw new DomainException(NotificationErrors.InAppUserInvalid);
}
