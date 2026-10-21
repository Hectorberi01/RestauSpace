using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SaasRestaurant.BuildingBlocks.Api;
using Storefront.Application;
using Storefront.Application.Abstractions;
using Storefront.Infrastructure;
using Storefront.Infrastructure.Persistence;

namespace Storefront.Api;

/// <summary>
/// Composition du module Storefront (vente en ligne), appelée par l'hôte. L'hôte fournit aussi <c>IStorefrontTenants</c>
/// (Tenants), <c>IStorefrontLocations</c> (Locations), <c>IStorefrontEntitlements</c> (Billing), le tenant courant
/// (<c>AddRequestTenantContext</c>) et le schéma d'authentification par clé publique
/// (<see cref="StorefrontKeyAuthentication.AddStorefrontKeys"/>).
/// </summary>
public static class StorefrontModule
{
    public static IServiceCollection AddStorefrontModule(
        this IServiceCollection services, string connectionString, Action<StorefrontOptions>? configure = null)
    {
        services.AddHttpContextAccessor();
        services.TryAddScoped<IStorefrontClient, HttpStorefrontClient>();
        services.TryAddScoped<Storefront.Application.Abstractions.IStorefrontTables, NoTables>();
        services.TryAddScoped<Storefront.Application.Abstractions.IStorefrontSurveys, NoSurveys>();
        services.TryAddScoped<Storefront.Application.Abstractions.IStorefrontReservations, NoReservations>();
        services.TryAddScoped<Storefront.Application.Abstractions.IStorefrontReviews, NoReviews>();
        services.AddStorefrontInfrastructure(connectionString, configure);
        services.AddScoped<StorefrontWorkspace>();
        services.AddHandlersFrom(typeof(StorefrontApplicationErrors).Assembly);
        services.AddStorefrontCors();
        services.AddStorefrontRateLimiting();
        return services;
    }

    /// <summary>Tant que l'hôte n'a pas branché Floor : aucun QR de table n'est reconnu.</summary>
    private sealed class NoTables : Storefront.Application.Abstractions.IStorefrontTables
    {
        public Task<Storefront.Application.Abstractions.StorefrontTable?> FindByQrTokenAsync(TenantId tenantId, string token, CancellationToken cancellationToken = default) =>
            Task.FromResult<Storefront.Application.Abstractions.StorefrontTable?>(null);

        public Task<TableSessionId> SeatAsync(TenantId tenantId, Guid tableId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Table ordering is not wired.");
    }

    /// <summary>Tant que l'hôte n'a pas branché Reviews : aucun questionnaire.</summary>
    private sealed class NoSurveys : Storefront.Application.Abstractions.IStorefrontSurveys
    {
        public Task<Storefront.Application.Abstractions.StorefrontSurvey?> FindAsync(TenantId tenantId, Guid surveyId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Storefront.Application.Abstractions.StorefrontSurvey?>(null);

        public Task<Storefront.Application.Abstractions.StorefrontSurvey?> ActiveAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Storefront.Application.Abstractions.StorefrontSurvey?>(null);

        public Task SubmitAsync(
            TenantId tenantId, Guid surveyId, OrderId? orderId, Guid? locationId,
            IReadOnlyList<Storefront.Application.Abstractions.StorefrontSurveyAnswer> answers, CancellationToken cancellationToken = default) =>
            throw new NotFoundException(Storefront.Application.Surveys.StorefrontSurveyErrors.SurveyNotFound);
    }

    /// <summary>Tant que l'hôte n'a pas branché Reviews : aucun avis.</summary>
    private sealed class NoReviews : Storefront.Application.Abstractions.IStorefrontReviews
    {
        public Task<Storefront.Application.Abstractions.StorefrontReviews> GetAsync(
            TenantId tenantId, IReadOnlyList<LocationId>? locations, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Storefront.Application.Abstractions.StorefrontReviews(
                0, null, Enumerable.Range(1, 5).Select(rating => new Storefront.Application.Abstractions.StorefrontRatingCount(rating, 0)).ToList(), []));

        public Task<Guid> SubmitAsync(
            TenantId tenantId, LocationId locationId, OrderId orderId, int rating, string? comment, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Reviews are not wired.");
    }

    /// <summary>Tant que l'hôte n'a pas branché Reservations : aucune réservation en ligne.</summary>
    private sealed class NoReservations : Storefront.Application.Abstractions.IStorefrontReservations
    {
        public Task<Storefront.Application.Abstractions.StorefrontBookingRules> RulesAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new Storefront.Application.Abstractions.StorefrontBookingRules(false, 0, 0, 0, 0, 0));

        public Task<IReadOnlyList<(DateTimeOffset StartAtUtc, bool IsAvailable)>> CheckAsync(
            TenantId tenantId, LocationId locationId, int partySize, IReadOnlyList<DateTimeOffset> starts, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<(DateTimeOffset, bool)>>(starts.Select(start => (start, false)).ToList());

        public Task<Guid> BookAsync(
            TenantId tenantId, LocationId locationId, DateTimeOffset startAt, int partySize, string? name, string? phone, string? email, string? notes,
            CustomerId? customerId, CancellationToken cancellationToken = default) =>
            throw new DomainException(Storefront.Application.Reservations.OnlineReservationErrors.Disabled);

        public Task<Storefront.Application.Abstractions.StorefrontReservation?> FindAsync(TenantId tenantId, Guid reservationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Storefront.Application.Abstractions.StorefrontReservation?>(null);

        public Task CancelAsync(TenantId tenantId, Guid reservationId, CancellationToken cancellationToken = default) =>
            throw new NotFoundException(Storefront.Application.Reservations.OnlineReservationErrors.ReservationNotFound);

        public Task<Storefront.Application.Abstractions.StartedPayment> StartDepositAsync(
            TenantId tenantId, Guid reservationId, Storefront.Application.Abstractions.OnlinePaymentRequest request, CancellationToken cancellationToken = default) =>
            throw new NotFoundException(Storefront.Application.Reservations.OnlineReservationErrors.ReservationNotFound);

        public Task<Guid> JoinWaitlistAsync(
            TenantId tenantId, LocationId locationId, DateTimeOffset desiredStart, int partySize, int flexibilityMinutes, string? name, string? phone,
            string? email, CustomerId? customerId, CancellationToken cancellationToken = default) =>
            throw new DomainException(Storefront.Application.Reservations.OnlineReservationErrors.Disabled);

        public Task<Storefront.Application.Abstractions.StorefrontWaitlistEntry?> FindWaitlistAsync(TenantId tenantId, Guid entryId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Storefront.Application.Abstractions.StorefrontWaitlistEntry?>(null);

        public Task<Guid> AcceptWaitlistOfferAsync(TenantId tenantId, Guid entryId, CancellationToken cancellationToken = default) =>
            throw new NotFoundException(Storefront.Application.Reservations.OnlineReservationErrors.WaitlistNotFound);

        public Task CancelWaitlistAsync(TenantId tenantId, Guid entryId, CancellationToken cancellationToken = default) =>
            throw new NotFoundException(Storefront.Application.Reservations.OnlineReservationErrors.WaitlistNotFound);
    }

    /// <summary>Applique les migrations en attente du schéma du module.</summary>
    public static async Task MigrateStorefrontDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<StorefrontDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
