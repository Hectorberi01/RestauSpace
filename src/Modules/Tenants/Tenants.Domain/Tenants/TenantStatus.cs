namespace Tenants.Domain.Tenants;

public enum TenantStatus
{
    Trial = 1,
    Active = 2,

    /// <summary>Lisible, écritures métier refusées (voir <see cref="Tenant.CanAcceptBusinessWrites"/>).</summary>
    Suspended = 3,

    /// <summary>Terminal.</summary>
    Closed = 4,
}

public enum TenantSuspensionReason
{
    PaymentOverdue = 1,
    TrialExpired = 2,
    PolicyViolation = 3,
    RequestedByTenant = 4,
    Other = 5,
}
