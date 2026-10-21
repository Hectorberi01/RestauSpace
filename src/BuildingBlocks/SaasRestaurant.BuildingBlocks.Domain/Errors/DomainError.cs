namespace SaasRestaurant.BuildingBlocks.Domain;

/// <summary>Erreur métier. <see cref="Code"/> est un contrat stable ; <see cref="Message"/> est destiné aux humains ;
/// <see cref="Kind"/> distingue une règle enfreinte d'un conflit avec l'état existant.
/// </summary>
public sealed record DomainError(string Code, string Message, DomainErrorKind Kind = DomainErrorKind.Rule);

public enum DomainErrorKind
{
    Rule = 0,
    Conflict = 1,
}
