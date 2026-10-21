using Delivery.Application.Abstractions;
using Delivery.Domain;

namespace Delivery.Application.Commands;

// Fiches de retrait et de livraison (delivery.operate). La commande est chargée dans le tenant du CONTEXTE (404 sinon) ;
// une commande n'a qu'une fiche active. Les coordonnées du client ne sont jamais journalisées.

/// <summary>Fiche de retrait d'une commande à emporter. <paramref name="PickupAt"/> facultatif. Renvoie l'id de la fiche.</summary>
public sealed record CreatePickupCommand(Guid OrderId, string? CustomerName, string? CustomerPhone = null, DateTimeOffset? PickupAt = null)
    : ICommand<Guid>;

public sealed class CreatePickupHandler(DeliveryWorkspace workspace, IDeliveryOrders orders, IClock clock) : ICommandHandler<CreatePickupCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreatePickupCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var order = await FulfillmentSteps.LoadNewOrderAsync(workspace, orders, command.OrderId, cancellationToken);
        var settings = await workspace.LoadOrCreateSettingsAsync(order.LocationId.Value, cancellationToken);
        var fulfillment = Fulfillment.ForPickup(
            workspace.CurrentTenant, order, settings, command.CustomerName, FulfillmentSteps.PhoneOrNull(command.CustomerPhone), command.PickupAt, clock);
        workspace.Fulfillments.Add(fulfillment);
        await workspace.SaveAsync(cancellationToken);
        return fulfillment.Id.Value;
    }
}

/// <summary>
/// Fiche de livraison d'une commande en livraison, dans une zone active de son établissement ; <paramref name="DeliverAt"/>
/// facultatif (livraison programmée). Renvoie l'id de la fiche.
/// </summary>
public sealed record CreateDeliveryCommand(
    Guid OrderId,
    Guid ZoneId,
    string? CustomerName,
    string? CustomerPhone,
    string? AddressLine,
    string? Landmark = null,
    string? Instructions = null,
    DateTimeOffset? DeliverAt = null) : ICommand<Guid>;

public sealed class CreateDeliveryHandler(DeliveryWorkspace workspace, IDeliveryOrders orders, IClock clock) : ICommandHandler<CreateDeliveryCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateDeliveryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var order = await FulfillmentSteps.LoadNewOrderAsync(workspace, orders, command.OrderId, cancellationToken);
        var settings = await workspace.LoadOrCreateSettingsAsync(order.LocationId.Value, cancellationToken);
        var zoneId = command.ZoneId == Guid.Empty ? throw new DomainException(FulfillmentErrors.ZoneUnavailable) : DeliveryZoneId.From(command.ZoneId);
        var fulfillment = Fulfillment.ForDelivery(
            workspace.CurrentTenant,
            order,
            settings,
            zoneId,
            command.CustomerName,
            FulfillmentSteps.PhoneOrNull(command.CustomerPhone),
            command.AddressLine,
            command.Landmark,
            command.Instructions,
            clock,
            command.DeliverAt);
        workspace.Fulfillments.Add(fulfillment);
        await workspace.SaveAsync(cancellationToken);
        return fulfillment.Id.Value;
    }
}

public enum FulfillmentStep
{
    ReadyForPickup = 1,
    PickedUp = 2,
    Depart = 3,
    Delivered = 4,
    Failed = 5,
    Retry = 6,
    Cancel = 7,
}

/// <summary>Fait avancer la fiche d'une étape ; <paramref name="Reason"/> pour un échec (obligatoire) ou une annulation.</summary>
public sealed record AdvanceFulfillmentCommand(Guid FulfillmentId, FulfillmentStep Step, string? Reason = null) : ICommand;

public sealed class AdvanceFulfillmentHandler(DeliveryWorkspace workspace, IDeliveryPayments payments, IClock clock) : ICommandHandler<AdvanceFulfillmentCommand>
{
    public async Task HandleAsync(AdvanceFulfillmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var fulfillment = await workspace.LoadFulfillmentAsync(command.FulfillmentId, cancellationToken);
        switch (command.Step)
        {
            case FulfillmentStep.ReadyForPickup:
                fulfillment.MarkReadyForPickup(clock);
                break;
            case FulfillmentStep.PickedUp:
                fulfillment.ConfirmPickup(clock);
                break;
            case FulfillmentStep.Depart:
                fulfillment.Depart(clock, await FulfillmentSteps.AmountToCollectAsync(workspace, payments, fulfillment, cancellationToken));
                break;
            case FulfillmentStep.Delivered:
                fulfillment.ConfirmDelivery(clock);
                break;
            case FulfillmentStep.Failed:
                fulfillment.MarkFailed(command.Reason, clock);
                break;
            case FulfillmentStep.Retry:
                fulfillment.Retry(clock);
                break;
            case FulfillmentStep.Cancel:
                fulfillment.Cancel(command.Reason, clock);
                break;
            default:
                throw new DomainException(FulfillmentErrors.InvalidTransition);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Confie la livraison à un livreur enregistré (<paramref name="CourierId"/>, son tarif est figé sur la fiche) ou saisi
/// à la main (nom obligatoire, téléphone E.164 facultatif).
/// </summary>
public sealed record AssignCourierCommand(Guid FulfillmentId, string? Name, string? Phone = null, Guid? CourierId = null) : ICommand;

public sealed class AssignCourierHandler(DeliveryWorkspace workspace, IClock clock) : ICommandHandler<AssignCourierCommand>
{
    public async Task HandleAsync(AssignCourierCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var fulfillment = await workspace.LoadFulfillmentAsync(command.FulfillmentId, cancellationToken);
        if (command.CourierId is { } courierId)
        {
            fulfillment.AssignCourier(await workspace.LoadCourierAsync(courierId, cancellationToken), clock);
        }
        else
        {
            fulfillment.AssignCourier(command.Name, FulfillmentSteps.PhoneOrNull(command.Phone), clock);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Livraison remise au client. S'il restait à payer au départ, <paramref name="Method"/> dit comment le client a payé le
/// livreur et <paramref name="Collected"/> combien (tout le montant dû si omis) ; l'hôte enregistre le paiement sur la commande.
/// </summary>
public sealed record ConfirmDeliveryCommand(Guid FulfillmentId, decimal? Collected = null, CollectionMethod? Method = null) : ICommand;

public sealed class ConfirmDeliveryHandler(DeliveryWorkspace workspace, IDeliveryPayments payments, IClock clock) : ICommandHandler<ConfirmDeliveryCommand>
{
    public async Task HandleAsync(ConfirmDeliveryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var fulfillment = await workspace.LoadFulfillmentAsync(command.FulfillmentId, cancellationToken);
        if (fulfillment.AmountToCollect is { } due && command.Method is { } method && Enum.IsDefined(method) && fulfillment.FeeCurrency is { } currency)
        {
            var amount = command.Collected ?? due;
            DomainException.ThrowIf(
                amount > 0m && CourierSteps.IsValidAmount(amount, currency)
                && !await payments.AcceptsAsync(workspace.CurrentTenant, fulfillment.LocationId, method, Money.Of(amount, currency), cancellationToken),
                DeliveryApplicationErrors.CollectionNotAccepted);
        }

        fulfillment.ConfirmDelivery(clock, command.Collected, command.Method);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record UpdateFulfillmentContactCommand(Guid FulfillmentId, string? CustomerName, string? CustomerPhone) : ICommand;

public sealed class UpdateFulfillmentContactHandler(DeliveryWorkspace workspace) : ICommandHandler<UpdateFulfillmentContactCommand>
{
    public async Task HandleAsync(UpdateFulfillmentContactCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var fulfillment = await workspace.LoadFulfillmentAsync(command.FulfillmentId, cancellationToken);
        fulfillment.UpdateContact(command.CustomerName, FulfillmentSteps.PhoneOrNull(command.CustomerPhone));
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record ChangeDeliveryAddressCommand(Guid FulfillmentId, string? AddressLine, string? Landmark = null, string? Instructions = null) : ICommand;

public sealed class ChangeDeliveryAddressHandler(DeliveryWorkspace workspace) : ICommandHandler<ChangeDeliveryAddressCommand>
{
    public async Task HandleAsync(ChangeDeliveryAddressCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var fulfillment = await workspace.LoadFulfillmentAsync(command.FulfillmentId, cancellationToken);
        fulfillment.ChangeAddress(command.AddressLine, command.Landmark, command.Instructions);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Pour l'hôte (orders.OrderReady) : la fiche de retrait de la commande passe « prête à retirer ». Sans fiche, ou fiche
/// déjà avancée, rien ne change. Renvoie vrai si la fiche a changé.
/// </summary>
public sealed record MarkOrderReadyForPickupCommand(Guid OrderId) : ICommand<bool>;

public sealed class MarkOrderReadyForPickupHandler(DeliveryWorkspace workspace, IClock clock) : ICommandHandler<MarkOrderReadyForPickupCommand, bool>
{
    public async Task<bool> HandleAsync(MarkOrderReadyForPickupCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OrderId == Guid.Empty)
        {
            return false;
        }

        var fulfillment = await workspace.Fulfillments.FindActiveForOrderAsync(workspace.CurrentTenant, OrderId.From(command.OrderId), cancellationToken);
        if (fulfillment is not { Mode: FulfillmentMode.Pickup, Status: FulfillmentStatus.Pending })
        {
            return false;
        }

        fulfillment.MarkReadyForPickup(clock);
        await workspace.SaveAsync(cancellationToken);
        return true;
    }
}

/// <summary>
/// Pour l'hôte (orders.OrderCancelled) : la fiche active de la commande est annulée. Idempotent. Renvoie vrai si la
/// fiche a changé.
/// </summary>
public sealed record CancelOrderFulfillmentCommand(Guid OrderId, string? Reason) : ICommand<bool>;

public sealed class CancelOrderFulfillmentHandler(DeliveryWorkspace workspace, IClock clock) : ICommandHandler<CancelOrderFulfillmentCommand, bool>
{
    public async Task<bool> HandleAsync(CancelOrderFulfillmentCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.OrderId == Guid.Empty)
        {
            return false;
        }

        var fulfillment = await workspace.Fulfillments.FindActiveForOrderAsync(workspace.CurrentTenant, OrderId.From(command.OrderId), cancellationToken);
        if (fulfillment is null || fulfillment.IsClosed)
        {
            return false;
        }

        var reason = string.IsNullOrWhiteSpace(command.Reason) ? "Commande annulée" : command.Reason.Trim();
        fulfillment.Cancel(reason.Length > Fulfillment.ReasonMaxLength ? reason[..Fulfillment.ReasonMaxLength] : reason, clock);
        await workspace.SaveAsync(cancellationToken);
        return true;
    }
}

internal static class FulfillmentSteps
{
    /// <summary>
    /// Le reste à payer de la commande au départ du livreur ; s'il y en a un, l'établissement doit accepter au moins un
    /// encaissement par le livreur (espèces ou mobile money).
    /// </summary>
    public static async Task<Money?> AmountToCollectAsync(
        DeliveryWorkspace workspace, IDeliveryPayments payments, Fulfillment fulfillment, CancellationToken cancellationToken)
    {
        if (fulfillment.Mode != FulfillmentMode.Delivery || fulfillment.Status != FulfillmentStatus.Assigned)
        {
            return null;
        }

        var tenant = workspace.CurrentTenant;
        var due = await payments.AmountDueAsync(tenant, fulfillment.OrderId, cancellationToken);
        if (!due.IsPositive)
        {
            return null;
        }

        var accepted = await payments.AcceptsAsync(tenant, fulfillment.LocationId, CollectionMethod.Cash, due, cancellationToken)
            || await payments.AcceptsAsync(tenant, fulfillment.LocationId, CollectionMethod.MobileMoney, due, cancellationToken);
        DomainException.ThrowIf(!accepted, DeliveryApplicationErrors.CollectionNotAccepted);
        return due;
    }

    public static PhoneNumber? PhoneOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : PhoneNumber.Create(value);

    /// <summary>La commande du tenant courant, sans fiche active (409 sinon).</summary>
    public static async Task<OrderReference> LoadNewOrderAsync(
        DeliveryWorkspace workspace, IDeliveryOrders orders, Guid orderId, CancellationToken cancellationToken)
    {
        var tenant = workspace.CurrentTenant;
        var order = (orderId == Guid.Empty ? null : await orders.FindAsync(tenant, OrderId.From(orderId), cancellationToken))
            ?? throw new NotFoundException(DeliveryApplicationErrors.OrderNotFound);
        // Contrôle avant d'écrire ; l'index unique (tenant, commande, fiche active) tranche entre deux envois simultanés.
        DomainException.ThrowIf(
            await workspace.Fulfillments.FindActiveForOrderAsync(tenant, order.OrderId, cancellationToken) is not null,
            DeliveryApplicationErrors.OrderAlreadyHasFulfillment);
        return order;
    }
}
