namespace Notifications.Domain;

public enum NotificationPurpose
{
    /// <summary>Message que le destinataire attend dans le cadre d'un service (confirmation, reçu, sécurité).</summary>
    Transactional = 1,

    /// <summary>Message promotionnel : uniquement avec le consentement du destinataire pour le canal.</summary>
    Marketing = 2,
}

/// <summary>
/// Réponse du module Customers à « ce client a-t-il consenti au marketing sur ce canal ? », lue par l'Application
/// juste avant de créer la notification, puis de nouveau juste avant chaque tentative d'envoi.
/// Le module Notifications n'interroge jamais Customers lui-même.
/// </summary>
public sealed class ConsentProof : ValueObject
{
    private ConsentProof(CustomerId customerId, CommunicationChannel channel, bool isGranted)
    {
        CustomerId = customerId;
        Channel = channel;
        IsGranted = isGranted;
    }

    /// <summary>Le client pour lequel le consentement a été lu.</summary>
    public CustomerId CustomerId { get; }

    public CommunicationChannel Channel { get; }

    public bool IsGranted { get; }

    public static ConsentProof Granted(CustomerId customerId, CommunicationChannel channel) => Create(customerId, channel, true);

    public static ConsentProof Denied(CustomerId customerId, CommunicationChannel channel) => Create(customerId, channel, false);

    private static ConsentProof Create(CustomerId customerId, CommunicationChannel channel, bool isGranted)
    {
        Guard.NotEmpty(customerId);
        DomainException.ThrowIf(!Enum.IsDefined(channel), NotificationErrors.ChannelInvalid);
        return new ConsentProof(customerId, channel, isGranted);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return CustomerId;
        yield return Channel;
        yield return IsGranted;
    }
}
