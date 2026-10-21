namespace SaasRestaurant.BuildingBlocks.Domain;

/// <summary>Levée quand une règle métier est enfreinte. Porte un code d'erreur stable.</summary>
public sealed class DomainException : Exception
{
    public DomainException(DomainError error) : base(error.Message) => Error = error;

    /// <summary>Refus métier causé par une erreur technique (ex. un prestataire qui refuse) : la cause reste dans les logs.</summary>
    public DomainException(DomainError error, Exception? innerException) : base(error.Message, innerException) => Error = error;

    public DomainError Error { get; }

    public string Code => Error.Code;

    public static void ThrowIf(bool ruleIsBroken, DomainError error)
    {
        if (ruleIsBroken)
        {
            throw new DomainException(error);
        }
    }
}
