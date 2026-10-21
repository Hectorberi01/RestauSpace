namespace Integrations.Application.Abstractions;

/// <summary>
/// Unit of work du module Integrations : une transaction sur le schéma du module, domain events écrits dans son outbox.
/// Jamais partagée avec un autre module.
/// </summary>
public interface IIntegrationsUnitOfWork : IUnitOfWork;
