namespace Kitchen.Application.Abstractions;

/// <summary>
/// Unit of work du module Kitchen : une transaction sur le schéma propre au module, domain events écrits dans son
/// outbox. Jamais partagée avec un autre module.
/// </summary>
public interface IKitchenUnitOfWork : IUnitOfWork;
