using Billing.Domain;

namespace Billing.Application.Commands;

// Les options d'un restaurant (vente en ligne, bornes…). Côté restaurant : le tenant du contexte, souscription immédiate
// pendant l'essai ou une période payée, facturée avec l'abonnement. Côté plateforme : le tenant est désigné par
// l'administrateur, jamais lu dans une requête de restaurant.

/// <summary>Ajoute une option (plan de type option, actif, dans la devise du restaurant) à un restaurant abonné.</summary>
public sealed record ActivateTenantAddOnCommand(Guid TenantId, Guid PlanId, int Quantity = 1) : ICommand<Guid>;

public sealed class ActivateTenantAddOnHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<ActivateTenantAddOnCommand, Guid>
{
    public async Task<Guid> HandleAsync(ActivateTenantAddOnCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = await TenantAddOns.RequireTenantAsync(workspace, command.TenantId, cancellationToken);
        DomainException.ThrowIf(
            await workspace.Subscriptions.FindLiveAsync(tenant, cancellationToken) is null, BillingApplicationErrors.AddOnNeedsSubscription);
        return await TenantAddOns.ActivateAsync(workspace, tenant, command.PlanId, command.Quantity, clock, cancellationToken);
    }
}

public sealed record ChangeTenantAddOnQuantityCommand(Guid TenantId, Guid AddOnId, int Quantity) : ICommand;

public sealed class ChangeTenantAddOnQuantityHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<ChangeTenantAddOnQuantityCommand>
{
    public async Task HandleAsync(ChangeTenantAddOnQuantityCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var addOn = await TenantAddOns.LoadAsync(workspace, command.TenantId, command.AddOnId, cancellationToken);
        addOn.ChangeQuantity(command.Quantity, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record CancelTenantAddOnCommand(Guid TenantId, Guid AddOnId) : ICommand;

public sealed class CancelTenantAddOnHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<CancelTenantAddOnCommand>
{
    public async Task HandleAsync(CancelTenantAddOnCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        (await TenantAddOns.LoadAsync(workspace, command.TenantId, command.AddOnId, cancellationToken)).Cancel(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

// --- Restaurant : le tenant du contexte.

/// <summary>
/// Le restaurant souscrit une option (plan d'option actif de sa devise) : elle est active tout de suite et facturée avec
/// la prochaine période payée. Seulement pendant l'essai ou une période payée : en retard ou suspendu, il règle d'abord.
/// </summary>
public sealed record SubscribeAddOnCommand(Guid PlanId, int Quantity = 1) : ICommand<Guid>;

public sealed class SubscribeAddOnHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<SubscribeAddOnCommand, Guid>
{
    public async Task<Guid> HandleAsync(SubscribeAddOnCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        await TenantAddOns.RequireOpenSubscriptionAsync(workspace, cancellationToken);
        DomainException.ThrowIf(
            (await workspace.Subscriptions.FindLiveAsync(workspace.CurrentTenant, cancellationToken))?.IsComplimentaryUnlimited == true,
            BillingApplicationErrors.AddOnComplimentaryUnlimited);
        return await TenantAddOns.ActivateAsync(workspace, workspace.CurrentTenant, command.PlanId, command.Quantity, clock, cancellationToken);
    }
}

/// <summary>Le restaurant change la quantité d'une de ses options (ex. nombre de bornes).</summary>
public sealed record ChangeMyAddOnQuantityCommand(Guid AddOnId, int Quantity) : ICommand;

public sealed class ChangeMyAddOnQuantityHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<ChangeMyAddOnQuantityCommand>
{
    public async Task HandleAsync(ChangeMyAddOnQuantityCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        await TenantAddOns.RequireOpenSubscriptionAsync(workspace, cancellationToken);
        (await TenantAddOns.LoadMineAsync(workspace, command.AddOnId, cancellationToken)).ChangeQuantity(command.Quantity, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Le restaurant résilie une de ses options : ses droits cessent aussitôt, elle n'est plus facturée.</summary>
public sealed record CancelMyAddOnCommand(Guid AddOnId) : ICommand;

public sealed class CancelMyAddOnHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<CancelMyAddOnCommand>
{
    public async Task HandleAsync(CancelMyAddOnCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        // Pas d'exigence d'écriture : un restaurant suspendu peut toujours réduire ce qu'il paie.
        (await TenantAddOns.LoadMineAsync(workspace, command.AddOnId, cancellationToken)).Cancel(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

internal static class TenantAddOns
{
    public static async Task<Guid> ActivateAsync(
        BillingWorkspace workspace, TenantId tenant, Guid planId, int quantity, IClock clock, CancellationToken cancellationToken)
    {
        var plan = await workspace.LoadPlanAsync(planId, cancellationToken);
        await workspace.EnsurePlanCurrencyAsync(tenant, plan, cancellationToken);
        var addOn = TenantAddOn.Activate(
            tenant,
            plan.ToReference(),
            quantity,
            await workspace.AddOns.FindActiveAsync(tenant, plan.Id, cancellationToken) is null,
            clock);
        workspace.AddOns.Add(addOn);
        await workspace.SaveAsync(cancellationToken);
        return addOn.Id.Value;
    }

    /// <summary>Un abonnement en essai ou en période payée : sinon le restaurant doit d'abord régulariser.</summary>
    public static async Task RequireOpenSubscriptionAsync(BillingWorkspace workspace, CancellationToken cancellationToken)
    {
        var subscription = await workspace.Subscriptions.FindLiveAsync(workspace.CurrentTenant, cancellationToken)
            ?? throw new DomainException(BillingApplicationErrors.AddOnNeedsSubscription);
        DomainException.ThrowIf(
            subscription.Status is not (SubscriptionStatus.Trialing or SubscriptionStatus.Active), BillingApplicationErrors.AddOnNeedsOpenSubscription);
    }

    public static async Task<TenantAddOn> LoadMineAsync(BillingWorkspace workspace, Guid addOnId, CancellationToken cancellationToken) =>
        (addOnId == Guid.Empty ? null : await workspace.AddOns.FindAsync(workspace.CurrentTenant, TenantAddOnId.From(addOnId), cancellationToken))
        ?? throw new NotFoundException(BillingApplicationErrors.AddOnNotFound);

    public static async Task<TenantId> RequireTenantAsync(BillingWorkspace workspace, Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = tenantId == Guid.Empty ? (TenantId?)null : TenantId.From(tenantId);
        return tenant is { } known && await workspace.Tenants.ExistsAsync(known, cancellationToken)
            ? known
            : throw new NotFoundException(BillingApplicationErrors.TenantNotFound);
    }

    public static async Task<TenantAddOn> LoadAsync(BillingWorkspace workspace, Guid tenantId, Guid addOnId, CancellationToken cancellationToken)
    {
        var tenant = await RequireTenantAsync(workspace, tenantId, cancellationToken);
        return (addOnId == Guid.Empty ? null : await workspace.AddOns.FindAsync(tenant, TenantAddOnId.From(addOnId), cancellationToken))
            ?? throw new NotFoundException(BillingApplicationErrors.AddOnNotFound);
    }
}
