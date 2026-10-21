namespace Notifications.Application.Abstractions;

/// <summary>
/// Unit of work du module Notifications : une transaction sur le schéma propre au module, domain events écrits dans
/// son outbox. Jamais partagée avec un autre module.
/// </summary>
public interface INotificationsUnitOfWork : IUnitOfWork;
