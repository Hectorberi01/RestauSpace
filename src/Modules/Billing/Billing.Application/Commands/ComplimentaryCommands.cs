using Billing.Domain;

namespace Billing.Application.Commands;

// Les plans offerts par la plateforme : un plan gratuit pour une période ou sans fin. Le tenant est désigné par
// l'administrateur, jamais lu dans une requête de restaurant.

/// <summary>
/// La plateforme offre <paramref name="PlanId"/> au restaurant jusqu'à <paramref name="UntilUtc"/> (null : sans fin),
/// renouvelé seul de <paramref name="RenewalMonths"/> mois si renseigné. Le restaurant sans abonnement vivant en reçoit
/// un ; sinon son abonnement passe au plan offert, quel que soit son état (impayé et suspendu compris). Renvoie l'id de
/// l'abonnement.
/// </summary>
public sealed record AdminGrantComplimentaryCommand(Guid TenantId, Guid PlanId, DateTimeOffset? UntilUtc, int? RenewalMonths = null) : ICommand<Guid>;

public sealed class AdminGrantComplimentaryHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<AdminGrantComplimentaryCommand, Guid>
{
    private const int MaxSubscriptionsRead = 20;

    public async Task<Guid> HandleAsync(AdminGrantComplimentaryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = command.TenantId == Guid.Empty ? (TenantId?)null : TenantId.From(command.TenantId);
        if (tenant is null || !await workspace.Tenants.ExistsAsync(tenant.Value, cancellationToken))
        {
            throw new NotFoundException(BillingApplicationErrors.TenantNotFound);
        }

        var plan = await workspace.LoadPlanAsync(command.PlanId, cancellationToken);
        await workspace.EnsurePlanCurrencyAsync(tenant.Value, plan, cancellationToken);
        var live = (await workspace.Platform.ListAsync(tenant.Value, null, 0, MaxSubscriptionsRead, cancellationToken))
            .FirstOrDefault(candidate => candidate.Status != SubscriptionStatus.Cancelled);
        var subscription = live is null ? null : await workspace.Platform.FindAsync(live.Id, cancellationToken);
        if (subscription is null)
        {
            var now = clock.UtcNow;
            subscription = SaasSubscription.StartTrial(
                tenant.Value, plan.ToReference(), Period.Create(now, now + workspace.Settings.TrialLength),
                await workspace.LiveSubscriptions.HasNoLiveSubscriptionAsync(tenant.Value, cancellationToken), clock);
            workspace.Subscriptions.Add(subscription);
        }

        subscription.GrantComplimentary(plan.ToReference(), command.UntilUtc, clock, command.RenewalMonths);
        await workspace.SaveAsync(cancellationToken);
        return subscription.Id.Value;
    }
}

/// <summary>La plateforme met fin à l'offre maintenant : le restaurant règle la période suivante (impayé, puis délai de grâce).</summary>
public sealed record AdminEndComplimentaryCommand(Guid SubscriptionId) : ICommand;

public sealed class AdminEndComplimentaryHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<AdminEndComplimentaryCommand>
{
    public async Task HandleAsync(AdminEndComplimentaryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        (await workspace.LoadAnySubscriptionAsync(command.SubscriptionId, cancellationToken)).EndComplimentary(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Le job : prévient le restaurant (contexte du job) que son essai ou son offre se termine bientôt. Vrai si un préavis part ;
/// un plan gratuit payé (offre), qui se renouvelle seul, n'est pas prévenu.
/// </summary>
public sealed record NoticeSubscriptionEndingCommand(Guid SubscriptionId) : ICommand<bool>;

public sealed class NoticeSubscriptionEndingHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<NoticeSubscriptionEndingCommand, bool>
{
    public async Task<bool> HandleAsync(NoticeSubscriptionEndingCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var subscription = command.SubscriptionId == Guid.Empty
            ? null
            : await workspace.Subscriptions.FindAsync(workspace.CurrentTenant, SubscriptionId.From(command.SubscriptionId), cancellationToken);
        if (subscription is null)
        {
            return false;
        }

        var plan = await workspace.Plans.FindAsync(subscription.PlanId, cancellationToken);
        // Seul un plan gratuit déjà payé (offre) se renouvelle seul ; la fin du mois Découverte est annoncée.
        var free = subscription.Status == SubscriptionStatus.Active
            && plan is not null && (await workspace.ChargeAsync(plan, subscription.TenantId, cancellationToken)).Amount == 0m;
        if (!subscription.NoticeEnding(clock, silently: free))
        {
            return false;
        }

        await workspace.SaveAsync(cancellationToken);
        return !free;
    }
}

// --- Options offertes.

/// <summary>
/// La plateforme offre une option au restaurant (abonnement vivant requis). Déjà active : son offre est remplacée et sa
/// quantité ajustée ; sinon elle est ajoutée, offerte. Renvoie l'id de l'option.
/// </summary>
public sealed record AdminOfferAddOnCommand(
    Guid TenantId, Guid PlanId, int Quantity, DateTimeOffset? UntilUtc, int? RenewalMonths, AddOnAfterOffer Then) : ICommand<Guid>;

public sealed class AdminOfferAddOnHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<AdminOfferAddOnCommand, Guid>
{
    public async Task<Guid> HandleAsync(AdminOfferAddOnCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = await TenantAddOns.RequireTenantAsync(workspace, command.TenantId, cancellationToken);
        DomainException.ThrowIf(
            await workspace.Subscriptions.FindLiveAsync(tenant, cancellationToken) is null, BillingApplicationErrors.AddOnNeedsSubscription);
        var offer = new AddOnOffer(command.UntilUtc, command.RenewalMonths, command.Then);
        var planId = command.PlanId == Guid.Empty ? (PlanId?)null : PlanId.From(command.PlanId);
        if (planId is { } known && await workspace.AddOns.FindActiveAsync(tenant, known, cancellationToken) is { } existing)
        {
            existing.ChangeQuantity(command.Quantity, clock);
            existing.GrantComplimentary(offer, clock);
            await workspace.SaveAsync(cancellationToken);
            return existing.Id.Value;
        }

        var id = await TenantAddOns.ActivateAsync(workspace, tenant, command.PlanId, command.Quantity, clock, cancellationToken, offer);
        return id;
    }
}

/// <summary>La plateforme met fin à l'offre d'une option maintenant : elle s'arrête ou devient payante, selon l'offre.</summary>
public sealed record AdminEndAddOnOfferCommand(Guid TenantId, Guid AddOnId) : ICommand;

public sealed class AdminEndAddOnOfferHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<AdminEndAddOnOfferCommand>
{
    public async Task HandleAsync(AdminEndAddOnOfferCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        (await TenantAddOns.LoadAsync(workspace, command.TenantId, command.AddOnId, cancellationToken)).EndComplimentary(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Le job, pour une option offerte échue de son tenant (contexte du job) : renouvelée, arrêtée ou rendue payante.</summary>
public sealed record ProcessAddOnOfferCommand(Guid AddOnId) : ICommand<bool>;

public sealed class ProcessAddOnOfferHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<ProcessAddOnOfferCommand, bool>
{
    public async Task<bool> HandleAsync(ProcessAddOnOfferCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var addOn = command.AddOnId == Guid.Empty
            ? null
            : await workspace.AddOns.FindAsync(workspace.CurrentTenant, TenantAddOnId.From(command.AddOnId), cancellationToken);
        if (addOn is null || !addOn.ProcessComplimentaryEnd(clock))
        {
            return false;
        }

        await workspace.SaveAsync(cancellationToken);
        return true;
    }
}
