namespace Reporting.Domain;

/// <summary>
/// Vue locale d'un fichier stocké par le module Files : son id et le tenant qui le possède. Construite par
/// l'Application à partir du fichier stocké lui-même (chargé sous isolation du tenant), jamais d'une saisie utilisateur.
/// </summary>
public sealed record ExportedFileReference(TenantId TenantId, FileId FileId)
{
    public TenantId TenantId { get; } = Guard.NotEmpty(TenantId);
}
