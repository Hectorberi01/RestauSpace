namespace Loyalty.Domain;

/// <summary>Port : « ce client n'a-t-il pas encore de compte ? » ; l'index unique (tenant, client) reste le garde-fou final.</summary>
public interface ILoyaltyAccountUniquenessChecker
{
    Task<bool> IsUniqueAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default);
}
