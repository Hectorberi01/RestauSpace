using Promotions.Application.Abstractions;
using Promotions.Domain;

namespace Promotions.Application.Commands;

// Les promotions appliquées aux commandes. L'usage est compté ici (agrégats + registre d'usages, une transaction) ; la
// remise est posée sur la commande par l'hôte, à partir de PromotionApplied (outbox). Les réactions aux faits d'Orders
// sont IDEMPOTENTES et ignorent sans erreur ce qui n'a pas lieu d'être, pour ne jamais bloquer l'outbox d'Orders.

/// <summary>Ce qu'une commande reçoit : la remise calculée par le domaine, à poser sur la commande (source Promotion).</summary>
public sealed record PromotionApplicationDto(Guid PromotionId, Guid OrderId, Guid? CouponId, decimal DiscountAmount, string CurrencyCode, string DiscountReference);

/// <summary>
/// Au comptoir : applique à une commande en brouillon UNE promotion, désignée soit par son id (promotion sans coupon),
/// soit par un code coupon présenté par le client. La remise est calculée par le domaine sur la commande telle qu'elle
/// est maintenant ; une promotion ne compte qu'une fois par commande (rejeu refusé : <c>already_applied_to_order</c>).
/// </summary>
public sealed record ApplyPromotionToOrderCommand(Guid OrderId, Guid? PromotionId = null, string? CouponCode = null) : ICommand<PromotionApplicationDto>;

public sealed class ApplyPromotionToOrderHandler(PromotionsWorkspace workspace, IPromotionOrders orders, IClock clock)
    : ICommandHandler<ApplyPromotionToOrderCommand, PromotionApplicationDto>
{
    public async Task<PromotionApplicationDto> HandleAsync(ApplyPromotionToOrderCommand command, CancellationToken cancellationToken = default)
    {
        var hasCode = !string.IsNullOrWhiteSpace(command.CouponCode);
        DomainException.ThrowIf(hasCode == command.PromotionId.HasValue, PromotionsApplicationErrors.TargetRequired);
        var code = hasCode ? Domain.CouponCode.Create(command.CouponCode) : null;

        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var order = (command.OrderId == Guid.Empty ? null : await orders.FindAsync(tenant, OrderId.From(command.OrderId), cancellationToken))
            ?? throw new NotFoundException(PromotionsApplicationErrors.OrderNotFound);
        DomainException.ThrowIf(!order.AcceptsDiscounts, PromotionsApplicationErrors.OrderNotEditable);

        var coupon = code is null
            ? null
            : await workspace.Coupons.FindByCodeAsync(tenant, code, cancellationToken)
                ?? throw new NotFoundException(PromotionsApplicationErrors.CouponNotFound);
        var promotion = await workspace.LoadPromotionAsync(coupon?.PromotionId.Value ?? command.PromotionId!.Value, cancellationToken);

        var snapshot = PromotionsWorkspace.Snapshot(tenant, order, couponPresented: coupon is not null);
        var counted = await workspace.Ledger.HasUsageForOrderAsync(tenant, promotion.Id, order.OrderId, cancellationToken);
        var discount = promotion.Apply(snapshot, counted, clock);
        if (coupon is not null)
        {
            var couponCounted = await workspace.Ledger.HasUsageForOrderAsync(tenant, coupon.Id, order.OrderId, cancellationToken);
            coupon.Redeem(promotion.ToReference(), order.OrderId, couponCounted, clock);
        }

        await workspace.SaveAsync(cancellationToken);
        return new PromotionApplicationDto(
            promotion.Id.Value, command.OrderId, coupon?.Id.Value, discount.Amount, discount.Currency.Code, promotion.Id.ToString());
    }
}

/// <summary>Ce qu'a donné une réaction : rien, ou ce qui a été écrit.</summary>
public enum PromotionReactionOutcome
{
    Ignored = 1,
    AlreadyDone = 2,
    Done = 3,
}

/// <summary>
/// orders.OrderCancelled : rend tous les usages (promotions et coupons) que la commande détient encore. Rejouable : un
/// second passage ne trouve plus rien.
/// </summary>
public sealed record ReleaseOrderPromotionsCommand(Guid OrderId) : ICommand<PromotionReactionOutcome>;

public sealed class ReleaseOrderPromotionsHandler(PromotionsWorkspace workspace, IOrderPromotionUsages usages, IClock clock)
    : ICommandHandler<ReleaseOrderPromotionsCommand, PromotionReactionOutcome>
{
    public Task<PromotionReactionOutcome> HandleAsync(ReleaseOrderPromotionsCommand command, CancellationToken cancellationToken = default) =>
        PromotionRelease.ReleaseAsync(workspace, usages, command.OrderId, onlyPromotion: null, clock, cancellationToken);
}

/// <summary>
/// Compensation : la remise de la promotion n'a pas pu être posée sur la commande (elle n'accepte plus de remise) ;
/// l'usage de la promotion, et du coupon qui y a mené, est rendu. Rejouable.
/// </summary>
public sealed record RevertPromotionForOrderCommand(Guid OrderId, Guid PromotionId) : ICommand<PromotionReactionOutcome>;

public sealed class RevertPromotionForOrderHandler(PromotionsWorkspace workspace, IOrderPromotionUsages usages, IClock clock)
    : ICommandHandler<RevertPromotionForOrderCommand, PromotionReactionOutcome>
{
    public Task<PromotionReactionOutcome> HandleAsync(RevertPromotionForOrderCommand command, CancellationToken cancellationToken = default) =>
        command.PromotionId == Guid.Empty
            ? Task.FromResult(PromotionReactionOutcome.Ignored)
            : PromotionRelease.ReleaseAsync(workspace, usages, command.OrderId, PromotionId.From(command.PromotionId), clock, cancellationToken);
}

internal static class PromotionRelease
{
    public static async Task<PromotionReactionOutcome> ReleaseAsync(
        PromotionsWorkspace workspace,
        IOrderPromotionUsages usages,
        Guid orderGuid,
        PromotionId? onlyPromotion,
        IClock clock,
        CancellationToken cancellationToken)
    {
        if (orderGuid == Guid.Empty)
        {
            return PromotionReactionOutcome.Ignored;
        }

        var tenant = workspace.CurrentTenant;
        var orderId = OrderId.From(orderGuid);
        var released = 0;

        foreach (var promotionId in await usages.PromotionsHeldByAsync(tenant, orderId, cancellationToken))
        {
            if (onlyPromotion is { } only && promotionId != only)
            {
                continue;
            }

            var promotion = await workspace.Promotions.FindAsync(tenant, promotionId, cancellationToken);
            if (promotion is null)
            {
                continue;
            }

            promotion.RevertUsage(orderId, await workspace.Ledger.HasUsageForOrderAsync(tenant, promotionId, orderId, cancellationToken), clock);
            released++;
        }

        foreach (var couponId in await usages.CouponsHeldByAsync(tenant, orderId, cancellationToken))
        {
            var coupon = await workspace.Coupons.FindAsync(tenant, couponId, cancellationToken);
            if (coupon is null || (onlyPromotion is { } only && coupon.PromotionId != only))
            {
                continue;
            }

            coupon.Release(orderId, await workspace.Ledger.HasUsageForOrderAsync(tenant, couponId, orderId, cancellationToken), clock);
            released++;
        }

        if (released == 0)
        {
            return PromotionReactionOutcome.AlreadyDone;
        }

        await workspace.SaveAsync(cancellationToken);
        return PromotionReactionOutcome.Done;
    }
}

/// <summary>
/// Expiration automatique (job, pour le tenant du contexte) : les promotions non expirées dont la période est terminée
/// passent à Expired. Au plus <paramref name="BatchSize"/> par passage ; renvoie le nombre expiré.
/// </summary>
public sealed record ExpireEndedPromotionsCommand(int BatchSize = 200) : ICommand<int>;

public sealed class ExpireEndedPromotionsHandler(PromotionsWorkspace workspace, IClock clock) : ICommandHandler<ExpireEndedPromotionsCommand, int>
{
    public async Task<int> HandleAsync(ExpireEndedPromotionsCommand command, CancellationToken cancellationToken = default)
    {
        if (!await workspace.AcceptsWritesAsync(cancellationToken))
        {
            return 0;
        }

        var ended = await workspace.Promotions.ListEndedAsync(
            workspace.CurrentTenant, clock.UtcNow, Math.Clamp(command.BatchSize, 1, 1000), cancellationToken);
        foreach (var promotion in ended)
        {
            promotion.Expire(clock);
        }

        if (ended.Count > 0)
        {
            await workspace.SaveAsync(cancellationToken);
        }

        return ended.Count;
    }
}
