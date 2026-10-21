namespace SaasRestaurant.BuildingBlocks.Application;

/// <summary>Une lecture. Ne change jamais l'état, ne lève jamais de domain event.</summary>
public interface IQuery<TResult>;

public interface IQueryHandler<in TQuery, TResult>where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
}
