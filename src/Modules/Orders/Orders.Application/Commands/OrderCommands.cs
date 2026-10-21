using Orders.Application.Abstractions;
using Orders.Domain;

namespace Orders.Application.Commands;

// Prise de commande par le personnel. Une commande se compose en brouillon (Draft), puis suit
// Placed -> Accepted -> Preparing -> Ready -> Completed ; annulable avant Completed. Les règles et les montants sont ceux
// de l'agrégat ; ici, d'où viennent les références (Locations, Floor, Catalog, Payments) et le numéro.

/// <summary>
/// Ouvre un brouillon dans un établissement du tenant courant, dans la devise et le mode de prix (TTC/HT) du tenant.
/// <paramref name="TableSessionId"/> : la session de table (Floor), seulement pour une commande sur place.
/// <paramref name="CustomerId"/> : la fiche client (Customers) pour laquelle la commande est prise, facultative ; une fiche
/// anonymisée est refusée par le domaine. <paramref name="Origin"/> : le canal, posé par l'appelant serveur (caisse par
/// défaut), jamais par le client ; <paramref name="ScheduledForUtc"/> : l'heure demandée, facultative. Renvoie l'id.
/// </summary>
public sealed record CreateOrderCommand(
    Guid LocationId,
    OrderType Type,
    Guid? TableSessionId = null,
    Guid? CustomerId = null,
    OrderOrigin? Origin = null,
    DateTimeOffset? ScheduledForUtc = null) : ICommand<Guid>;

public sealed class CreateOrderHandler(
    OrdersWorkspace workspace,
    IOrderLocations locations,
    IOrderTableSessions tableSessions,
    IOrderNumberGenerator numbers,
    IOrderCustomers customers,
    IClock clock) : ICommandHandler<CreateOrderCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenantId = workspace.CurrentTenant;
        var location = (command.LocationId == Guid.Empty
                ? null
                : await locations.FindAsync(tenantId, LocationId.From(command.LocationId), cancellationToken))
            ?? throw new NotFoundException(OrdersApplicationErrors.LocationNotFound);

        TableSessionReference? session = null;
        if (command.TableSessionId is { } sessionId)
        {
            session = (sessionId == Guid.Empty
                    ? null
                    : await tableSessions.FindAsync(tenantId, TableSessionId.From(sessionId), cancellationToken))
                ?? throw new NotFoundException(OrdersApplicationErrors.TableSessionNotFound);
        }

        CustomerReference? customer = null;
        if (command.CustomerId is { } customerId)
        {
            customer = (customerId == Guid.Empty
                    ? null
                    : await customers.FindAsync(tenantId, CustomerId.From(customerId), cancellationToken))
                ?? throw new NotFoundException(OrdersApplicationErrors.CustomerNotFound);
        }

        // Le type et la fiche client sont vérifiés (règles du domaine) avant tout effet ; le numéro n'est tiré qu'ensuite.
        DomainException.ThrowIf(!Enum.IsDefined(command.Type), OrderErrors.TypeUnknown);
        DomainException.ThrowIf(customer is { IsAnonymized: true }, OrderErrors.CustomerAnonymized);
        DomainException.ThrowIf(command.Origin is { } origin && !Enum.IsDefined(origin.Channel), OrderErrors.ChannelUnknown);
        DomainException.ThrowIf(command.ScheduledForUtc is { } at && at <= clock.UtcNow, OrderErrors.ScheduleInPast);
        var number = await numbers.NextAsync(tenantId, location.LocationId, DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), cancellationToken);
        var order = Order.Create(
            tenantId, location, number, command.Type, tenant.Currency, tenant.PriceTaxMode, clock, customer, session, command.Origin,
            command.ScheduledForUtc);

        workspace.Orders.Add(order);
        await workspace.SaveAsync(cancellationToken);
        return order.Id.Value;
    }
}

/// <summary>
/// Ajoute une ligne : le Catalog décrit la vente (produit actif, variante, options valides, prix, taxe) et la commande
/// fige cet instantané. Un même produit ajouté deux fois donne deux lignes. Renvoie l'id de la ligne.
/// </summary>
public sealed record AddOrderItemCommand(
    Guid OrderId,
    Guid ProductId,
    int Quantity,
    Guid? VariantId = null,
    IReadOnlyList<Guid>? ModifierIds = null,
    string? Note = null,
    IReadOnlyList<OrderComponentInput>? Components = null) : ICommand<Guid>;

public sealed class AddOrderItemHandler(OrdersWorkspace workspace, IOrderCatalog catalog) : ICommandHandler<AddOrderItemCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddOrderItemCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var order = await workspace.LoadAsync(tenant, command.OrderId, cancellationToken);
        var quantity = Quantity.Create(command.Quantity);

        var product = (command.ProductId == Guid.Empty
                ? null
                : await catalog.DescribeForSaleAsync(
                    tenant,
                    ProductId.From(command.ProductId),
                    command.VariantId is { } variant ? VariantId.From(variant) : null,
                    (command.ModifierIds ?? Array.Empty<Guid>()).Select(ModifierId.From).ToList(),
                    order.LocationId,
                    order.Channel,
                    ComponentChoices.From(command.Components),
                    cancellationToken))
            ?? throw new NotFoundException(OrdersApplicationErrors.ProductNotFound);

        var itemId = order.AddItem(product, quantity, command.Note);

        await workspace.SaveAsync(cancellationToken);
        return itemId.Value;
    }
}

public sealed record RemoveOrderItemCommand(Guid OrderId, Guid ItemId) : ICommand;

public sealed class RemoveOrderItemHandler(OrdersWorkspace workspace) : ICommandHandler<RemoveOrderItemCommand>
{
    public Task HandleAsync(RemoveOrderItemCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditAsync(command.OrderId, order => order.RemoveItem(OrderItemId.From(command.ItemId)), cancellationToken);
}

public sealed record ChangeOrderItemQuantityCommand(Guid OrderId, Guid ItemId, int Quantity) : ICommand;

public sealed class ChangeOrderItemQuantityHandler(OrdersWorkspace workspace) : ICommandHandler<ChangeOrderItemQuantityCommand>
{
    public Task HandleAsync(ChangeOrderItemQuantityCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditAsync(
            command.OrderId,
            order => order.ChangeItemQuantity(OrderItemId.From(command.ItemId), Quantity.Create(command.Quantity)),
            cancellationToken);
}

/// <summary>Note cuisine d'une ligne ; vide = supprimée. Sans effet sur les montants.</summary>
public sealed record ChangeOrderItemNoteCommand(Guid OrderId, Guid ItemId, string? Note) : ICommand;

public sealed class ChangeOrderItemNoteHandler(OrdersWorkspace workspace) : ICommandHandler<ChangeOrderItemNoteCommand>
{
    public Task HandleAsync(ChangeOrderItemNoteCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditAsync(
            command.OrderId, order => order.ChangeItemNote(OrderItemId.From(command.ItemId), command.Note), cancellationToken);
}

/// <summary>
/// Remise manuelle (geste commercial) d'un montant, dans la devise et le mode de prix de la commande ; identifiée par
/// sa référence. Plafonnée au sous-total, réévaluée par la commande à chaque changement de ligne.
/// </summary>
public sealed record ApplyManualDiscountCommand(Guid OrderId, decimal Amount, string? Reference, string? Label = null) : ICommand;

public sealed class ApplyManualDiscountHandler(OrdersWorkspace workspace) : ICommandHandler<ApplyManualDiscountCommand>
{
    public Task HandleAsync(ApplyManualDiscountCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditAsync(
            command.OrderId,
            order => order.ApplyDiscount(OrderDiscount.Create(
                Money.Of(command.Amount, order.Currency), DiscountSource.Manual, command.Reference, command.Label)),
            cancellationToken);
}

/// <summary>
/// Remise décidée par un AUTRE module (Promotions, Loyalty) et posée par l'hôte : la provenance et la devise sont celles
/// de la décision ; une devise différente de celle de la commande est refusée (<c>orders.order.currency_mismatch</c>).
/// Pas de route : une personne passe par <see cref="ApplyManualDiscountCommand"/>.
/// </summary>
public sealed record ApplyExternalDiscountCommand(
    Guid OrderId, DiscountSource Source, decimal Amount, string CurrencyCode, string Reference, string? Label = null) : ICommand;

public sealed class ApplyExternalDiscountHandler(OrdersWorkspace workspace) : ICommandHandler<ApplyExternalDiscountCommand>
{
    public Task HandleAsync(ApplyExternalDiscountCommand command, CancellationToken cancellationToken = default)
    {
        DomainException.ThrowIf(command.Source == DiscountSource.Manual, OrdersApplicationErrors.DiscountSourceNotExternal);
        var amount = Money.Of(command.Amount, Currency.FromCode(command.CurrencyCode));
        return workspace.EditAsync(
            command.OrderId,
            order => order.ApplyDiscount(OrderDiscount.Create(amount, command.Source, command.Reference, command.Label)),
            cancellationToken);
    }
}

public sealed record RemoveDiscountCommand(Guid OrderId, DiscountSource Source, string Reference) : ICommand;

public sealed class RemoveDiscountHandler(OrdersWorkspace workspace) : ICommandHandler<RemoveDiscountCommand>
{
    public Task HandleAsync(RemoveDiscountCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditAsync(command.OrderId, order => order.RemoveDiscount(command.Source, command.Reference), cancellationToken);
}

/// <summary>Draft -> Placed : les lignes et les montants sont désormais figés (base du ticket cuisine et du paiement).</summary>
public sealed record PlaceOrderCommand(Guid OrderId) : ICommand;

/// <summary>Passe la commande ; refusée quand le plafond mensuel du plan est atteint (un brouillon n'est pas compté).</summary>
public sealed class PlaceOrderHandler(OrdersWorkspace workspace, IOrderPlanLimits limits, IClock clock) : ICommandHandler<PlaceOrderCommand>
{
    public async Task HandleAsync(PlaceOrderCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenantId = workspace.CurrentTenant;
        var order = await workspace.LoadAsync(tenantId, command.OrderId, cancellationToken);
        if (order.Status == OrderStatus.Draft && await limits.MonthlyOrdersAsync(tenantId, cancellationToken) is { } limit)
        {
            var now = clock.UtcNow.ToUniversalTime();
            var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
            var placed = await workspace.Orders.CountPlacedAsync(tenantId, monthStart, monthStart.AddMonths(1), cancellationToken);
            DomainException.ThrowIf(placed >= limit, OrdersApplicationErrors.MonthlyOrderLimitReached);
        }

        order.Place(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record AcceptOrderCommand(Guid OrderId) : ICommand;

public sealed class AcceptOrderHandler(OrdersWorkspace workspace, IClock clock) : ICommandHandler<AcceptOrderCommand>
{
    public Task HandleAsync(AcceptOrderCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditAsync(command.OrderId, order => order.Accept(clock), cancellationToken);
}

public sealed record StartOrderPreparationCommand(Guid OrderId) : ICommand;

public sealed class StartOrderPreparationHandler(OrdersWorkspace workspace, IClock clock) : ICommandHandler<StartOrderPreparationCommand>
{
    public Task HandleAsync(StartOrderPreparationCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditAsync(command.OrderId, order => order.StartPreparation(clock), cancellationToken);
}

public sealed record MarkOrderReadyCommand(Guid OrderId) : ICommand;

public sealed class MarkOrderReadyHandler(OrdersWorkspace workspace, IClock clock) : ICommandHandler<MarkOrderReadyCommand>
{
    public Task HandleAsync(MarkOrderReadyCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditAsync(command.OrderId, order => order.MarkReady(clock), cancellationToken);
}

/// <summary>Ready -> Completed, seulement si les paiements de la commande (projection locale) couvrent son total.</summary>
public sealed record CompleteOrderCommand(Guid OrderId) : ICommand;

public sealed class CompleteOrderHandler(OrdersWorkspace workspace, IOrderPayments payments, IClock clock) : ICommandHandler<CompleteOrderCommand>
{
    public async Task HandleAsync(CompleteOrderCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var order = await workspace.LoadAsync(tenant, command.OrderId, cancellationToken);

        var paid = await payments.SettledAmountAsync(tenant, order.Id, order.Currency, cancellationToken);
        order.Complete(OrderSettlement.Create(tenant, order.Id, paid), clock);

        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record RecordPaymentSettlementCommand(Guid PaymentId, Guid OrderId, decimal SettledAmount, string? CurrencyCode, long Version) : ICommand;

public sealed class RecordPaymentSettlementHandler(OrdersWorkspace workspace, IOrderPaymentSettlements settlements)
    : ICommandHandler<RecordPaymentSettlementCommand>
{
    public Task HandleAsync(RecordPaymentSettlementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        DomainException.ThrowIf(command.PaymentId == Guid.Empty || command.OrderId == Guid.Empty, CommonErrors.EmptyId);
        return settlements.RecordAsync(
            workspace.CurrentTenant,
            command.PaymentId,
            OrderId.From(command.OrderId),
            Money.Of(command.SettledAmount, Currency.FromCode(command.CurrencyCode)),
            command.Version,
            cancellationToken);
    }
}

/// <summary>Annulation depuis tout statut non terminal ; raison obligatoire (1 à 500 caractères).</summary>
public sealed record CancelOrderCommand(Guid OrderId, string? Reason) : ICommand;

public sealed class CancelOrderHandler(OrdersWorkspace workspace, IClock clock) : ICommandHandler<CancelOrderCommand>
{
    public Task HandleAsync(CancelOrderCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditAsync(command.OrderId, order => order.Cancel(command.Reason!, clock), cancellationToken);
}

/// <summary>
/// Rattache la commande à une fiche client du tenant courant, ou l'en détache (<paramref name="CustomerId"/> null). Une
/// fiche inconnue ou d'un autre tenant : 404 ; une fiche anonymisée : refusée.
/// </summary>
public sealed record ChangeOrderCustomerCommand(Guid OrderId, Guid? CustomerId) : ICommand;

public sealed class ChangeOrderCustomerHandler(OrdersWorkspace workspace, IOrderCustomers customers, IClock clock)
    : ICommandHandler<ChangeOrderCustomerCommand>
{
    public async Task HandleAsync(ChangeOrderCustomerCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenantId = workspace.CurrentTenant;
        var order = await workspace.LoadAsync(tenantId, command.OrderId, cancellationToken);
        CustomerReference? customer = null;
        if (command.CustomerId is { } customerId)
        {
            customer = (customerId == Guid.Empty
                    ? null
                    : await customers.FindAsync(tenantId, CustomerId.From(customerId), cancellationToken))
                ?? throw new NotFoundException(OrdersApplicationErrors.CustomerNotFound);
        }

        order.ChangeCustomer(customer, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Frais de livraison décidés par le module Delivery (zone de la fiche) et posés par l'hôte ; <paramref name="Reference"/> :
/// la fiche de livraison. Pas de route : une personne passe par la fiche de livraison.
/// </summary>
public sealed record ApplyDeliveryFeeCommand(Guid OrderId, decimal Amount, string CurrencyCode, string Reference) : ICommand;

public sealed class ApplyDeliveryFeeHandler(OrdersWorkspace workspace, IClock clock) : ICommandHandler<ApplyDeliveryFeeCommand>
{
    public Task HandleAsync(ApplyDeliveryFeeCommand command, CancellationToken cancellationToken = default)
    {
        var fee = Money.Of(command.Amount, Currency.FromCode(command.CurrencyCode));
        return workspace.EditAsync(command.OrderId, order => order.ApplyDeliveryFee(fee, command.Reference, clock), cancellationToken);
    }
}

/// <summary>La fiche de livraison <paramref name="Reference"/> est annulée : ses frais quittent la commande.</summary>
public sealed record RemoveDeliveryFeeCommand(Guid OrderId, string Reference) : ICommand;

public sealed class RemoveDeliveryFeeHandler(OrdersWorkspace workspace, IClock clock) : ICommandHandler<RemoveDeliveryFeeCommand>
{
    public Task HandleAsync(RemoveDeliveryFeeCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditAsync(command.OrderId, order => order.RemoveDeliveryFee(command.Reference, clock), cancellationToken);
}

/// <summary>
/// Recompose une ligne du brouillon (variante, options, quantité, note) sans la retirer : le Catalog redécrit le même
/// produit, la ligne garde sa place. Renvoie l'id de la ligne recomposée.
/// </summary>
public sealed record ReplaceOrderItemCommand(
    Guid OrderId,
    Guid ItemId,
    int Quantity,
    Guid? VariantId = null,
    IReadOnlyList<Guid>? ModifierIds = null,
    string? Note = null,
    IReadOnlyList<OrderComponentInput>? Components = null) : ICommand<Guid>;

public sealed class ReplaceOrderItemHandler(OrdersWorkspace workspace, IOrderCatalog catalog) : ICommandHandler<ReplaceOrderItemCommand, Guid>
{
    public async Task<Guid> HandleAsync(ReplaceOrderItemCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var order = await workspace.LoadAsync(tenant, command.OrderId, cancellationToken);
        var itemId = command.ItemId == Guid.Empty ? throw new DomainException(OrderErrors.ItemNotFound) : OrderItemId.From(command.ItemId);
        var current = order.Items.FirstOrDefault(item => item.Id == itemId) ?? throw new DomainException(OrderErrors.ItemNotFound);
        var quantity = Quantity.Create(command.Quantity);

        var product = await catalog.DescribeForSaleAsync(
                tenant,
                current.ProductId,
                command.VariantId is { } variant ? VariantId.From(variant) : null,
                (command.ModifierIds ?? Array.Empty<Guid>()).Select(ModifierId.From).ToList(),
                order.LocationId,
                order.Channel,
                ComponentChoices.From(command.Components),
                cancellationToken)
            ?? throw new NotFoundException(OrdersApplicationErrors.ProductNotFound);

        var replaced = order.ReplaceItem(itemId, product, quantity, command.Note);
        await workspace.SaveAsync(cancellationToken);
        return replaced.Value;
    }
}

/// <summary>Une commande sur place en cours change de table : vers une autre session ouverte du même établissement.</summary>
public sealed record MoveOrderToTableSessionCommand(Guid OrderId, Guid TableSessionId) : ICommand;

public sealed class MoveOrderToTableSessionHandler(OrdersWorkspace workspace, IOrderTableSessions sessions, IClock clock)
    : ICommandHandler<MoveOrderToTableSessionCommand>
{
    public async Task HandleAsync(MoveOrderToTableSessionCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var order = await workspace.LoadAsync(tenant, command.OrderId, cancellationToken);
        var session = (command.TableSessionId == Guid.Empty
                ? null
                : await sessions.FindAsync(tenant, TableSessionId.From(command.TableSessionId), cancellationToken))
            ?? throw new NotFoundException(OrdersApplicationErrors.TableSessionNotFound);

        order.MoveToTableSession(session, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Pour l'hôte (fusion de deux tables, demandée par Floor) : les commandes en cours d'une session passent sur une autre.
/// Renvoie le nombre de commandes déplacées.
/// </summary>
public sealed record MoveTableSessionOrdersCommand(Guid FromTableSessionId, Guid ToTableSessionId) : ICommand<int>;

public sealed class MoveTableSessionOrdersHandler(OrdersWorkspace workspace, IOrderTableSessions sessions, IClock clock)
    : ICommandHandler<MoveTableSessionOrdersCommand, int>
{
    public async Task<int> HandleAsync(MoveTableSessionOrdersCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        DomainException.ThrowIf(command.FromTableSessionId == Guid.Empty, CommonErrors.EmptyId);
        var target = (command.ToTableSessionId == Guid.Empty
                ? null
                : await sessions.FindAsync(tenant, TableSessionId.From(command.ToTableSessionId), cancellationToken))
            ?? throw new NotFoundException(OrdersApplicationErrors.TableSessionNotFound);

        var moving = await workspace.Orders.ListOpenByTableSessionAsync(tenant, TableSessionId.From(command.FromTableSessionId), cancellationToken);
        foreach (var order in moving)
        {
            order.MoveToTableSession(target, clock);
        }

        await workspace.SaveAsync(cancellationToken);
        return moving.Count;
    }
}
