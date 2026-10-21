namespace Customers.Domain;

/// <summary>Port : un agrégat de consentement par (client, canal).</summary>
public interface IConsentUniquenessChecker
{
    Task<bool> IsUniqueAsync(TenantId tenantId, CustomerId customerId, CommunicationChannel channel, CancellationToken cancellationToken = default);
}
