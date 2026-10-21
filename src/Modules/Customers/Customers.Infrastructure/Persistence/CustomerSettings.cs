using Customers.Domain;

namespace Customers.Infrastructure.Persistence;

/// <summary>Réglages d'un restaurant pour ce module (une ligne par tenant, absente tant que rien n'est réglé).</summary>
internal sealed class CustomerSettings : ITenantOwned
{
    public TenantId TenantId { get; set; }

    public ContactRequirement ContactRequirement { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}
