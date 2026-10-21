using Tenants.Application.Abstractions;
using Tenants.Domain.Prospects;

namespace Tenants.Infrastructure.Persistence;

internal sealed class DemoRequestRepository(TenantsDbContext context) : IDemoRequestRepository
{
    public Task<DemoRequest?> FindAsync(DemoRequestId id, CancellationToken cancellationToken = default) =>
        context.DemoRequests.SingleOrDefaultAsync(request => request.Id == id, cancellationToken);

    public async Task<IReadOnlyList<DemoRequest>> ListAsync(
        DemoRequestStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.DemoRequests.AsNoTracking();
        if (status is { } wanted)
        {
            query = query.Where(request => request.Status == wanted);
        }

        return await query
            .OrderByDescending(request => request.SubmittedAtUtc)
            .ThenByDescending(request => request.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public void Add(DemoRequest request) => context.DemoRequests.Add(request);

    public void Remove(DemoRequest request) => context.DemoRequests.Remove(request);
}
