namespace Promotions.Domain;

public enum PromotionStatus
{
    Draft = 1,
    Active = 2,
    Paused = 3,
    Expired = 4,
}

public enum BenefitType
{
    Percentage = 1,
    FixedAmount = 2,
}

/// <summary>Copie locale des canaux de commande auxquels une promotion peut être restreinte.</summary>
public enum OrderType
{
    DineIn = 1,
    Takeaway = 2,
    Delivery = 3,
}

/// <summary>Décide quels changements de règles sont acceptés une fois qu'une promotion a déjà été appliquée.</summary>
public enum RuleChangePolicy
{
    /// <summary>Seulement les changements sans risque pour les utilisateurs passés ou actuels : fin de période plus tardive,
    /// limite d'utilisation plus haute (ou supprimée).</summary>
    NonDestructiveOnly = 1,

    /// <summary>Aucun changement de règle une fois utilisée.</summary>
    Frozen = 2,

    /// <summary>Tout changement de règle valide, dérogation explicite.</summary>
    Unrestricted = 3,
}
