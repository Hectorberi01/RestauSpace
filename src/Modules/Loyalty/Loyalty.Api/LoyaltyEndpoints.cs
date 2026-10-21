using Loyalty.Application;
using Loyalty.Application.Commands;
using Loyalty.Application.Queries;
using Loyalty.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using SaasRestaurant.BuildingBlocks.Api;

namespace Loyalty.Api;

/// <summary>
/// Programme de fidélité du restaurant sélectionné dans l'access token. Aucune route ne prend de tenant : ce n'est jamais
/// une valeur de requête. L'hôte chaîne l'autorisation (<c>Tenancy.TenantMemberPolicy</c>) sur le groupe renvoyé.
/// Lecture : <see cref="LoyaltyPermissions.View"/> ; comptoir (ouvrir un compte, utiliser une récompense) :
/// <see cref="LoyaltyPermissions.Operate"/> ; corrections : <see cref="LoyaltyPermissions.Manage"/> ; barème et
/// catalogue : <see cref="LoyaltyPermissions.Configure"/>. Gagner, reprendre et expirer des points n'ont pas de route :
/// ce sont des réactions aux commandes et un job.
/// </summary>
public static class LoyaltyEndpoints
{
    private const string View = LoyaltyPermissions.View;
    private const string Operate = LoyaltyPermissions.Operate;
    private const string Manage = LoyaltyPermissions.Manage;
    private const string Configure = LoyaltyPermissions.Configure;
    private const string GetAccountRoute = "GetLoyaltyAccount";
    private const string GetRewardRoute = "GetLoyaltyReward";
    private const string GetStoredValueCardRoute = "GetStoredValueCard";

    public static RouteGroupBuilder MapLoyaltyEndpoints(this IEndpointRouteBuilder routes)
    {
        var loyalty = routes.MapGroup("/loyalty").WithTags("Loyalty");

        loyalty.MapGet("/program", async (IQueryHandler<GetLoyaltyProgramQuery, LoyaltyProgramDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetLoyaltyProgramQuery(), ct)))
            .RequirePermission(View);

        loyalty.MapPut("/program", (LoyaltyProgramRequest request, ICommandHandler<SetLoyaltyProgramCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(
                new SetLoyaltyProgramCommand(request.IsEnabled, request.PointsPerCurrencyUnit, request.AutoEnroll, request.InactivityExpiryDays), ct)))
            .RequirePermission(Configure);

        MapRewards(loyalty.MapGroup("/rewards"));
        MapAccounts(loyalty.MapGroup("/accounts"));
        MapStoredValueCards(loyalty.MapGroup("/stored-value-cards"));
        return loyalty;
    }

    private static void MapRewards(RouteGroupBuilder rewards)
    {
        rewards.MapGet("/", async (bool? activeOnly, IQueryHandler<ListRewardsQuery, IReadOnlyList<RewardDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListRewardsQuery(activeOnly ?? false), ct)))
            .RequirePermission(View);

        rewards.MapPost("/", async (CreateRewardRequest request, ICommandHandler<CreateRewardCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new CreateRewardCommand(request.Name, request.PointCost, request.DiscountAmount, request.CurrencyCode), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetRewardRoute, new { id });
        }).RequirePermission(Configure);

        rewards.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetRewardQuery, RewardDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetRewardQuery(id), ct)))
            .WithName(GetRewardRoute)
            .RequirePermission(View);

        rewards.MapPut("/{id:guid}/cost", (Guid id, RewardCostRequest request, ICommandHandler<ChangeRewardCostCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeRewardCostCommand(id, request.PointCost), ct)))
            .RequirePermission(Configure);

        rewards.MapPut("/{id:guid}/discount-value", (
            Guid id, RewardDiscountValueRequest request, ICommandHandler<ChangeRewardDiscountValueCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ChangeRewardDiscountValueCommand(id, request.DiscountAmount, request.CurrencyCode), ct)))
            .RequirePermission(Configure);

        rewards.MapPost("/{id:guid}/activate", (Guid id, ICommandHandler<ActivateRewardCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ActivateRewardCommand(id), ct)))
            .RequirePermission(Configure);

        rewards.MapPost("/{id:guid}/deactivate", (Guid id, ICommandHandler<DeactivateRewardCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new DeactivateRewardCommand(id), ct)))
            .RequirePermission(Configure);
    }

    private static void MapAccounts(RouteGroupBuilder accounts)
    {
        accounts.MapGet("/", async (
            LoyaltyAccountStatus? status, int? page, int? pageSize,
            IQueryHandler<ListLoyaltyAccountsQuery, IReadOnlyList<LoyaltyAccountDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(new ListLoyaltyAccountsQuery(status, skip, take), ct))))
            .RequirePermission(View);

        accounts.MapPost("/", async (OpenAccountRequest request, ICommandHandler<OpenLoyaltyAccountCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new OpenLoyaltyAccountCommand(request.CustomerId), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetAccountRoute, new { id });
        }).RequirePermission(Operate);

        // Avant « /{id} » : route fixe.
        accounts.MapGet("/by-customer/{customerId:guid}", async (
            Guid customerId, IQueryHandler<GetLoyaltyAccountByCustomerQuery, LoyaltyAccountDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetLoyaltyAccountByCustomerQuery(customerId), ct)))
            .RequirePermission(View);

        accounts.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetLoyaltyAccountQuery, LoyaltyAccountDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetLoyaltyAccountQuery(id), ct)))
            .WithName(GetAccountRoute)
            .RequirePermission(View);

        accounts.MapGet("/{id:guid}/transactions", async (
            Guid id, int? page, int? pageSize,
            IQueryHandler<ListLoyaltyTransactionsQuery, IReadOnlyList<LoyaltyTransactionDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(new ListLoyaltyTransactionsQuery(id, skip, take), ct))))
            .RequirePermission(View);

        accounts.MapPost("/{id:guid}/rewards/{rewardId:guid}/redeem", async (
            Guid id, Guid rewardId, RedeemRewardRequest request,
            ICommandHandler<RedeemRewardForOrderCommand, RewardRedemptionDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new RedeemRewardForOrderCommand(id, rewardId, request.OrderId), ct)))
            .RequirePermission(Operate);

        accounts.MapPost("/{id:guid}/adjustments", (
            Guid id, AdjustPointsRequest request, ICommandHandler<AdjustLoyaltyPointsCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new AdjustLoyaltyPointsCommand(id, request.Points, request.Direction, request.Reason, request.Reference), ct)))
            .RequirePermission(Manage);

        accounts.MapPost("/{id:guid}/suspend", (Guid id, ICommandHandler<SuspendLoyaltyAccountCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SuspendLoyaltyAccountCommand(id), ct)))
            .RequirePermission(Manage);

        accounts.MapPost("/{id:guid}/reactivate", (Guid id, ICommandHandler<ReactivateLoyaltyAccountCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ReactivateLoyaltyAccountCommand(id), ct)))
            .RequirePermission(Manage);

        accounts.MapPost("/{id:guid}/close", (Guid id, ReasonRequest request, ICommandHandler<CloseLoyaltyAccountCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CloseLoyaltyAccountCommand(id, request.Reason), ct)))
            .RequirePermission(Manage);
    }

    /// <summary>
    /// Cartes cadeaux et avoirs. Le code complet ne sort qu'à l'émission ; la recherche par code passe dans le corps
    /// d'un POST, jamais dans l'URL. Émettre et annuler : <see cref="LoyaltyPermissions.Manage"/>.
    /// </summary>
    private static void MapStoredValueCards(RouteGroupBuilder cards)
    {
        cards.MapGet("/", async (
            StoredValueKind? kind, Guid? customerId, int? page, int? pageSize,
            IQueryHandler<ListStoredValueCardsQuery, IReadOnlyList<StoredValueCardDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(new ListStoredValueCardsQuery(kind, customerId, skip, take), ct))))
            .RequirePermission(View);

        cards.MapPost("/", async (IssueStoredValueCardRequest request, ICommandHandler<IssueStoredValueCardCommand, IssuedStoredValueCardDto> handler, CancellationToken ct) =>
        {
            var issued = await handler.HandleAsync(
                new IssueStoredValueCardCommand(request.Kind, request.Amount, request.CurrencyCode, request.ExpiresAtUtc, request.CustomerId, request.Note), ct);
            return TypedResults.CreatedAtRoute(issued, GetStoredValueCardRoute, new { id = issued.Id });
        }).RequirePermission(Manage);

        cards.MapPost("/lookup", async (StoredValueLookupRequest request, IQueryHandler<LookupStoredValueCardQuery, StoredValueCardDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new LookupStoredValueCardQuery(request.Code), ct)))
            .RequirePermission(View);

        cards.MapGet("/{id:guid}", async (Guid id, IQueryHandler<GetStoredValueCardQuery, StoredValueCardDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetStoredValueCardQuery(id), ct)))
            .WithName(GetStoredValueCardRoute)
            .RequirePermission(View);

        cards.MapPost("/{id:guid}/cancel", (Guid id, ReasonRequest request, ICommandHandler<CancelStoredValueCardCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CancelStoredValueCardCommand(id, request.Reason), ct)))
            .RequirePermission(Manage);
    }

    private static async Task<NoContent> Run(Task command)
    {
        await command;
        return TypedResults.NoContent();
    }
}
