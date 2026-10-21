namespace Notifications.Domain;

/// <summary>
/// Canal par lequel une notification atteint son destinataire. Valeurs EXPLICITES et définitives : elles sont stockées
/// en base et dans les événements ; un nouveau canal prend la valeur suivante, jamais celle d'un canal existant.
/// Email, Sms, WhatsApp et Push ont les mêmes valeurs que <see cref="CommunicationChannel"/> du noyau (canaux sur
/// lesquels un client peut consentir au marketing, module Customers) ; InApp n'existe qu'ici.
/// </summary>
public enum NotificationChannel
{
    Email = 1,
    Sms = 2,
    WhatsApp = 3,
    Push = 4,

    /// <summary>Affichée dans le tableau de bord ou l'application : aucun prestataire externe, aucun consentement marketing.</summary>
    InApp = 5,
}

/// <summary>Règles attachées au canal, au même endroit pour ne jamais les réécrire dans le code (pas de chaînes magiques).</summary>
public static class NotificationChannels
{
    /// <summary>Tous les canaux connus, dans l'ordre de leurs valeurs.</summary>
    public static IReadOnlyList<NotificationChannel> All { get; } = Array.AsReadOnly(Enum.GetValues<NotificationChannel>());

    public static bool IsKnown(this NotificationChannel channel) => Enum.IsDefined(channel);

    /// <summary>Le canal passe par un prestataire extérieur (faux seulement pour InApp, livré par la plateforme elle-même).</summary>
    public static bool UsesExternalProvider(this NotificationChannel channel) => channel != NotificationChannel.InApp;

    /// <summary>
    /// Le canal de consentement correspondant (Customers), ou null pour un canal sans consentement marketing (InApp).
    /// </summary>
    public static CommunicationChannel? ToConsentChannel(this NotificationChannel channel) => channel switch
    {
        NotificationChannel.Email => CommunicationChannel.Email,
        NotificationChannel.Sms => CommunicationChannel.Sms,
        NotificationChannel.WhatsApp => CommunicationChannel.WhatsApp,
        NotificationChannel.Push => CommunicationChannel.Push,
        _ => null,
    };

    /// <summary>Le canal de notification d'un canal de communication du noyau.</summary>
    public static NotificationChannel FromCommunicationChannel(CommunicationChannel channel) => channel switch
    {
        CommunicationChannel.Email => NotificationChannel.Email,
        CommunicationChannel.Sms => NotificationChannel.Sms,
        CommunicationChannel.WhatsApp => NotificationChannel.WhatsApp,
        CommunicationChannel.Push => NotificationChannel.Push,
        _ => throw new DomainException(NotificationProviderErrors.ChannelUnknown),
    };

    /// <summary>Lève <c>notifications.channel.unknown</c> pour une valeur hors liste (jamais d'exception technique).</summary>
    public static NotificationChannel Require(NotificationChannel channel)
    {
        DomainException.ThrowIf(!channel.IsKnown(), NotificationProviderErrors.ChannelUnknown);
        return channel;
    }
}
