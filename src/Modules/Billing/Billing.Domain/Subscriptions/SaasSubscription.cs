namespace Billing.Domain;

/// <summary>
/// Ce qu'un tenant paie à la plateforme. Sans rapport avec les paiements des commandes du restaurant.
/// Cancelled est terminal : revenir implique de démarrer un nouvel abonnement.
/// Une OFFRE de la plateforme (<see cref="GrantComplimentary"/>) rend l'abonnement actif sans paiement jusqu'à une date, ou
/// sans fin (<see cref="UnlimitedEndUtc"/>) : la période courante est alors la période offerte. À son terme, le parcours
/// normal reprend (impayé, délai de grâce, suspension).
/// </summary>
public sealed class SaasSubscription : AggregateRoot<SubscriptionId>, ITenantOwned
{
    /// <summary>Fin de la période d'une offre sans limite de durée : elle n'arrive jamais à échéance.</summary>
    public static readonly DateTimeOffset UnlimitedEndUtc = new(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Fenêtre du préavis de fin d'essai ou d'offre.</summary>
    public static readonly TimeSpan EndingNoticeWindow = TimeSpan.FromDays(7);

    private SaasSubscription(SubscriptionId id, TenantId tenantId, PlanId planId, Period trialPeriod, DateTimeOffset startedAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        PlanId = planId;
        Status = SubscriptionStatus.Trialing;
        CurrentPeriod = trialPeriod;
        StartedAtUtc = startedAtUtc;
    }

    // Réservé à l'ORM.
    private SaasSubscription() => CurrentPeriod = null!;

    public TenantId TenantId { get; }

    public PlanId PlanId { get; private set; }

    public SubscriptionStatus Status { get; private set; }

    public Period CurrentPeriod { get; private set; }

    public bool CancelAtPeriodEnd { get; private set; }

    /// <summary>Motif de suspension ; null dès que l'abonnement n'est plus suspendu par <see cref="Suspend"/> (effacé par <see cref="Activate"/>).</summary>
    public SuspensionReason? SuspensionReason { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    /// <summary>La période courante est offerte par la plateforme (rien à payer avant sa fin).</summary>
    public bool IsComplimentary { get; private set; }

    /// <summary>Offre sans limite de durée.</summary>
    public bool IsComplimentaryUnlimited => IsComplimentary && CurrentPeriod.EndUtc >= UnlimitedEndUtc;

    /// <summary>Fin de la période dont le restaurant a déjà été prévenu de l'échéance (un préavis par échéance).</summary>
    public DateTimeOffset? EndingNoticeSentForUtc { get; private set; }

    public static SaasSubscription StartTrial(
        TenantId tenantId,
        PlanReference plan,
        Period trialPeriod,
        bool tenantHasNoLiveSubscription,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(trialPeriod);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(!tenantHasNoLiveSubscription, SubscriptionErrors.AlreadySubscribed);
        DomainException.ThrowIf(plan.IsAddOn, SubscriptionErrors.PlanIsAddOn);
        DomainException.ThrowIf(!plan.IsActive, SubscriptionErrors.PlanArchived);

        var now = clock.UtcNow;
        DomainException.ThrowIf(trialPeriod.HasEndedAt(now), SubscriptionErrors.PeriodEnded);

        var subscription = new SaasSubscription(SubscriptionId.New(), tenantId, plan.PlanId, trialPeriod, now);
        subscription.Raise(new SubscriptionTrialStarted(tenantId, subscription.Id, plan.PlanId, trialPeriod, now));
        return subscription;
    }

    /// <summary>
    /// Entre (ou rentre) dans l'état payé pour <paramref name="paidPeriod"/> : fin d'essai, ou paiement
    /// régularisé après impayé / suspension (y compris la suspension pour fin d'essai). La période payée doit contenir
    /// l'instant présent, ou commencer exactement à la fin d'une période courante encore en cours (conversion anticipée).
    /// Tant qu'une résiliation est programmée, la période payée ne peut pas dépasser la fin de la période courante.
    /// </summary>
    public void Activate(Period paidPeriod, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(paidPeriod);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotCancelled();
        DomainException.ThrowIf(
            Status is not (SubscriptionStatus.Trialing or SubscriptionStatus.PastDue or SubscriptionStatus.Suspended),
            SubscriptionErrors.CannotActivate);
        DomainException.ThrowIf(paidPeriod.StartUtc < CurrentPeriod.StartUtc, SubscriptionErrors.PeriodNotConsistent);

        var now = clock.UtcNow;
        DomainException.ThrowIf(paidPeriod.HasEndedAt(now), SubscriptionErrors.PeriodEnded);

        var followsRunningPeriod = paidPeriod.StartUtc == CurrentPeriod.EndUtc && !CurrentPeriod.HasEndedAt(now);
        DomainException.ThrowIf(!paidPeriod.Contains(now) && !followsRunningPeriod, SubscriptionErrors.PeriodNotConsistent);
        DomainException.ThrowIf(
            CancelAtPeriodEnd && paidPeriod.EndUtc > CurrentPeriod.EndUtc,
            SubscriptionErrors.CancellationScheduled);

        var previous = Status;
        Status = SubscriptionStatus.Active;
        CurrentPeriod = paidPeriod;
        IsComplimentary = false;
        SuspensionReason = null;
        Raise(new SubscriptionActivated(TenantId, Id, PlanId, previous, paidPeriod, now));
    }

    public void ChangePlan(PlanReference newPlan, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newPlan);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotCancelled();
        // Après un essai expiré, le restaurant choisit un plan payant avant de payer.
        DomainException.ThrowIf(
            Status is not (SubscriptionStatus.Trialing or SubscriptionStatus.Active)
            && !(Status == SubscriptionStatus.Suspended && SuspensionReason == Billing.Domain.SuspensionReason.TrialExpired),
            SubscriptionErrors.PlanChangeNotAllowed);
        DomainException.ThrowIf(IsComplimentary, SubscriptionErrors.Complimentary);
        if (newPlan.PlanId == PlanId)
        {
            return;
        }

        DomainException.ThrowIf(newPlan.IsAddOn, SubscriptionErrors.PlanIsAddOn);
        DomainException.ThrowIf(!newPlan.IsActive, SubscriptionErrors.PlanArchived);

        var previous = PlanId;
        PlanId = newPlan.PlanId;
        Raise(new SubscriptionPlanChanged(TenantId, Id, previous, PlanId, clock.UtcNow));
    }

    public void MarkPastDue(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotCancelled();
        DomainException.ThrowIf(Status != SubscriptionStatus.Active, SubscriptionErrors.NotActive);
        DomainException.ThrowIf(IsComplimentaryUnlimited, SubscriptionErrors.Complimentary);

        IsComplimentary = false;
        Status = SubscriptionStatus.PastDue;
        Raise(new SubscriptionPastDue(TenantId, Id, CurrentPeriod, clock.UtcNow));
    }

    /// <summary>
    /// Suspend un abonnement vivant sans y mettre fin : <see cref="Activate"/> le rétablit. C'est ainsi qu'un essai
    /// expiré sans conversion est clos (<see cref="Billing.Domain.SuspensionReason.TrialExpired"/>).
    /// </summary>
    public void Suspend(SuspensionReason reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotCancelled();
        DomainException.ThrowIf(!Enum.IsDefined(reason), SubscriptionErrors.SuspensionReasonInvalid);
        DomainException.ThrowIf(
            Status is not (SubscriptionStatus.Trialing or SubscriptionStatus.Active or SubscriptionStatus.PastDue),
            SubscriptionErrors.CannotSuspend);

        var now = clock.UtcNow;
        DomainException.ThrowIf(
            reason == Billing.Domain.SuspensionReason.TrialExpired
            && (Status != SubscriptionStatus.Trialing || !CurrentPeriod.HasEndedAt(now)),
            SubscriptionErrors.TrialNotExpired);

        var previous = Status;
        IsComplimentary = false;
        Status = SubscriptionStatus.Suspended;
        SuspensionReason = reason;
        Raise(new SubscriptionSuspended(TenantId, Id, previous, reason, now));
    }

    public void ScheduleCancellation(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotCancelled();
        DomainException.ThrowIf(CancelAtPeriodEnd, SubscriptionErrors.CancellationAlreadyScheduled);
        DomainException.ThrowIf(IsComplimentaryUnlimited, SubscriptionErrors.Complimentary);

        CancelAtPeriodEnd = true;
        Raise(new SubscriptionCancellationScheduled(TenantId, Id, CurrentPeriod.EndUtc, clock.UtcNow));
    }

    public void UnscheduleCancellation(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotCancelled();
        DomainException.ThrowIf(!CancelAtPeriodEnd, SubscriptionErrors.CancellationNotScheduled);

        CancelAtPeriodEnd = false;
        Raise(new SubscriptionCancellationUnscheduled(TenantId, Id, clock.UtcNow));
    }

    /// <summary>Résiliation immédiate et définitive, depuis tout statut vivant.</summary>
    public void Cancel(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotCancelled();

        CancelNow(CancellationReason.Requested, clock.UtcNow);
    }

    /// <summary>
    /// Appelée une fois la période courante terminée (jamais à l'avance). Fait passer un abonnement actif à sa période
    /// suivante, qui doit commencer exactement là où se termine la courante. Quand une résiliation est programmée,
    /// elle prend effet à la fin de période : quel que soit son statut vivant, l'abonnement est résilié au lieu
    /// d'être renouvelé.
    /// </summary>
    public void RenewPeriod(Period nextPeriod, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(nextPeriod);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotCancelled();

        var now = clock.UtcNow;
        var periodHasEnded = CurrentPeriod.HasEndedAt(now);
        if (CancelAtPeriodEnd)
        {
            DomainException.ThrowIf(!periodHasEnded, SubscriptionErrors.PeriodNotEnded);
            CancelNow(CancellationReason.AtPeriodEnd, now);
            return;
        }

        DomainException.ThrowIf(Status != SubscriptionStatus.Active, SubscriptionErrors.NotActive);
        DomainException.ThrowIf(nextPeriod.StartUtc != CurrentPeriod.EndUtc, SubscriptionErrors.PeriodNotContiguous);
        DomainException.ThrowIf(!periodHasEnded, SubscriptionErrors.PeriodNotEnded);

        CurrentPeriod = nextPeriod;
        IsComplimentary = false;
        Raise(new SubscriptionPeriodRenewed(TenantId, Id, PlanId, nextPeriod, now));
    }

    /// <summary>
    /// La plateforme offre <paramref name="plan"/> à partir de maintenant, jusqu'à <paramref name="untilUtc"/> ou sans fin
    /// (null). Depuis tout statut vivant : un abonnement impayé ou suspendu est rétabli. Une résiliation programmée est
    /// retirée (c'est une décision de la plateforme). Une nouvelle offre remplace la précédente.
    /// </summary>
    public void GrantComplimentary(PlanReference plan, DateTimeOffset? untilUtc, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotCancelled();
        DomainException.ThrowIf(plan.IsAddOn, SubscriptionErrors.PlanIsAddOn);
        DomainException.ThrowIf(!plan.IsActive, SubscriptionErrors.PlanArchived);

        var now = clock.UtcNow;
        var end = untilUtc?.ToUniversalTime() ?? UnlimitedEndUtc;
        DomainException.ThrowIf(end <= now || end > UnlimitedEndUtc, SubscriptionErrors.ComplimentaryEndInvalid);

        var previous = Status;
        var previousPlan = PlanId;
        PlanId = plan.PlanId;
        Status = SubscriptionStatus.Active;
        CurrentPeriod = Period.Create(now, end);
        IsComplimentary = true;
        CancelAtPeriodEnd = false;
        SuspensionReason = null;
        EndingNoticeSentForUtc = null;
        if (previousPlan != PlanId)
        {
            Raise(new SubscriptionPlanChanged(TenantId, Id, previousPlan, PlanId, now));
        }

        Raise(new SubscriptionComplimentaryGranted(TenantId, Id, PlanId, untilUtc is null ? null : end, now));
        Raise(new SubscriptionActivated(TenantId, Id, PlanId, previous, CurrentPeriod, now));
    }

    /// <summary>
    /// La plateforme met fin à l'offre maintenant : la période offerte s'arrête, et le job d'échéance fait passer
    /// l'abonnement en impayé (le restaurant règle la période suivante).
    /// </summary>
    public void EndComplimentary(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotCancelled();
        DomainException.ThrowIf(!IsComplimentary, SubscriptionErrors.NotComplimentary);

        var now = clock.UtcNow;
        CurrentPeriod = Period.Create(CurrentPeriod.StartUtc, now > CurrentPeriod.StartUtc ? now : CurrentPeriod.StartUtc.AddTicks(1));
        IsComplimentary = false;
        Raise(new SubscriptionComplimentaryEnded(TenantId, Id, now));
    }

    /// <summary>
    /// Préavis de fin d'essai ou d'offre limitée, une fois par échéance, dans les <see cref="EndingNoticeWindow"/> qui la
    /// précèdent. Faux s'il n'y a rien à annoncer (déjà prévenu, trop tôt, autre statut, résiliation programmée).
    /// <paramref name="silently"/> : l'échéance est marquée comme traitée sans rien annoncer (plan gratuit, qui se renouvelle seul).
    /// </summary>
    public bool NoticeEnding(IClock clock, bool silently = false)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.UtcNow;
        var end = CurrentPeriod.EndUtc;
        var ending = Status is SubscriptionStatus.Trialing || (Status == SubscriptionStatus.Active && IsComplimentary && !IsComplimentaryUnlimited);
        if (!ending || CancelAtPeriodEnd || EndingNoticeSentForUtc == end || CurrentPeriod.HasEndedAt(now) || end - now > EndingNoticeWindow)
        {
            return false;
        }

        EndingNoticeSentForUtc = end;
        if (silently)
        {
            return true;
        }

        Raise(new SubscriptionEndingSoon(TenantId, Id, PlanId, end, IsComplimentary, now));
        return true;
    }

    private void CancelNow(CancellationReason reason, DateTimeOffset now)
    {
        IsComplimentary = false;
        Status = SubscriptionStatus.Cancelled;
        CancelledAtUtc = now;
        Raise(new SubscriptionCancelled(TenantId, Id, PlanId, reason, now));
    }

    private void EnsureNotCancelled() =>
        DomainException.ThrowIf(Status == SubscriptionStatus.Cancelled, SubscriptionErrors.Cancelled);
}
