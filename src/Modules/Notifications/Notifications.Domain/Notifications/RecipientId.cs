namespace Notifications.Domain;

public enum RecipientKind
{
    /// <summary>Un client du restaurant (module Customers).</summary>
    Customer = 1,

    /// <summary>Un utilisateur connecté : membre de l'équipe, gérant, propriétaire (module Identity).</summary>
    User = 2,
}

/// <summary>
/// QUI reçoit une notification (et à qui appartiennent des préférences) : un client OU un utilisateur, par son seul id.
/// « Utilisateur » couvre employé, gérant, propriétaire et administrateur de la plateforme : leur rôle est connu de Staff
/// et d'Identity, pas d'ici. Aucune adresse : où joindre la personne est dans <see cref="NotificationRecipient"/>.
/// </summary>
public sealed class RecipientId : ValueObject
{
    private RecipientId(RecipientKind kind, Guid id)
    {
        Kind = kind;
        Id = id;
    }

    public RecipientKind Kind { get; }

    public Guid Id { get; }

    public bool IsCustomer => Kind == RecipientKind.Customer;

    /// <summary>Le client désigné, ou null pour un utilisateur.</summary>
    public CustomerId? ToCustomerId() => IsCustomer ? CustomerId.From(Id) : null;

    public static RecipientId ForCustomer(CustomerId customerId) => new(RecipientKind.Customer, Guard.NotEmpty(customerId).Value);

    public static RecipientId ForUser(UserId userId) => new(RecipientKind.User, Guard.NotEmpty(userId).Value);

    /// <summary>Relit un propriétaire stocké.</summary>
    public static RecipientId Of(RecipientKind kind, Guid id) => kind switch
    {
        RecipientKind.Customer => ForCustomer(CustomerId.From(id)),
        RecipientKind.User => ForUser(UserId.From(id)),
        _ => throw new DomainException(NotificationErrors.RecipientKindUnknown),
    };

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Kind;
        yield return Id;
    }

    public override string ToString() => $"{Kind}:{Id}";
}
