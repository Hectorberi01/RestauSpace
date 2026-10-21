using Tenants.Domain.Prospects;

namespace Tenants.Application.Abstractions;

/// <summary>Demandes de démonstration du site vitrine (plateforme, sans tenant).</summary>
public interface IDemoRequestRepository
{
    Task<DemoRequest?> FindAsync(DemoRequestId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, de la plus récente à la plus ancienne.</summary>
    Task<IReadOnlyList<DemoRequest>> ListAsync(DemoRequestStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    void Add(DemoRequest request);

    void Remove(DemoRequest request);
}
