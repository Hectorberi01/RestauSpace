namespace Tenants.Application;

public static class TenantApplicationErrors
{
    public static readonly DomainError NotFound = new("tenants.tenant.not_found", "The tenant does not exist.");
    public static readonly DomainError ConcurrentUpdate = new("tenants.tenant.concurrent_update", "The tenant was changed by someone else; reload it and try again.");
}
