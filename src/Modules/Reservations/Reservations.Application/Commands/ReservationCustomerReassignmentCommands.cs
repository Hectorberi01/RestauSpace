using Reservations.Application.Abstractions;

namespace Reservations.Application.Commands;

/// <summary>
/// customers.CustomerMerged : les reservations du doublon passent à la fiche qui l'a absorbé, pour le tenant du contexte.
/// Rejouable : une seconde fois, il ne reste rien à rattacher.
/// </summary>
public sealed record ReassignReservationsToCustomerCommand(Guid FromCustomerId, Guid ToCustomerId) : ICommand<int>;

public sealed class ReassignReservationsToCustomerHandler(ITenantContext tenantContext, IReservationCustomerReassignment reassignment)
    : ICommandHandler<ReassignReservationsToCustomerCommand, int>
{
    public Task<int> HandleAsync(ReassignReservationsToCustomerCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.FromCustomerId == Guid.Empty || command.ToCustomerId == Guid.Empty || command.FromCustomerId == command.ToCustomerId
            ? Task.FromResult(0)
            : reassignment.ReassignAsync(
                tenantContext.RequireTenant(), CustomerId.From(command.FromCustomerId), CustomerId.From(command.ToCustomerId), cancellationToken);
    }
}
