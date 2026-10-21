namespace Loyalty.Domain;

public enum LoyaltyAccountStatus
{
    Active = 1,
    Suspended = 2,

    /// <summary>Terminal : le client est parti ou a été anonymisé ; le solde restant est perdu.</summary>
    Closed = 3,
}

public enum LoyaltyTransactionType
{
    Earn = 1,
    Redeem = 2,
    Adjust = 3,
    Expire = 4,

    /// <summary>Annule une écriture Earn ou Redeem antérieure (commande annulée ou remboursée) ; liée à celle-ci.</summary>
    Reversal = 5,

    /// <summary>Ramène le solde à zéro à la clôture du compte.</summary>
    Closure = 6,
}

/// <summary>Effet d'une écriture du grand livre sur le solde.</summary>
public enum LedgerDirection
{
    Credit = 1,
    Debit = 2,
}
