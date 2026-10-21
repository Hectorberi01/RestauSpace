using System.Text.Json;
using Customers.Application.Abstractions;
using Customers.Application.Queries;
using Orders.Application.Abstractions;
using Reservations.Application.Abstractions;
using Reservations.Application.Commands;
using SaasRestaurant.BuildingBlocks.Application;
using Tenants.Application.Queries;
using SaasRestaurant.Contracts.Customers;

namespace SaasRestaurant.Host;

// Les ports déclarés par Customers et ceux qu'Orders et Reservations déclarent vers Customers, servis par les cas d'usage
// de l'autre module, et l'effacement des copies d'un client anonymisé (par l'outbox). Aucun module n'en référence un
// autre : seul le host les relie. Les queries répondent pour le tenant du contexte (requête ou BackgroundTenant).

/// <summary>Customers -> Tenants : le tenant accepte-t-il les écritures métier ? Inconnu = non.</summary>
internal sealed class CustomersTenantsFromTenants(CachedTenants tenants) : ICustomersTenants
{
    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);
}

/// <summary>
/// La référence d'un client du tenant du contexte, lue dans Customers : (tenant, id, anonymisé) et RIEN d'autre. Null
/// pour un client inconnu ou d'un autre tenant.
/// </summary>
internal sealed class CustomerReferences(ITenantContext tenantContext, IQueryHandler<GetCustomerReferenceQuery, CustomerReferenceDto> customers)
{
    public async Task<CustomerReferenceDto?> FindAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var reference = await customers.HandleAsync(new GetCustomerReferenceQuery(customerId.Value), cancellationToken);
            return reference.TenantId == tenantId.Value ? reference : null;
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Orders -> Customers : la fiche client d'une commande.</summary>
internal sealed class OrderCustomersFromCustomers(CustomerReferences customers) : IOrderCustomers
{
    public async Task<Orders.Domain.CustomerReference?> FindAsync(
        TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
        await customers.FindAsync(tenantId, customerId, cancellationToken) is { } reference
            ? new Orders.Domain.CustomerReference(tenantId, customerId, reference.IsAnonymized)
            : null;
}

/// <summary>Reservations -> Customers : la fiche client d'une réservation.</summary>
internal sealed class ReservationCustomersFromCustomers(CustomerReferences customers) : IReservationCustomers
{
    public async Task<Reservations.Domain.CustomerReference?> FindAsync(
        TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
        await customers.FindAsync(tenantId, customerId, cancellationToken) is { } reference
            ? new Reservations.Domain.CustomerReference(tenantId, customerId, reference.IsAnonymized)
            : null;
}

/// <summary>
/// customers.CustomerAnonymized -> effacement du contact et des notes de toutes les réservations du client (idempotent),
/// par l'outbox (après commit, au moins une fois). Le message est traité dans son propre scope DI, pour le tenant du
/// message (<see cref="BackgroundTenant"/>). Les consentements sont retirés par Customers lui-même, dans la transaction
/// de l'anonymisation ; Orders ne garde du client que son id. Un refus métier part en file morte ; les autres erreurs
/// sont retentées.
/// </summary>
internal sealed class CustomerAnonymizationFlow(IServiceScopeFactory scopes, ILogger<CustomerAnonymizationFlow> logger) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not ("customers", "CustomerAnonymized"))
        {
            return;
        }

        var anonymized = message.Read<CustomerAnonymized>();
        var customerId = anonymized.CustomerId;

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(anonymized.TenantId));

        var erased = await services.GetRequiredService<ICommandHandler<AnonymizeCustomerReservationsCommand, int>>()
            .HandleAsync(new AnonymizeCustomerReservationsCommand(customerId), cancellationToken);
        // Ni nom ni contact dans le journal : seulement des ids et un nombre.
        logger.LogInformation("Customer {CustomerId} anonymized: {Count} reservation(s) erased.", customerId, erased);
    }
}

/// <summary>
/// customers.CustomerMerged -> l'historique du doublon (commandes, avis, réservations, points de fidélité) passe à la
/// fiche qui l'a absorbé, par l'outbox, pour le tenant du message. Chaque étape est rejouable : un message traité deux
/// fois ne change rien de plus. Journal : ids et nombres seulement.
/// </summary>
internal sealed class CustomerMergeFlow(IServiceScopeFactory scopes, ILogger<CustomerMergeFlow> logger) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not ("customers", "CustomerMerged"))
        {
            return;
        }

        var merged = message.Read<CustomerMerged>();
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(TenantId.From(merged.TenantId));

        var orders = await services.GetRequiredService<ICommandHandler<Orders.Application.Commands.ReassignOrdersToCustomerCommand, int>>()
            .HandleAsync(new Orders.Application.Commands.ReassignOrdersToCustomerCommand(merged.CustomerId, merged.MergedIntoId), cancellationToken);
        var feedbacks = await services.GetRequiredService<ICommandHandler<Reviews.Application.Commands.ReassignFeedbacksToCustomerCommand, int>>()
            .HandleAsync(new Reviews.Application.Commands.ReassignFeedbacksToCustomerCommand(merged.CustomerId, merged.MergedIntoId), cancellationToken);
        var reservations = await services.GetRequiredService<ICommandHandler<ReassignReservationsToCustomerCommand, int>>()
            .HandleAsync(new ReassignReservationsToCustomerCommand(merged.CustomerId, merged.MergedIntoId), cancellationToken);
        var loyalty = await services.GetRequiredService<ICommandHandler<Loyalty.Application.Commands.MergeCustomerLoyaltyAccountsCommand, Loyalty.Application.Commands.LoyaltyReactionOutcome>>()
            .HandleAsync(new Loyalty.Application.Commands.MergeCustomerLoyaltyAccountsCommand(merged.CustomerId, merged.MergedIntoId), cancellationToken);
        logger.LogInformation(
            "Customer {CustomerId} merged into {MergedIntoId}: {Orders} order(s), {Feedbacks} feedback(s), {Reservations} reservation(s), loyalty {Loyalty}.",
            merged.CustomerId, merged.MergedIntoId, orders, feedbacks, reservations, loyalty);
    }
}
