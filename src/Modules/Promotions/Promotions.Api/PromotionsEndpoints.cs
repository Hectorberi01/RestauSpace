using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Promotions.Application;
using Promotions.Application.Commands;
using Promotions.Application.Queries;
using Promotions.Domain;
using SaasRestaurant.BuildingBlocks.Api;

namespace Promotions.Api;

/// <summary>
/// Promotions et coupons du restaurant sélectionné dans l'access token. Aucune route ne prend de tenant : ce n'est jamais
/// une valeur de requête. L'hôte chaîne l'autorisation (<c>Tenancy.TenantMemberPolicy</c>) sur le groupe renvoyé.
/// Lecture et prévisualisation : <see cref="PromotionsPermissions.View"/> ; appliquer à une commande :
/// <see cref="PromotionsPermissions.Apply"/> ; catalogue : <see cref="PromotionsPermissions.Manage"/>. Rendre un usage
/// (commande annulée) et expirer n'ont pas de route : ce sont une réaction et un job.
/// </summary>
public static class PromotionsEndpoints
{
    private const string View = PromotionsPermissions.View;
    private const string Manage = PromotionsPermissions.Manage;
    private const string GetPromotionRoute = "GetPromotion";
    private const string GetCouponRoute = "GetCoupon";

    public static RouteGroupBuilder MapPromotionsEndpoints(this IEndpointRouteBuilder routes)
    {
        var promotions = routes.MapGroup("/promotions").WithTags("Promotions");

        promotions.MapGet("/", async (
            PromotionStatus? status, int? page, int? pageSize, IQueryHandler<ListPromotionsQuery, IReadOnlyList<PromotionDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(new ListPromotionsQuery(status, skip, take), ct))))
            .RequirePermission(View);

        promotions.MapPost("/", async (CreatePromotionRequest request, ICommandHandler<CreatePromotionCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreatePromotionCommand(request.Name, request.Rules), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetPromotionRoute, new { id });
        }).RequirePermission(Manage);

        // Avant « /{id} » : routes fixes.
        promotions.MapGet("/coupons/{couponId:guid}", async (Guid couponId, IQueryHandler<GetCouponQuery, CouponDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetCouponQuery(couponId), ct)))
            .WithName(GetCouponRoute)
            .RequirePermission(View);

        promotions.MapPost("/coupons/{couponId:guid}/disable", (Guid couponId, ICommandHandler<DisableCouponCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new DisableCouponCommand(couponId), ct)))
            .RequirePermission(Manage);

        promotions.MapGet("/orders/{orderId:guid}/preview", async (
            Guid orderId, string? couponCode, IQueryHandler<PreviewOrderDiscountsQuery, IReadOnlyList<DiscountPreviewDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new PreviewOrderDiscountsQuery(orderId, couponCode), ct)))
            .RequirePermission(View);

        promotions.MapPost("/orders/{orderId:guid}/apply", async (
            Guid orderId, ApplyPromotionRequest request, ICommandHandler<ApplyPromotionToOrderCommand, PromotionApplicationDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ApplyPromotionToOrderCommand(orderId, request.PromotionId, request.CouponCode), ct)))
            .RequirePermission(PromotionsPermissions.Apply);

        promotions.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetPromotionQuery, PromotionDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetPromotionQuery(id), ct)))
            .WithName(GetPromotionRoute)
            .RequirePermission(View);

        promotions.MapPut("/{id:guid}/rules", (Guid id, ChangeRulesRequest request, ICommandHandler<ChangePromotionRulesCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(
                new ChangePromotionRulesCommand(id, request.Rules, request.Policy ?? RuleChangePolicy.NonDestructiveOnly), ct)))
            .RequirePermission(Manage);

        promotions.MapPost("/{id:guid}/activate", (Guid id, ICommandHandler<ActivatePromotionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ActivatePromotionCommand(id), ct)))
            .RequirePermission(Manage);

        promotions.MapPost("/{id:guid}/pause", (Guid id, ICommandHandler<PausePromotionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new PausePromotionCommand(id), ct)))
            .RequirePermission(Manage);

        promotions.MapPost("/{id:guid}/resume", (Guid id, ICommandHandler<ResumePromotionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ResumePromotionCommand(id), ct)))
            .RequirePermission(Manage);

        promotions.MapPost("/{id:guid}/expire", (Guid id, ICommandHandler<ExpirePromotionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ExpirePromotionCommand(id), ct)))
            .RequirePermission(Manage);

        promotions.MapGet("/{id:guid}/coupons", async (Guid id, IQueryHandler<ListCouponsQuery, IReadOnlyList<CouponDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListCouponsQuery(id), ct)))
            .RequirePermission(View);

        promotions.MapPost("/{id:guid}/coupons", async (
            Guid id, CreateCouponRequest request, ICommandHandler<CreateCouponCommand, Guid> handler, CancellationToken ct) =>
        {
            var couponId = await handler.HandleAsync(new CreateCouponCommand(id, request.Code, request.MaxUses), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(couponId), GetCouponRoute, new { couponId });
        }).RequirePermission(Manage);

        return promotions;
    }

    private static async Task<NoContent> Run(Task command)
    {
        await command;
        return TypedResults.NoContent();
    }
}
