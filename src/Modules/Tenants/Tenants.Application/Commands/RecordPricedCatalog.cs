using Tenants.Application.Abstractions;

namespace Tenants.Application.Commands;

public sealed record RecordPricedCatalogCommand(Guid TenantId) : ICommand;

public sealed class RecordPricedCatalogHandler(IPricedCatalogLedger ledger) : ICommandHandler<RecordPricedCatalogCommand>
{
    public Task HandleAsync(RecordPricedCatalogCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ledger.RecordAsync(TenantId.From(command.TenantId), cancellationToken);
    }
}
