namespace Payments.Application.Abstractions;

/// <summary>
/// Unit of work du module Payments : une transaction sur le schéma propre du module, domain events écrits dans son outbox.
/// Jamais partagée avec un autre module.
/// </summary>
public interface IPaymentsUnitOfWork : IUnitOfWork;
