namespace Notifications.Domain;

/// <summary>
/// Ce qu'un prestataire sait faire, au-delà de son canal. Valeurs explicites et définitives (stockées). Correspondance
/// avec le vocabulaire des prestataires : TEXT et BODY (Push) = <see cref="Text"/> ; TEMPLATE(S) = <see cref="ProviderTemplates"/> ;
/// DEVICE = <see cref="DeviceTargeting"/>.
/// </summary>
public enum NotificationFeature
{
    /// <summary>Texte brut (corps d'un email texte, d'un SMS, d'un WhatsApp, d'un Push, d'une notification in-app).</summary>
    Text = 1,

    /// <summary>Corps HTML (email).</summary>
    Html = 2,

    /// <summary>Pièces jointes (email).</summary>
    Attachments = 3,

    /// <summary>Modèles stockés CHEZ le prestataire (modèles WhatsApp approuvés par Meta, modèles d'email ou de SMS).</summary>
    ProviderTemplates = 4,

    /// <summary>Le prestataire confirme la remise (webhook ou interrogation) : statut Delivered possible.</summary>
    DeliveryStatus = 5,

    Image = 6,

    /// <summary>Document joint à un message (WhatsApp).</summary>
    Document = 7,

    /// <summary>Titre distinct du corps (Push, in-app).</summary>
    Title = 8,

    /// <summary>Lien profond ouvrant un écran de l'application (Push, in-app).</summary>
    DeepLink = 9,

    /// <summary>Envoi à un sujet auquel des appareils sont abonnés (Push).</summary>
    Topic = 10,

    /// <summary>Envoi à un appareil précis par son jeton (Push).</summary>
    DeviceTargeting = 11,
}

public static class NotificationFeatures
{
    /// <summary>Les canaux sur lesquels une fonctionnalité a un sens. Une capacité ne peut annoncer que celles de son canal.</summary>
    public static bool IsAvailableOn(this NotificationFeature feature, NotificationChannel channel) => feature switch
    {
        NotificationFeature.Text => channel.IsKnown(),
        NotificationFeature.Html or NotificationFeature.Attachments => channel == NotificationChannel.Email,
        NotificationFeature.ProviderTemplates =>
            channel is NotificationChannel.Email or NotificationChannel.Sms or NotificationChannel.WhatsApp,
        // InApp : la plateforme connaît elle-même la lecture, ce n'est pas une fonctionnalité de prestataire.
        NotificationFeature.DeliveryStatus => channel.IsKnown() && channel.UsesExternalProvider(),
        NotificationFeature.Image => channel is NotificationChannel.WhatsApp or NotificationChannel.Push or NotificationChannel.InApp,
        NotificationFeature.Document => channel == NotificationChannel.WhatsApp,
        NotificationFeature.Title or NotificationFeature.DeepLink => channel is NotificationChannel.Push or NotificationChannel.InApp,
        NotificationFeature.Topic or NotificationFeature.DeviceTargeting => channel == NotificationChannel.Push,
        _ => false,
    };

    /// <summary>Fonctionnalités qui portent le contenu du message : une capacité en annonce au moins une.</summary>
    public static bool CarriesContent(this NotificationFeature feature) => feature is NotificationFeature.Text or NotificationFeature.Html;
}
