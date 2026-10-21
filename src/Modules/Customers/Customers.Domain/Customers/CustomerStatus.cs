namespace Customers.Domain;

public enum CustomerStatus
{
    Active = 1,
    Anonymized = 2,

    /// <summary>Doublon fusionné dans une autre fiche : plus aucune donnée personnelle, seulement le lien vers elle.</summary>
    Merged = 3,
}
