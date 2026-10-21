namespace Notifications.Domain;

/// <summary>
/// Le choix d'une personne pour une finalité (transactionnel ou marketing) sur un canal : accepté ou refusé, quand et
/// depuis où. Absente = accepté (valeur par défaut). Ligne d'un <see cref="NotificationPreference"/>.
/// </summary>
public sealed class ChannelPreference : ValueObject
{
    internal ChannelPreference(NotificationPurpose purpose, NotificationChannel channel, bool isAllowed, DateTimeOffset changedAtUtc, PreferenceSource source)
    {
        Purpose = purpose;
        Channel = channel;
        IsAllowed = isAllowed;
        ChangedAtUtc = changedAtUtc;
        Source = source;
    }

    // Réservé à l'ORM.
    private ChannelPreference()
    {
    }

    public NotificationPurpose Purpose { get; private init; }
    public NotificationChannel Channel { get; private init; }
    public bool IsAllowed { get; private init; }
    public DateTimeOffset ChangedAtUtc { get; private init; }
    public PreferenceSource Source { get; private init; } = null!;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Purpose;
        yield return Channel;
        yield return IsAllowed;
        yield return ChangedAtUtc;
        yield return Source;
    }
}
