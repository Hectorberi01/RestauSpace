namespace Notifications.Domain;

/// <summary>
/// Le canal d'une configuration proposé dans UN établissement : actif ou non et son rang (<see cref="Priority"/>, le plus
/// petit d'abord) quand plusieurs prestataires du même canal servent l'établissement (le suivant sert de repli). Canal et
/// prestataire sont recopiés de la configuration (lecture par établissement sans charger les configurations). Entité
/// enfant : seule la racine la crée et la modifie.
/// </summary>
public sealed class NotificationChannelAssignment : Entity<NotificationChannelAssignmentId>
{
    public const int MaxPriority = 1000;

    private NotificationChannelAssignment(
        NotificationChannelAssignmentId id, LocationId locationId, NotificationChannel channel, NotificationProviderId providerId, int priority, bool isEnabled)
        : base(id)
    {
        LocationId = locationId;
        Channel = channel;
        ProviderId = providerId;
        Priority = priority;
        IsEnabled = isEnabled;
    }

    // Réservé à l'ORM.
    private NotificationChannelAssignment()
    {
    }

    public LocationId LocationId { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public NotificationProviderId ProviderId { get; private set; }

    public bool IsEnabled { get; private set; }

    public int Priority { get; private set; }

    internal static NotificationChannelAssignment Create(
        LocationId locationId, NotificationChannel channel, NotificationProviderId providerId, int priority, bool isEnabled)
    {
        ValidatePriority(priority);
        return new NotificationChannelAssignment(NotificationChannelAssignmentId.New(), locationId, channel, providerId, priority, isEnabled);
    }

    /// <summary>Vrai si quelque chose a changé.</summary>
    internal bool Update(int priority, bool isEnabled)
    {
        ValidatePriority(priority);
        if (Priority == priority && IsEnabled == isEnabled)
        {
            return false;
        }

        Priority = priority;
        IsEnabled = isEnabled;
        return true;
    }

    /// <summary>La configuration a changé de prestataire (même canal) : l'affectation suit.</summary>
    internal void MoveTo(NotificationProviderId providerId) => ProviderId = providerId;

    private static void ValidatePriority(int priority) =>
        DomainException.ThrowIf(priority is < 0 or > MaxPriority, NotificationConfigurationErrors.PriorityOutOfRange);
}
