using Promotions.Domain;

namespace Promotions.Application.Commands;

// Le catalogue des promotions et de leurs coupons, vu par le personnel. Les règles (cycle de vie, modification après
// usage, unicité du code) sont celles des agrégats ; ici, l'ordre des étapes.

/// <summary>Crée une promotion en brouillon. Renvoie l'id.</summary>
public sealed record CreatePromotionCommand(string? Name, PromotionRulesInput Rules) : ICommand<Guid>;

public sealed class CreatePromotionHandler(PromotionsWorkspace workspace, IClock clock) : ICommandHandler<CreatePromotionCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreatePromotionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command.Rules);
        var rules = command.Rules.ToRules();
        await workspace.RequireWritableTenantAsync(cancellationToken);
        await workspace.RequireOwnTargetsAsync(rules, cancellationToken);

        var promotion = Promotion.Create(workspace.CurrentTenant, command.Name ?? string.Empty, rules, clock);
        workspace.Promotions.Add(promotion);
        await workspace.SaveAsync(cancellationToken);
        return promotion.Id.Value;
    }
}

/// <summary>
/// Remplace les règles. Une fois la promotion utilisée, la policy décide (par défaut, seulement une fin plus tardive et une
/// limite plus haute) ; <c>Unrestricted</c> est une dérogation explicite.
/// </summary>
public sealed record ChangePromotionRulesCommand(Guid PromotionId, PromotionRulesInput Rules, RuleChangePolicy Policy = RuleChangePolicy.NonDestructiveOnly)
    : ICommand;

public sealed class ChangePromotionRulesHandler(PromotionsWorkspace workspace, IClock clock) : ICommandHandler<ChangePromotionRulesCommand>
{
    public async Task HandleAsync(ChangePromotionRulesCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command.Rules);
        var rules = command.Rules.ToRules();
        await workspace.RequireOwnTargetsAsync(rules, cancellationToken);
        await workspace.EditPromotionAsync(command.PromotionId, promotion => promotion.ChangeRules(rules, clock, command.Policy), cancellationToken);
    }
}

public sealed record ActivatePromotionCommand(Guid PromotionId) : ICommand;

public sealed class ActivatePromotionHandler(PromotionsWorkspace workspace, IClock clock) : ICommandHandler<ActivatePromotionCommand>
{
    public Task HandleAsync(ActivatePromotionCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditPromotionAsync(command.PromotionId, promotion => promotion.Activate(clock), cancellationToken);
}

public sealed record PausePromotionCommand(Guid PromotionId) : ICommand;

public sealed class PausePromotionHandler(PromotionsWorkspace workspace, IClock clock) : ICommandHandler<PausePromotionCommand>
{
    public Task HandleAsync(PausePromotionCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditPromotionAsync(command.PromotionId, promotion => promotion.Pause(clock), cancellationToken);
}

public sealed record ResumePromotionCommand(Guid PromotionId) : ICommand;

public sealed class ResumePromotionHandler(PromotionsWorkspace workspace, IClock clock) : ICommandHandler<ResumePromotionCommand>
{
    public Task HandleAsync(ResumePromotionCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditPromotionAsync(command.PromotionId, promotion => promotion.Resume(clock), cancellationToken);
}

/// <summary>Arrêt définitif, avant ou après la fin de la période (le job le fait seul à la fin de la période).</summary>
public sealed record ExpirePromotionCommand(Guid PromotionId) : ICommand;

public sealed class ExpirePromotionHandler(PromotionsWorkspace workspace, IClock clock) : ICommandHandler<ExpirePromotionCommand>
{
    public Task HandleAsync(ExpirePromotionCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditPromotionAsync(command.PromotionId, promotion => promotion.Expire(clock), cancellationToken);
}

/// <summary>
/// Crée un coupon de la promotion : code normalisé (majuscules), unique dans le restaurant, nombre d'utilisations
/// maximal. Une promotion expirée n'en reçoit plus. Renvoie l'id.
/// </summary>
public sealed record CreateCouponCommand(Guid PromotionId, string? Code, int MaxUses) : ICommand<Guid>;

public sealed class CreateCouponHandler(PromotionsWorkspace workspace, ICouponCodeUniquenessChecker uniqueness, IClock clock)
    : ICommandHandler<CreateCouponCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateCouponCommand command, CancellationToken cancellationToken = default)
    {
        var code = CouponCode.Create(command.Code);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var promotion = await workspace.LoadPromotionAsync(command.PromotionId, cancellationToken);

        var isUnique = await uniqueness.IsUniqueAsync(tenant, code, cancellationToken);
        var coupon = Coupon.Create(tenant, promotion.ToReference(), code, command.MaxUses, isUnique, clock);
        workspace.Coupons.Add(coupon);
        await workspace.SaveAsync(cancellationToken);
        return coupon.Id.Value;
    }
}

public sealed record DisableCouponCommand(Guid CouponId) : ICommand;

public sealed class DisableCouponHandler(PromotionsWorkspace workspace, IClock clock) : ICommandHandler<DisableCouponCommand>
{
    public async Task HandleAsync(DisableCouponCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        (await workspace.LoadCouponAsync(command.CouponId, cancellationToken)).Disable(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}
