namespace SaasRestaurant.BuildingBlocks.Application;

/// <summary>Levée quand un appelant doit ralentir. <see cref="RetryAfter"/> indique pendant combien de temps.</summary>
public sealed class TooManyRequestsException : Exception
{
    public TooManyRequestsException(DomainError error, TimeSpan retryAfter) : base(error.Message)
    {
        Error = error;
        RetryAfter = retryAfter;
    }

    public DomainError Error { get; }

    public TimeSpan RetryAfter { get; }

    public string Code => Error.Code;
}
