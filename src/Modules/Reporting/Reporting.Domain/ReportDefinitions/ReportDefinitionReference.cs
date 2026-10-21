namespace Reporting.Domain;

/// <summary>
/// Référence à une <see cref="ReportDefinition"/> portant son tenant propriétaire. Seul
/// <see cref="ReportDefinition.ToReference"/> peut la construire : un export ne peut donc pas être rattaché à la
/// définition d'un autre tenant en forgeant la paire.
/// </summary>
public sealed record ReportDefinitionReference
{
    internal ReportDefinitionReference(TenantId tenantId, ReportDefinitionId reportDefinitionId)
    {
        TenantId = tenantId;
        ReportDefinitionId = reportDefinitionId;
    }

    public TenantId TenantId { get; }

    public ReportDefinitionId ReportDefinitionId { get; }
}
