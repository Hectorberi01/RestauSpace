using Billing.Application;
using Billing.Application.Commands;
using Billing.Application.Queries;
using Billing.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;

namespace Billing.Api;

/// <summary>Les deux groupes de routes : l'hôte chaîne la policy de chacun (restaurant, administration).</summary>
public sealed record BillingRouteGroups(RouteGroupBuilder Restaurant, RouteGroupBuilder Admin);

/// <summary>
/// Abonnement SaaS.
/// <list type="bullet">
/// <item><c>/billing</c> : le restaurant sélectionné dans l'access token (jamais un tenant de requête) voit le catalogue
/// et son abonnement (<see cref="BillingPermissions.View"/>), choisit son plan, en change (programmé à l'échéance pendant
/// une période payée) et programme sa résiliation
/// (<see cref="BillingPermissions.Manage"/>) ;</item>
/// <item><c>/admin/billing</c> : la plateforme gère le catalogue des plans et les abonnements de tous les restaurants
/// (enregistrement des paiements, impayé, suspension, résiliation). L'hôte y met la policy d'administration.</item>
/// </list>
/// Échéances (fin d'essai, impayé, suspension, résiliation programmée) : job du module, pas de route.
/// </summary>
public static class BillingEndpoints
{
    private const string View = BillingPermissions.View;
    private const string Manage = BillingPermissions.Manage;
    private const string GetPlanRoute = "GetBillingPlan";
    private const string GetSubscriptionRoute = "GetBillingSubscription";

    public static BillingRouteGroups MapBillingEndpoints(this IEndpointRouteBuilder routes) =>
        new(MapRestaurant(routes), MapAdmin(routes));

    private static RouteGroupBuilder MapRestaurant(IEndpointRouteBuilder routes)
    {
        var billing = routes.MapGroup("/billing").WithTags("Billing");

        billing.MapGet("/plans", async (IQueryHandler<ListMyPlansQuery, IReadOnlyList<PlanDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListMyPlansQuery(), ct)))
            .RequirePermission(View);

        billing.MapGet("/add-ons", async (IQueryHandler<ListMyAddOnsQuery, MyAddOnsDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListMyAddOnsQuery(), ct)))
            .RequirePermission(View);

        billing.MapPost("/add-ons", async (SubscribeAddOnRequest request, ICommandHandler<SubscribeAddOnCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new SubscribeAddOnCommand(request.PlanId, request.Quantity ?? 1), ct);
            return TypedResults.Created("/billing/add-ons", new CreatedResponse(id));
        }).RequirePermission(Manage);

        billing.MapPut("/add-ons/{addOnId:guid}/quantity", (
            Guid addOnId, AddOnQuantityRequest request, ICommandHandler<ChangeMyAddOnQuantityCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeMyAddOnQuantityCommand(addOnId, request.Quantity), ct)))
            .RequirePermission(Manage);

        billing.MapPost("/add-ons/{addOnId:guid}/cancel", (Guid addOnId, ICommandHandler<CancelMyAddOnCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CancelMyAddOnCommand(addOnId), ct)))
            .RequirePermission(Manage);

        billing.MapGet("/subscription", async (IQueryHandler<GetMySubscriptionQuery, MySubscriptionDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetMySubscriptionQuery(), ct)))
            .RequirePermission(View);

        billing.MapGet("/features", async (IQueryHandler<GetMyFeaturesQuery, MyFeaturesDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetMyFeaturesQuery(), ct)))
            .RequirePermission(View);

        billing.MapPost("/subscription", async (ChoosePlanRequest request, ICommandHandler<StartSubscriptionCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new StartSubscriptionCommand(request.PlanId), ct);
            return TypedResults.Created("/billing/subscription", new CreatedResponse(id));
        }).RequirePermission(Manage);

        billing.MapPut("/subscription/plan", (ChoosePlanRequest request, ICommandHandler<ChangeSubscriptionPlanCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeSubscriptionPlanCommand(request.PlanId), ct)))
            .RequirePermission(Manage);

        // Période payée : le changement de plan est programmé pour l'échéance ; cette route l'annule.
        billing.MapDelete("/subscription/scheduled-plan", (ICommandHandler<UnscheduleSubscriptionPlanChangeCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new UnscheduleSubscriptionPlanChangeCommand(), ct)))
            .RequirePermission(Manage);

        billing.MapPost("/subscription/cancellation", (ICommandHandler<ScheduleSubscriptionCancellationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ScheduleSubscriptionCancellationCommand(), ct)))
            .RequirePermission(Manage);

        billing.MapDelete("/subscription/cancellation", (ICommandHandler<UnscheduleSubscriptionCancellationCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new UnscheduleSubscriptionCancellationCommand(), ct)))
            .RequirePermission(Manage);

        billing.MapGet("/payments", async (
            int? page, int? pageSize, IQueryHandler<ListMySubscriptionPaymentsQuery, IReadOnlyList<SubscriptionPaymentRecordDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) =>
                handler.HandleAsync(new ListMySubscriptionPaymentsQuery(skip, take), ct))))
            .RequirePermission(View);

        billing.MapGet("/payments/{paymentId:guid}/invoice", async (
            Guid paymentId, IQueryHandler<GetMyInvoiceQuery, InvoiceDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetMyInvoiceQuery(paymentId), ct)))
            .RequirePermission(View);

        // Règlement en ligne : le prestataire de la plateforme fait foi ; le restaurant relit l'état en attendant.
        billing.MapGet("/online-payment", async (IQueryHandler<GetOnlinePaymentQuery, OnlinePaymentDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetOnlinePaymentQuery(), ct)))
            .RequirePermission(View);

        billing.MapPost("/checkouts", async (
            StartCheckoutRequest? request, ICommandHandler<StartSubscriptionCheckoutCommand, SubscriptionCheckoutDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new StartSubscriptionCheckoutCommand(request?.PayerPhone, request?.Provider), ct)))
            .RequirePermission(Manage);

        billing.MapPost("/checkouts/{checkoutId:guid}/refresh", async (
            Guid checkoutId, ICommandHandler<RefreshSubscriptionCheckoutCommand, SubscriptionCheckoutDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new RefreshSubscriptionCheckoutCommand(checkoutId), ct)))
            .RequirePermission(View);

        return billing;
    }

    private static RouteGroupBuilder MapAdmin(IEndpointRouteBuilder routes)
    {
        var admin = routes.MapGroup("/admin/billing").WithTags("Billing administration");
        MapAdminPlans(admin.MapGroup("/plans"));
        MapAdminSubscriptions(admin);

        admin.MapGet("/overview", async (IQueryHandler<GetBillingOverviewQuery, BillingOverviewDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetBillingOverviewQuery(), ct)));

        // Le prestataire qui encaisse les abonnements de chaque devise (ses identifiants sont réglés dans Payments).
        admin.MapGet("/online-payment", async (
            IQueryHandler<ListOnlinePaymentRoutesQuery, IReadOnlyList<OnlinePaymentRouteDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListOnlinePaymentRoutesQuery(), ct)));

        admin.MapPut("/online-payment/{currency}", (
            string currency, OnlinePaymentRouteRequest request, ICommandHandler<SetOnlinePaymentRouteCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetOnlinePaymentRouteCommand(currency, request.All), ct)));

        admin.MapGet("/payments", async (
            Guid? tenantId, Guid? subscriptionId, DateTimeOffset? from, DateTimeOffset? to, int? page, int? pageSize,
            IQueryHandler<ListSubscriptionPaymentsQuery, IReadOnlyList<SubscriptionPaymentRecordDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) =>
                handler.HandleAsync(new ListSubscriptionPaymentsQuery(tenantId, subscriptionId, from, to, skip, take), ct))));

        return admin;
    }

    private static void MapAdminPlans(RouteGroupBuilder plans)
    {
        plans.MapGet("/", async (bool? activeOnly, string? currency, IQueryHandler<ListPlansQuery, IReadOnlyList<PlanDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListPlansQuery(activeOnly ?? false, currency), ct)));

        plans.MapPost("/", async (CreatePlanRequest request, ICommandHandler<CreatePlanCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new CreatePlanCommand(request.Name, request.Price, request.CurrencyCode, request.BillingInterval, request.Entitlements, request.Kind), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetPlanRoute, new { id });
        });

        plans.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetPlanQuery, PlanDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetPlanQuery(id), ct)))
            .WithName(GetPlanRoute);

        plans.MapPut("/{id:guid}/price", (Guid id, PlanPriceRequest request, ICommandHandler<ChangePlanPriceCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangePlanPriceCommand(id, request.Price), ct)));

        plans.MapPut("/{id:guid}/entitlements", (Guid id, EntitlementInput request, ICommandHandler<SetPlanEntitlementCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetPlanEntitlementCommand(id, request), ct)));

        plans.MapPost("/{id:guid}/archive", (Guid id, ICommandHandler<ArchivePlanCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ArchivePlanCommand(id), ct)));
    }

    private static void MapAdminSubscriptions(RouteGroupBuilder admin)
    {
        // Le restaurant est désigné ici par l'administrateur de la plateforme : c'est la seule route où un tenant vient
        // de la requête, et elle n'est servie qu'à la policy d'administration.
        admin.MapPost("/tenants/{tenantId:guid}/subscription", async (
            Guid tenantId, AdminStartSubscriptionRequest request, ICommandHandler<AdminStartSubscriptionCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new AdminStartSubscriptionCommand(tenantId, request.PlanId, request.TrialDays), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetSubscriptionRoute, new { id });
        });

        // Plan offert au restaurant désigné, pour une période ou sans fin (même règle que la route ci-dessus).
        admin.MapPost("/tenants/{tenantId:guid}/complimentary", async (
            Guid tenantId, GrantComplimentaryRequest request, ICommandHandler<AdminGrantComplimentaryCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new CreatedResponse(await handler.HandleAsync(new AdminGrantComplimentaryCommand(tenantId, request.PlanId, request.UntilUtc, request.RenewalMonths), ct))));

        // Les options d'un restaurant, désigné par l'administrateur (même règle que la route ci-dessus).
        var addOns = admin.MapGroup("/tenants/{tenantId:guid}/add-ons");

        addOns.MapGet("/", async (Guid tenantId, IQueryHandler<ListTenantAddOnsQuery, IReadOnlyList<TenantAddOnDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListTenantAddOnsQuery(tenantId), ct)));

        addOns.MapPost("/", async (
            Guid tenantId, AdminActivateAddOnRequest request, ICommandHandler<ActivateTenantAddOnCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new ActivateTenantAddOnCommand(tenantId, request.PlanId, request.Quantity ?? 1), ct);
            return TypedResults.Created($"/admin/billing/tenants/{tenantId}/add-ons/{id}", new CreatedResponse(id));
        });

        addOns.MapPut("/{addOnId:guid}/quantity", (
            Guid tenantId, Guid addOnId, AddOnQuantityRequest request, ICommandHandler<ChangeTenantAddOnQuantityCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeTenantAddOnQuantityCommand(tenantId, addOnId, request.Quantity), ct)));

        addOns.MapPost("/{addOnId:guid}/cancel", (Guid tenantId, Guid addOnId, ICommandHandler<CancelTenantAddOnCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CancelTenantAddOnCommand(tenantId, addOnId), ct)));

        // Option offerte : ajoutée, ou offre remplacée si l'option est déjà active.
        addOns.MapPost("/offer", async (
            Guid tenantId, OfferAddOnRequest request, ICommandHandler<AdminOfferAddOnCommand, Guid> handler, CancellationToken ct) =>
            TypedResults.Ok(new CreatedResponse(await handler.HandleAsync(
                new AdminOfferAddOnCommand(tenantId, request.PlanId, request.Quantity ?? 1, request.UntilUtc, request.RenewalMonths, request.Then), ct))));

        addOns.MapPost("/{addOnId:guid}/offer/end", (Guid tenantId, Guid addOnId, ICommandHandler<AdminEndAddOnOfferCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AdminEndAddOnOfferCommand(tenantId, addOnId), ct)));

        // Toutes les options actives de la plateforme (vue Vente en ligne du back-office).
        admin.MapGet("/add-ons", async (int? page, int? pageSize, IQueryHandler<ListActiveAddOnsQuery, IReadOnlyList<TenantAddOnDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(new ListActiveAddOnsQuery(skip, take), ct))));

        var subscriptions = admin.MapGroup("/subscriptions");

        subscriptions.MapGet("/", async (
            Guid? tenantId, SubscriptionStatus? status, int? page, int? pageSize,
            IQueryHandler<ListSubscriptionsQuery, IReadOnlyList<SubscriptionDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(new ListSubscriptionsQuery(tenantId, status, skip, take), ct))));

        subscriptions.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetSubscriptionQuery, SubscriptionDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetSubscriptionQuery(id), ct)))
            .WithName(GetSubscriptionRoute);

        subscriptions.MapGet("/{id:guid}/charge", async (Guid id, IQueryHandler<GetSubscriptionChargeQuery, SubscriptionChargeDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetSubscriptionChargeQuery(id), ct)));

        subscriptions.MapPost("/{id:guid}/payments", async (
            Guid id, ICommandHandler<RecordSubscriptionPaymentCommand, SubscriptionPaymentDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new RecordSubscriptionPaymentCommand(id), ct)));

        subscriptions.MapPost("/{id:guid}/past-due", (Guid id, ICommandHandler<AdminMarkSubscriptionPastDueCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AdminMarkSubscriptionPastDueCommand(id), ct)));

        subscriptions.MapPost("/{id:guid}/suspend", (
            Guid id, SuspendSubscriptionRequest request, ICommandHandler<AdminSuspendSubscriptionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AdminSuspendSubscriptionCommand(id, request.Reason), ct)));

        subscriptions.MapPost("/{id:guid}/complimentary/end", (Guid id, ICommandHandler<AdminEndComplimentaryCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AdminEndComplimentaryCommand(id), ct)));

        subscriptions.MapPost("/{id:guid}/cancel", (Guid id, ICommandHandler<AdminCancelSubscriptionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AdminCancelSubscriptionCommand(id), ct)));
    }

    private static async Task<NoContent> Run(Task command)
    {
        await command;
        return TypedResults.NoContent();
    }
}
