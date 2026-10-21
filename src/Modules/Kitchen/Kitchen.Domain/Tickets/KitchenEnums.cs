namespace Kitchen.Domain;

public enum KitchenTicketStatus
{
    Queued = 1,
    InProgress = 2,
    Ready = 3,
    Completed = 4,
    Cancelled = 5,
}

public enum KitchenTicketItemStatus
{
    Queued = 1,
    Preparing = 2,
    Ready = 3,
}

/// <summary>Ordre dans lequel la cuisine doit traiter les tickets ; la valeur la plus haute d'abord.</summary>
public enum KitchenPriority
{
    Normal = 1,
    High = 2,
    Rush = 3,
}

/// <summary>
/// Service d'un plat dans le repas. <see cref="Direct"/> part en cuisine tout de suite (boissons, plat unique) ; pour une
/// commande sur place, les services suivant le premier attendent d'être envoyés par la salle.
/// </summary>
public enum KitchenCourse
{
    Direct = 1,
    Starter = 2,
    Main = 3,
    Dessert = 4,
}
