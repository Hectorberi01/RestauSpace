namespace Notifications.Domain;

/// <summary>
/// Ordre de traitement des envois : un OTP passe avant une confirmation, une campagne marketing après tout le reste (elle
/// ne bloque jamais un message urgent). Valeurs explicites et définitives (stockées) : la plus petite passe d'abord.
/// </summary>
public enum NotificationPriority
{
    Critical = 1,
    High = 2,
    Normal = 3,
    Low = 4,
}
