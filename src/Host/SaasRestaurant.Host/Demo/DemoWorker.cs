using Customers.Infrastructure.Persistence;
using Delivery.Infrastructure.Persistence;
using Floor.Infrastructure.Persistence;
using Kitchen.Infrastructure.Persistence;
using Loyalty.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Notifications.Infrastructure.Persistence;
using Orders.Application.Abstractions;
using Orders.Application.Commands;
using Orders.Domain;
using Orders.Infrastructure.Persistence;
using Payments.Infrastructure.Persistence;
using Promotions.Infrastructure.Persistence;
using Reservations.Infrastructure.Persistence;
using Reviews.Infrastructure.Persistence;
using SaasRestaurant.BuildingBlocks.Application;
using Storefront.Infrastructure.Persistence;

namespace SaasRestaurant.Host.Demo;

/// <summary>
/// Restaurants de démonstration : les commandes avancent seules (en préparation, puis prête), et chaque nuit l'activité
/// des visiteurs est effacée (commandes, paiements, clients, réservations…). La carte, les réglages et l'équipe restent.
/// Journal : ids et compteurs seulement.
/// </summary>
internal sealed class DemoWorker(IServiceScopeFactory scopes, IDemoTenants demo, IClock clock, IConfiguration configuration, ILogger<DemoWorker> logger)
    : BackgroundService
{
    // Délais depuis le passage de la commande.
    public static readonly TimeSpan PreparingAfter = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ReadyAfter = TimeSpan.FromSeconds(90);

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    // Ce qui est effacé, par module (noms des entités). Tout le reste est gardé.
    private static readonly (Type Context, string[] Entities)[] Purged =
    [
        (typeof(OrdersDbContext), ["OrderPaymentSettlement", "Order"]),
        (typeof(StorefrontDbContext), ["OnlineOrder", "CustomerSession"]),
        (typeof(PaymentsDbContext), ["Payment"]),
        (typeof(KitchenDbContext), ["KitchenTicket"]),
        (typeof(DeliveryDbContext), ["Fulfillment"]),
        (typeof(FloorDbContext), ["SessionOrder", "TableSession", "WaitingParty"]),
        (typeof(ReservationsDbContext), ["Reservation", "WaitlistEntry"]),
        (typeof(LoyaltyDbContext), ["LoyaltyTransaction", "StoredValueCard", "LoyaltyAccount"]),
        (typeof(PromotionsDbContext), ["CouponUsage", "PromotionUsage"]),
        (typeof(ReviewsDbContext), ["SurveyResponse", "Feedback"]),
        (typeof(NotificationsDbContext), ["Notification", "OtpChallenge"]),
        (typeof(CustomersDbContext), ["Consent", "CustomerNote", "Customer"]),
    ];

    private DateOnly? _purgedOn;

    /// <summary>Un passage : fait avancer les commandes des restaurants de démo ; renvoie le nombre de changements.</summary>
    public async Task<int> AdvanceOrdersAsync(CancellationToken cancellationToken = default)
    {
        var changed = 0;
        foreach (var tenant in demo.All)
        {
            changed += await AdvanceAsync(tenant, OrderStatus.Accepted, PreparingAfter, id => new StartOrderPreparationCommand(id), cancellationToken);
            changed += await AdvanceAsync(tenant, OrderStatus.Preparing, ReadyAfter, id => new MarkOrderReadyCommand(id), cancellationToken);
        }

        return changed;
    }

    /// <summary>Efface l'activité des visiteurs d'un restaurant de démo ; renvoie le nombre de lignes effacées.</summary>
    public async Task<int> PurgeAsync(TenantId tenant, CancellationToken cancellationToken = default)
    {
        if (!demo.IsDemo(tenant))
        {
            throw new InvalidOperationException("Only a demonstration restaurant can be purged.");
        }

        var deleted = 0;
        foreach (var (contextType, entities) in Purged)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(tenant);
            if (scope.ServiceProvider.GetService(contextType) is not DbContext context)
            {
                continue;
            }

            try
            {
                deleted += await DemoPurge.DeleteAsync(context, entities, tenant, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning("Demo purge of {Context} for tenant {TenantId} failed: {Error}.", contextType.Name, tenant.Value, exception.GetType().Name);
            }
        }

        return deleted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (demo.All.Count == 0)
        {
            return;
        }

        var purgeAt = TimeOnly.TryParse(configuration["Demo:PurgeAtUtc"], out var at) ? at : new TimeOnly(2, 0);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AdvanceOrdersAsync(stoppingToken);
                var now = clock.UtcNow;
                var today = DateOnly.FromDateTime(now.UtcDateTime);
                if (_purgedOn != today && TimeOnly.FromDateTime(now.UtcDateTime) >= purgeAt)
                {
                    foreach (var tenant in demo.All)
                    {
                        var deleted = await PurgeAsync(tenant, stoppingToken);
                        logger.LogInformation("Demo tenant {TenantId} purged: {Rows} rows.", tenant.Value, deleted);
                    }

                    _purgedOn = today;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Demo pass failed; retrying at the next interval.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<int> AdvanceAsync<TCommand>(
        TenantId tenant, OrderStatus status, TimeSpan after, Func<Guid, TCommand> next, CancellationToken cancellationToken)
        where TCommand : ICommand
    {
        IReadOnlyList<Guid> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(tenant);
            var page = await scope.ServiceProvider.GetRequiredService<IOrderRepository>()
                .ListAsync(tenant, new OrderSearch(Status: status), 0, 50, cancellationToken);
            var limit = clock.UtcNow - after;
            due = page.Items.Where(order => order.PlacedAtUtc is { } placed && placed <= limit).Select(order => order.Id.Value).ToList();
        }

        var changed = 0;
        foreach (var id in due)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<BackgroundTenant>().ActFor(tenant);
            try
            {
                await scope.ServiceProvider.GetRequiredService<ICommandHandler<TCommand>>().HandleAsync(next(id), cancellationToken);
                changed++;
            }
            catch (Exception exception) when (exception is DomainException or ConcurrencyException or NotFoundException)
            {
                logger.LogDebug("Demo order {OrderId} not advanced: {Error}.", id, exception.GetType().Name);
            }
        }

        return changed;
    }
}
