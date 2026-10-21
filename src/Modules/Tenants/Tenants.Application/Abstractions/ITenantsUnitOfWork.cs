namespace Tenants.Application.Abstractions;

/// <summary>Unit of work du module Tenants. La concurrence optimiste sur <c>Tenant</c> est appliquée ici.</summary>
public interface ITenantsUnitOfWork : IUnitOfWork;
