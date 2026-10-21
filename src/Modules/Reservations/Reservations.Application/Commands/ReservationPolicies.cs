using Reservations.Domain;

namespace Reservations.Application.Commands;

/// <summary>
/// Remplace la politique de réservation d'un établissement du tenant courant. Durées en minutes, horizon en jours ;
/// les bornes sont celles de <see cref="ReservationPolicy.Create"/>. Les réservations existantes ne sont pas revues.
/// </summary>
public sealed record SetReservationPolicyCommand(
    Guid LocationId,
    int MinLeadTimeMinutes,
    int MaxAdvanceDays,
    int DefaultDurationMinutes,
    int CancellationCutoffMinutes,
    int MinDurationMinutes,
    int MaxDurationMinutes,
    int MaxPartySize,
    bool AcceptsOnlineBookings = false) : ICommand;

public sealed class SetReservationPolicyHandler(ReservationsWorkspace workspace) : ICommandHandler<SetReservationPolicyCommand>
{
    public async Task HandleAsync(SetReservationPolicyCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var location = await workspace.RequireLocationAsync(tenant, command.LocationId, cancellationToken);

        // Toute durée en minutes tient dans un TimeSpan ; l'horizon en jours est ramené juste au-delà du plafond, pour que le
        // domaine le refuse avec son erreur au lieu d'un dépassement de TimeSpan.
        var maxAdvanceDays = Math.Clamp(command.MaxAdvanceDays, -1, (int)ReservationPolicy.MaxAdvanceCeiling.TotalDays + 1);
        var policy = ReservationPolicy.Create(
            TimeSpan.FromMinutes(command.MinLeadTimeMinutes),
            TimeSpan.FromDays(maxAdvanceDays),
            TimeSpan.FromMinutes(command.DefaultDurationMinutes),
            TimeSpan.FromMinutes(command.CancellationCutoffMinutes),
            TimeSpan.FromMinutes(command.MinDurationMinutes),
            TimeSpan.FromMinutes(command.MaxDurationMinutes),
            command.MaxPartySize,
            command.AcceptsOnlineBookings);

        await workspace.Policies.SetAsync(tenant, location.LocationId, policy, cancellationToken);
        await workspace.SaveAsync(cancellationToken);
    }
}
