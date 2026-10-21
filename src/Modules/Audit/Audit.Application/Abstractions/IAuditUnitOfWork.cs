namespace Audit.Application.Abstractions;

/// <summary>
/// Unit of work du module Audit : une transaction sur le schéma propre au module, domain events écrits dans son outbox.
/// Jamais partagée avec un autre module.
/// </summary>
public interface IAuditUnitOfWork : IUnitOfWork;
