using Billing.Domain;

namespace Billing.Application.Commands;

// L'abonnement du restaurant, côté restaurant (tenant du contexte) et côté plateforme (tenant désigné). Les règles du
// cycle de vie sont celles de SaasSubscription ; ici, le calcul des périodes et l'ordre des étapes.

/// <summary>
/// Le restaurant choisit son plan : un essai démarre (durée <see cref="BillingSettings.TrialLength"/>). Un seul
/// abonnement vivant par restaurant. Renvoie l'id de l'abonnement.
/// </summary>
public sealed record StartSubscriptionCommand(Guid PlanId) : ICommand<Guid>;

public sealed class StartSubscriptionHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<StartSubscriptionCommand, Guid>
{
    public async Task<Guid> HandleAsync(StartSubscriptionCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        return await SubscriptionStart.StartAsync(workspace, workspace.CurrentTenant, command.PlanId, workspace.Settings.TrialLength, clock, cancellationToken);
    }
}

/// <summary>Le plan gratuit (Découverte) : un plan de base à 0.</summary>
internal static class FreePlan
{
    public static bool Is(Plan plan) => plan.Price.Amount == 0m;
}

internal static class SubscriptionStart
{
    public static async Task<Guid> StartAsync(
        BillingWorkspace workspace, TenantId tenant, Guid planId, TimeSpan trialLength, IClock clock, CancellationToken cancellationToken)
    {
        var plan = await workspace.LoadPlanAsync(planId, cancellationToken);
        await workspace.EnsurePlanCurrencyAsync(tenant, plan, cancellationToken);
        var now = clock.UtcNow;
        var end = now + trialLength;
        if (FreePlan.Is(plan))
        {
            // Plan gratuit : une seule fois, pour un mois.
            DomainException.ThrowIf(await workspace.Subscriptions.HasOtherAsync(tenant, null, cancellationToken), BillingApplicationErrors.FreePlanUnavailable);
            end = now.AddMonths(workspace.Settings.FreePlanMonths);
        }

        var isFirst = await workspace.LiveSubscriptions.HasNoLiveSubscriptionAsync(tenant, cancellationToken);
        var subscription = SaasSubscription.StartTrial(tenant, plan.ToReference(), Period.Create(now, end), isFirst, clock);
        workspace.Subscriptions.Add(subscription);
        await workspace.SaveAsync(cancellationToken);
        return subscription.Id.Value;
    }
}

/// <summary>
/// Passe à un autre plan actif, pendant l'essai, une période payée ou après un essai expiré (même plan = rien). Le plan
/// gratuit : seulement pendant l'essai du premier abonnement, sans dépasser son premier mois.
/// </summary>
public sealed record ChangeSubscriptionPlanCommand(Guid PlanId) : ICommand;

public sealed class ChangeSubscriptionPlanHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<ChangeSubscriptionPlanCommand>
{
    public async Task HandleAsync(ChangeSubscriptionPlanCommand command, CancellationToken cancellationToken = default)
    {
        var subscription = await workspace.LoadLiveSubscriptionAsync(cancellationToken);
        // Essai expiré : le restaurant est suspendu, mais doit pouvoir choisir un plan payant.
        if (subscription.SuspensionReason != SuspensionReason.TrialExpired)
        {
            await workspace.RequireWritableTenantAsync(cancellationToken);
        }

        var plan = await workspace.LoadPlanAsync(command.PlanId, cancellationToken);
        await workspace.EnsurePlanCurrencyAsync(subscription.TenantId, plan, cancellationToken);
        if (FreePlan.Is(plan) && plan.Id != subscription.PlanId)
        {
            DomainException.ThrowIf(
                subscription.Status != SubscriptionStatus.Trialing
                || subscription.CurrentPeriod.EndUtc > subscription.StartedAtUtc.AddMonths(workspace.Settings.FreePlanMonths)
                || await workspace.Subscriptions.HasOtherAsync(subscription.TenantId, subscription.Id, cancellationToken),
                BillingApplicationErrors.FreePlanUnavailable);
        }

        subscription.ChangePlan(plan.ToReference(), clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Le restaurant résilie à la fin de la période courante (réversible jusque-là).</summary>
public sealed record ScheduleSubscriptionCancellationCommand : ICommand;

public sealed class ScheduleSubscriptionCancellationHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<ScheduleSubscriptionCancellationCommand>
{
    public async Task HandleAsync(ScheduleSubscriptionCancellationCommand command, CancellationToken cancellationToken = default)
    {
        // Pas d'exigence d'écriture : un restaurant suspendu peut toujours décider de partir.
        (await workspace.LoadLiveSubscriptionAsync(cancellationToken)).ScheduleCancellation(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record UnscheduleSubscriptionCancellationCommand : ICommand;

public sealed class UnscheduleSubscriptionCancellationHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<UnscheduleSubscriptionCancellationCommand>
{
    public async Task HandleAsync(UnscheduleSubscriptionCancellationCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        (await workspace.LoadLiveSubscriptionAsync(cancellationToken)).UnscheduleCancellation(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

// --- Plateforme (routes d'administration) : le tenant est désigné, l'abonnement lu sans filtre de contexte.

/// <summary>La plateforme ouvre l'abonnement d'un restaurant : essai de <paramref name="TrialDays"/> jours (défaut du réglage).</summary>
public sealed record AdminStartSubscriptionCommand(Guid TenantId, Guid PlanId, int? TrialDays = null) : ICommand<Guid>;

public sealed class AdminStartSubscriptionHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<AdminStartSubscriptionCommand, Guid>
{
    public const int MaxTrialDays = 90;

    public async Task<Guid> HandleAsync(AdminStartSubscriptionCommand command, CancellationToken cancellationToken = default)
    {
        DomainException.ThrowIf(command.TrialDays is < 1 or > MaxTrialDays, BillingApplicationErrors.TrialLengthInvalid);
        var tenant = command.TenantId == Guid.Empty ? (TenantId?)null : TenantId.From(command.TenantId);
        if (tenant is null || !await workspace.Tenants.ExistsAsync(tenant.Value, cancellationToken))
        {
            throw new NotFoundException(BillingApplicationErrors.TenantNotFound);
        }

        var trial = command.TrialDays is { } days ? TimeSpan.FromDays(days) : workspace.Settings.TrialLength;
        return await SubscriptionStart.StartAsync(workspace, tenant.Value, command.PlanId, trial, clock, cancellationToken);
    }
}

/// <summary>Ce que paie l'enregistrement d'un paiement : la période désormais payée.</summary>
public sealed record SubscriptionPaymentDto(Guid SubscriptionId, SubscriptionStatus Status, DateTimeOffset PaidFromUtc, DateTimeOffset PaidUntilUtc);

/// <summary>
/// La plateforme enregistre le paiement de la période due (en attendant un prestataire de paiement SaaS) : le prix du
/// plan et des options actives à ce moment (voir <see cref="SubscriptionCharge"/>). Selon l'état :
/// <list type="bullet">
/// <item>essai en cours : la première période payée suit l'essai (conversion anticipée) ;</item>
/// <item>essai fini, impayé, suspendu : la période payée part de la fin de la période courante si elle contient
/// maintenant, sinon de maintenant (pas de trou rétroactif) ;</item>
/// <item>période payée échue : renouvellement à la suite ;</item>
/// <item>période payée en cours : rien à payer (<c>billing.subscription.nothing_to_pay</c>), ce qui rend un double envoi
/// sans effet.</item>
/// </list>
/// La durée est l'intervalle de facturation du plan.
/// </summary>
public sealed record RecordSubscriptionPaymentCommand(Guid SubscriptionId) : ICommand<SubscriptionPaymentDto>;

public sealed class RecordSubscriptionPaymentHandler(BillingWorkspace workspace, IClock clock)
    : ICommandHandler<RecordSubscriptionPaymentCommand, SubscriptionPaymentDto>
{
    public async Task<SubscriptionPaymentDto> HandleAsync(RecordSubscriptionPaymentCommand command, CancellationToken cancellationToken = default)
    {
        var subscription = await workspace.LoadAnySubscriptionAsync(command.SubscriptionId, cancellationToken);
        var plan = await workspace.Plans.FindAsync(subscription.PlanId, cancellationToken)
            ?? throw new NotFoundException(BillingApplicationErrors.PlanNotFound);
        var paid = SubscriptionDues.NextPaidPeriod(subscription, plan, clock.UtcNow)
            ?? throw new DomainException(BillingApplicationErrors.NothingToPay);
        SubscriptionDues.Apply(subscription, paid, clock);

        // Le plan et les options actives au moment du paiement, ligne par ligne.
        workspace.Payments.Add(SubscriptionPayment.Record(subscription, plan, await workspace.ActiveAddOnPlansAsync(subscription.TenantId, cancellationToken), clock));
        await workspace.SaveAsync(cancellationToken);
        return new SubscriptionPaymentDto(
            subscription.Id.Value, subscription.Status, subscription.CurrentPeriod.StartUtc, subscription.CurrentPeriod.EndUtc);
    }
}

public sealed record AdminMarkSubscriptionPastDueCommand(Guid SubscriptionId) : ICommand;

public sealed class AdminMarkSubscriptionPastDueHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<AdminMarkSubscriptionPastDueCommand>
{
    public async Task HandleAsync(AdminMarkSubscriptionPastDueCommand command, CancellationToken cancellationToken = default)
    {
        (await workspace.LoadAnySubscriptionAsync(command.SubscriptionId, cancellationToken)).MarkPastDue(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record AdminSuspendSubscriptionCommand(Guid SubscriptionId, SuspensionReason Reason) : ICommand;

public sealed class AdminSuspendSubscriptionHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<AdminSuspendSubscriptionCommand>
{
    public async Task HandleAsync(AdminSuspendSubscriptionCommand command, CancellationToken cancellationToken = default)
    {
        (await workspace.LoadAnySubscriptionAsync(command.SubscriptionId, cancellationToken)).Suspend(command.Reason, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Résiliation immédiate et définitive par la plateforme ; les options du restaurant sont résiliées avec.</summary>
public sealed record AdminCancelSubscriptionCommand(Guid SubscriptionId) : ICommand;

public sealed class AdminCancelSubscriptionHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<AdminCancelSubscriptionCommand>
{
    public async Task HandleAsync(AdminCancelSubscriptionCommand command, CancellationToken cancellationToken = default)
    {
        var subscription = await workspace.LoadAnySubscriptionAsync(command.SubscriptionId, cancellationToken);
        subscription.Cancel(clock);
        await workspace.CancelAddOnsAsync(subscription.TenantId, clock, cancellationToken);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Ce qu'a décidé le job d'échéance pour un abonnement.</summary>
public enum SubscriptionDueOutcome
{
    Nothing = 1,
    Cancelled = 2,
    TrialExpired = 3,
    PastDue = 4,
    SuspendedForNonPayment = 5,
    RenewedFree = 6,
}

/// <summary>
/// Job d'échéance, pour un abonnement de son tenant (contexte du job) dont la période est terminée :
/// résiliation programmée -> résilié ; période payée d'un plan gratuit (plan et options à 0, cas d'une offre) -> période
/// suivante, sans paiement ; essai, plan gratuit compris (Découverte : un mois) -> suspendu (essai expiré) ; période payée -> impayé ; impayé au-delà du délai de grâce -> suspendu (impayé).
/// Rejouable : un abonnement déjà traité ne change plus.
/// </summary>
public sealed record ProcessDueSubscriptionCommand(Guid SubscriptionId) : ICommand<SubscriptionDueOutcome>;

public sealed class ProcessDueSubscriptionHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<ProcessDueSubscriptionCommand, SubscriptionDueOutcome>
{
    public async Task<SubscriptionDueOutcome> HandleAsync(ProcessDueSubscriptionCommand command, CancellationToken cancellationToken = default)
    {
        var subscription = command.SubscriptionId == Guid.Empty
            ? null
            : await workspace.Subscriptions.FindAsync(workspace.CurrentTenant, SubscriptionId.From(command.SubscriptionId), cancellationToken);
        var now = clock.UtcNow;
        if (subscription is null || subscription.Status == SubscriptionStatus.Cancelled || !subscription.CurrentPeriod.HasEndedAt(now))
        {
            return SubscriptionDueOutcome.Nothing;
        }

        SubscriptionDueOutcome outcome;
        if (subscription.CancelAtPeriodEnd)
        {
            // La période proposée est ignorée quand une résiliation est programmée.
            subscription.RenewPeriod(subscription.CurrentPeriod, clock);
            await workspace.CancelAddOnsAsync(subscription.TenantId, clock, cancellationToken);
            outcome = SubscriptionDueOutcome.Cancelled;
        }
        else if (subscription.Status == SubscriptionStatus.Active
            && await workspace.Plans.FindAsync(subscription.PlanId, cancellationToken) is { } plan
            && (await workspace.ChargeAsync(plan, subscription.TenantId, cancellationToken)).Amount == 0m)
        {
            SubscriptionDues.Apply(subscription, SubscriptionDues.NextPaidPeriod(subscription, plan, now)!, clock);
            outcome = SubscriptionDueOutcome.RenewedFree;
        }
        else if (subscription.Status == SubscriptionStatus.Trialing)
        {
            subscription.Suspend(SuspensionReason.TrialExpired, clock);
            outcome = SubscriptionDueOutcome.TrialExpired;
        }
        else if (subscription.Status == SubscriptionStatus.Active)
        {
            subscription.MarkPastDue(clock);
            outcome = SubscriptionDueOutcome.PastDue;
        }
        else if (subscription.Status == SubscriptionStatus.PastDue && subscription.CurrentPeriod.EndUtc + workspace.Settings.PastDueGracePeriod <= now)
        {
            subscription.Suspend(SuspensionReason.PaymentOverdue, clock);
            outcome = SubscriptionDueOutcome.SuspendedForNonPayment;
        }
        else
        {
            return SubscriptionDueOutcome.Nothing;
        }

        await workspace.SaveAsync(cancellationToken);
        return outcome;
    }
}
