using System.Globalization;
using System.Text.Json;
using Locations.Application.Queries;
using Notifications.Application.Commands;
using Notifications.Domain;
using Orders.Application.Queries;
using Reviews.Application;
using Reviews.Application.Abstractions;
using Reviews.Application.Commands;
using SaasRestaurant.BuildingBlocks.Application;
using Staff.Application.Queries;
using Staff.Domain;
using Tenants.Application.Queries;

namespace SaasRestaurant.Host;

// Les ports déclarés par Reviews, servis par les cas d'usage des autres modules, l'effacement des avis d'un client
// anonymisé et l'alerte de note basse (par l'outbox). Aucun module n'en référence un autre : seul le host les relie.
// Les queries répondent pour le tenant du contexte (requête ou BackgroundTenant) : jamais pour un autre.

/// <summary>Reviews -> Tenants : le tenant accepte-t-il les écritures métier ? Inconnu = non.</summary>
internal sealed class ReviewTenantsFromTenants(CachedTenants tenants) : IReviewTenants
{
    public Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        tenants.AcceptsBusinessWritesAsync(tenantId, cancellationToken);
}

/// <summary>Reviews -> Locations : l'établissement évalué (opérationnel ou non : une expérience passée reste évaluable).</summary>
internal sealed class ReviewLocationsFromLocations(ITenantContext tenantContext, IQueryHandler<GetLocationByIdQuery, LocationDto> locations)
    : IReviewLocations
{
    public async Task<Reviews.Domain.LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var location = await locations.HandleAsync(new GetLocationByIdQuery(locationId.Value), cancellationToken);
            return new Reviews.Domain.LocationReference(tenantId, locationId, location.IsOperational);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Reviews -> Orders : la commande évaluée, réduite à (tenant, id, établissement, client). Ni montant ni lignes.</summary>
internal sealed class ReviewOrdersFromOrders(ITenantContext tenantContext, IQueryHandler<GetOrderByIdQuery, OrderDto> orders) : IReviewOrders
{
    public async Task<ReviewedOrder?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default)
    {
        if (tenantContext.CurrentTenantId != tenantId)
        {
            return null;
        }

        try
        {
            var order = await orders.HandleAsync(new GetOrderByIdQuery(orderId.Value), cancellationToken);
            return new ReviewedOrder(
                tenantId,
                orderId,
                LocationId.From(order.LocationId),
                order.CustomerId is { } customer ? CustomerId.From(customer) : (CustomerId?)null);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }
}

/// <summary>Reviews -> Customers : l'auteur d'un avis (tenant, id, anonymisé ; rien d'autre).</summary>
internal sealed class ReviewCustomersFromCustomers(CustomerReferences customers) : IReviewCustomers
{
    public async Task<Reviews.Domain.CustomerReference?> FindAsync(
        TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
        await customers.FindAsync(tenantId, customerId, cancellationToken) is { } reference
            ? new Reviews.Domain.CustomerReference(customerId, tenantId, reference.IsAnonymized)
            : null;
}

/// <summary>
/// customers.CustomerAnonymized -> retrait de l'auteur ET du commentaire de tous les avis du client (idempotent), par
/// l'outbox (après commit, au moins une fois), dans son propre scope DI pour le tenant du message. Un refus métier part
/// en file morte ; les autres erreurs sont retentées. Journal : ids et nombre seulement.
/// </summary>
internal sealed class ReviewsAnonymizationFlow(IServiceScopeFactory scopes, ILogger<ReviewsAnonymizationFlow> logger) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not ("customers", "CustomerAnonymized"))
        {
            return;
        }

        var anonymized = message.Read<SaasRestaurant.Contracts.Customers.CustomerAnonymized>();
        var tenantId = TenantId.From(anonymized.TenantId);
        var customerId = anonymized.CustomerId;

        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(tenantId);

        var detached = await services.GetRequiredService<ICommandHandler<DetachCustomerFeedbacksCommand, int>>()
            .HandleAsync(new DetachCustomerFeedbacksCommand(customerId), cancellationToken);
        logger.LogInformation("Customer {CustomerId} anonymized: {Count} feedback(s) detached.", customerId, detached);
    }
}

/// <summary>Note à partir de laquelle un avis est signalé à l'équipe (Reviews:LowRatingAlertThreshold ; 0 = jamais).</summary>
internal sealed record ReviewAlertSettings(int LowRatingThreshold);

/// <summary>
/// reviews.FeedbackSubmitted avec une note basse -> notification in-app <c>staff.new_review</c> aux membres ACTIFS qui
/// peuvent traiter les avis de cet établissement (reviews.manage, rôle sur place ou tenant-wide). Variables : nom du
/// restaurant, de l'établissement et la note, JAMAIS le commentaire ni l'auteur. Une notification par (événement,
/// destinataire) grâce à la clé d'idempotence. Un refus métier part en file morte ; les autres erreurs sont retentées.
/// </summary>
internal sealed class ReviewAlertFlow(IServiceScopeFactory scopes, ReviewAlertSettings settings, ILogger<ReviewAlertFlow> logger) : IOutboxConsumer
{
    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if ((message.Module, message.Type) is not ("reviews", "FeedbackSubmitted"))
        {
            return;
        }

        var submitted = message.Read<SaasRestaurant.Contracts.Reviews.FeedbackSubmitted>();
        var rating = submitted.Rating;
        if (rating > settings.LowRatingThreshold)
        {
            return;
        }

        var tenantId = TenantId.From(submitted.TenantId);
        var feedbackId = submitted.FeedbackId;
        var locationId = LocationId.From(submitted.LocationId);
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundTenant>().ActFor(tenantId);

        var restaurant = await services.GetRequiredService<IQueryHandler<GetTenantByIdQuery, TenantDto>>()
            .HandleAsync(new GetTenantByIdQuery(tenantId.Value), cancellationToken);
        var location = await services.GetRequiredService<IQueryHandler<GetLocationByIdQuery, LocationDto>>()
            .HandleAsync(new GetLocationByIdQuery(locationId.Value), cancellationToken);
        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["restaurant.name"] = restaurant.Name,
            ["location.name"] = location.Name,
            ["review.rating"] = rating.ToString(CultureInfo.InvariantCulture),
        };

        var send = services.GetRequiredService<ICommandHandler<SendNotificationCommand, SendNotificationResult>>();
        var recipients = await RecipientsAsync(services, tenantId, locationId, cancellationToken);
        foreach (var userId in recipients)
        {
            _ = await send.HandleAsync(
                new SendNotificationCommand(
                    NotificationTypes.StaffNewReview.Key.Value, NotificationChannel.InApp, UserId: userId, LocationId: locationId.Value,
                    Variables: variables, IdempotencyKey: $"reviews:{message.Id.ToString(CultureInfo.InvariantCulture)}:{userId:N}"),
                cancellationToken);
        }

        logger.LogInformation("Low rating on feedback {FeedbackId}: {Count} team member(s) notified.", feedbackId, recipients.Count);
    }

    /// <summary>Les membres actifs du restaurant qui peuvent traiter les avis de l'établissement (Staff : rôles, sans cache).</summary>
    private static async Task<List<Guid>> RecipientsAsync(IServiceProvider services, TenantId tenantId, LocationId locationId, CancellationToken cancellationToken)
    {
        var list = services.GetRequiredService<IQueryHandler<ListStaffQuery, PagedResult<StaffMemberDto>>>();
        var permissions = services.GetRequiredService<IPermissionChecker>();
        var recipients = new List<Guid>();
        for (var page = 1; ; page++)
        {
            var members = await list.HandleAsync(new ListStaffQuery(page, ListStaffQuery.MaxPageSize), cancellationToken);
            foreach (var member in members.Items.Where(member => member.Status == StaffStatus.Active))
            {
                if (await permissions.HasLocationPermissionAsync(
                    UserId.From(member.UserId), tenantId, locationId, ReviewsPermissions.Manage, cancellationToken))
                {
                    recipients.Add(member.UserId);
                }
            }

            if (!members.HasMore || members.Items.Count == 0)
            {
                return recipients;
            }
        }
    }
}
