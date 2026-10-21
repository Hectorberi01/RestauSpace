namespace Payments.Domain;

public enum PaymentStatus
{
    Pending = 1,
    Authorized = 2,
    Captured = 3,
    Failed = 4,
    Cancelled = 5,
    PartiallyRefunded = 6,
    Refunded = 7,
}

public enum PaymentAttemptStatus
{
    Pending = 1,
    Succeeded = 2,
    Failed = 3,
}

public enum RefundStatus
{
    Requested = 1,
    Succeeded = 2,
    Failed = 3,
}

/// <summary>Résultat validé, remonté par le prestataire, d'un remboursement.</summary>
public enum RefundOutcome
{
    Succeeded = 1,
    Failed = 2,
}

/// <summary>Ce que le paiement règle.</summary>
public enum PaymentPurpose
{
    /// <summary>Une commande (addition).</summary>
    Order = 1,

    /// <summary>
    /// L'acompte d'une réservation : rattaché à la réservation tant qu'il n'est pas déduit d'une addition
    /// (<see cref="Payment.ApplyToOrder"/>).
    /// </summary>
    ReservationDeposit = 2,
}
