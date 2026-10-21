namespace Billing.Application.Abstractions;

/// <summary>
/// Unit of work du module Billing : une transaction sur le schéma propre au module, domain events écrits dans son outbox.
/// Jamais partagée avec un autre module.
/// </summary>
public interface IBillingUnitOfWork : IUnitOfWork;
