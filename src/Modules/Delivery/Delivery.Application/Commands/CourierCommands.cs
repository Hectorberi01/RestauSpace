using Delivery.Application.Abstractions;
using Delivery.Domain;

namespace Delivery.Application.Commands;

// Fiches livreurs (delivery.couriers). Le tarif par course est dans la devise du restaurant ; nom et téléphone ne sont
// jamais journalisés.

/// <summary>Enregistre un livreur ; <paramref name="FeePerDelivery"/> null ou absent : pas de tarif par course. Renvoie son id.</summary>
public sealed record CreateCourierCommand(string? Name, string? Phone, CourierKind Kind, decimal? FeePerDelivery) : ICommand<Guid>;

public sealed class CreateCourierHandler(DeliveryWorkspace workspace, IClock clock) : ICommandHandler<CreateCourierCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateCourierCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var fee = await CourierSteps.FeeAsync(workspace, command.FeePerDelivery, cancellationToken);
        var courier = Courier.Create(workspace.CurrentTenant, command.Name, FulfillmentSteps.PhoneOrNull(command.Phone), command.Kind, fee, clock);
        workspace.Couriers.Add(courier);
        await workspace.SaveAsync(cancellationToken);
        return courier.Id.Value;
    }
}

/// <summary>Modifie la fiche ; les courses déjà confiées gardent leur tarif.</summary>
public sealed record UpdateCourierCommand(Guid CourierId, string? Name, string? Phone, CourierKind Kind, decimal? FeePerDelivery) : ICommand;

public sealed class UpdateCourierHandler(DeliveryWorkspace workspace, IClock clock) : ICommandHandler<UpdateCourierCommand>
{
    public async Task HandleAsync(UpdateCourierCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var courier = await workspace.LoadCourierAsync(command.CourierId, cancellationToken);
        var fee = await CourierSteps.FeeAsync(workspace, command.FeePerDelivery, cancellationToken);
        courier.Update(command.Name, FulfillmentSteps.PhoneOrNull(command.Phone), command.Kind, fee, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Active ou désactive le livreur ; désactivé, il n'est plus proposé mais ses courses et remises restent.</summary>
public sealed record SetCourierActiveCommand(Guid CourierId, bool Active) : ICommand;

public sealed class SetCourierActiveHandler(DeliveryWorkspace workspace, IClock clock) : ICommandHandler<SetCourierActiveCommand>
{
    public async Task HandleAsync(SetCourierActiveCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var courier = await workspace.LoadCourierAsync(command.CourierId, cancellationToken);
        if (command.Active)
        {
            courier.Activate(clock);
        }
        else
        {
            courier.Deactivate(clock);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Remise d'un livreur (delivery.remit) : solde les courses données, du même livreur et du même établissement.
/// <paramref name="Counted"/> : ce que le livreur a remis (positif) ou reçu pour ses courses (négatif). L'hôte en fait un
/// mouvement de la caisse ouverte de l'établissement. Renvoie l'id de la remise.
/// </summary>
public sealed record RecordRemittanceCommand(IReadOnlyList<Guid> FulfillmentIds, decimal Counted, string? Note = null) : ICommand<Guid>;

public sealed class RecordRemittanceHandler(DeliveryWorkspace workspace, IDeliveryOperator user, IClock clock) : ICommandHandler<RecordRemittanceCommand, Guid>
{
    public async Task<Guid> HandleAsync(RecordRemittanceCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var ids = (command.FulfillmentIds ?? []).Where(id => id != Guid.Empty).Distinct().ToList();
        DomainException.ThrowIf(ids.Count == 0, RemittanceErrors.NoDelivery);
        DomainException.ThrowIf(ids.Count > CourierRemittance.MaxDeliveries, RemittanceErrors.TooManyDeliveries);
        var currency = await workspace.CurrencyAsync(cancellationToken);
        DomainException.ThrowIf(!CourierSteps.IsValidAmount(command.Counted, currency), DeliveryApplicationErrors.RemittanceAmountInvalid);

        var deliveries = await workspace.Fulfillments.ListForUpdateAsync(workspace.CurrentTenant, ids.Select(FulfillmentId.From).ToList(), cancellationToken);
        if (deliveries.Count != ids.Count)
        {
            throw new NotFoundException(DeliveryApplicationErrors.FulfillmentNotFound);
        }

        // L'établissement est rechargé dans le tenant du contexte : jamais une remise sur l'établissement d'un autre.
        await workspace.LoadLocationAsync(deliveries[0].LocationId.Value, cancellationToken);
        var remittance = CourierRemittance.Record(workspace.CurrentTenant, deliveries, command.Counted, command.Note, user.CurrentUserId, clock);
        workspace.Remittances.Add(remittance);
        await workspace.SaveAsync(cancellationToken);
        return remittance.Id.Value;
    }
}

internal static class CourierSteps
{
    public static async Task<Money?> FeeAsync(DeliveryWorkspace workspace, decimal? amount, CancellationToken cancellationToken)
    {
        if (amount is not { } value || value == 0m)
        {
            return null;
        }

        var currency = await workspace.CurrencyAsync(cancellationToken);
        DomainException.ThrowIf(value < 0m || !IsValidAmount(value, currency), CourierErrors.FeeInvalid);
        return Money.Of(value, currency);
    }

    /// <summary>Montant signé exprimable dans la devise (décimales comprises).</summary>
    public static bool IsValidAmount(decimal value, Currency currency) =>
        Math.Abs(value) <= Money.MaxAbsoluteAmount && decimal.Round(value, currency.DecimalPlaces) == value;
}
