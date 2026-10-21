using Orders.Application.Abstractions;

namespace Orders.Application.Commands;

/// <summary>
/// customers.CustomerMerged : les orders du doublon passent à la fiche qui l'a absorbé, pour le tenant du contexte.
/// Rejouable : une seconde fois, il ne reste rien à rattacher.
/// </summary>
public sealed record ReassignOrdersToCustomerCommand(Guid FromCustomerId, Guid ToCustomerId) : ICommand<int>;

public sealed class ReassignOrdersToCustomerHandler(ITenantContext tenantContext, IOrderCustomerReassignment reassignment)
    : ICommandHandler<ReassignOrdersToCustomerCommand, int>
{
    public Task<int> HandleAsync(ReassignOrdersToCustomerCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.FromCustomerId == Guid.Empty || command.ToCustomerId == Guid.Empty || command.FromCustomerId == command.ToCustomerId
            ? Task.FromResult(0)
            : reassignment.ReassignAsync(
                tenantContext.RequireTenant(), CustomerId.From(command.FromCustomerId), CustomerId.From(command.ToCustomerId), cancellationToken);
    }
}
