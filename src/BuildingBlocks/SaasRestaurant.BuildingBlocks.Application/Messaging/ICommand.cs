namespace SaasRestaurant.BuildingBlocks.Application;

/// <summary>Une demande de changement d'état. Une commande, un handler, une transaction.</summary>
public interface ICommand;

/// <summary>Une commande qui renvoie une valeur, typiquement l'id de ce qu'elle a créé.</summary>
public interface ICommand<TResult>;

public interface ICommandHandler<in TCommand>
where TCommand : ICommand
{
    Task HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken = default);
}
