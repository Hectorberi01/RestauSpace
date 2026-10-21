namespace Customers.Domain;

/// <summary>Motif de retrait d'un consentement. Liste fermée exprès : un texte libre pourrait porter des données personnelles.</summary>
public enum WithdrawalReason
{
    CustomerRequest = 1,
    UnsubscribeLink = 2,
    InvalidAddress = 3,
    CustomerAnonymized = 4,
    Other = 5,
}
