using Billing.Domain;

namespace Billing.Domain;

public class SaasSubscriptionTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = FakeClock.DefaultNow;
    private static readonly Period Trial = Period.Create(Now, Now.AddDays(14));
    private static readonly Period FirstMonth = Period.Create(Now.AddDays(14), Now.AddDays(44));
    private static readonly Period SecondMonth = Period.Create(Now.AddDays(44), Now.AddDays(74));

    private readonly FakeClock _clock = new();
    // Une référence ne peut provenir que d'un vrai plan.
    private readonly Plan _starterPlan = NewPlan("Starter");
    private readonly Plan _proPlan = NewPlan("Pro");

    private PlanReference _starter => _starterPlan.ToReference();

    private PlanReference _pro => _proPlan.ToReference();

    private static Plan NewPlan(string name) =>
        Plan.Create(name, Money.Of(19m, Currency.EUR), BillingInterval.Monthly, [], new FakeClock());

    private static PlanReference Archived(Plan plan)
    {
        plan.Archive(new FakeClock());
        return plan.ToReference();
    }

    private SaasSubscription Trialing()
    {
        var subscription = SaasSubscription.StartTrial(Tenant, _starter, Trial, tenantHasNoLiveSubscription: true, _clock);
        subscription.ClearDomainEvents();
        return subscription;
    }

    private SaasSubscription Active()
    {
        var subscription = Trialing();
        subscription.Activate(FirstMonth, _clock);
        subscription.ClearDomainEvents();
        return subscription;
    }

    private SaasSubscription PastDue()
    {
        var subscription = Active();
        subscription.MarkPastDue(_clock);
        subscription.ClearDomainEvents();
        return subscription;
    }

    private SaasSubscription Suspended()
    {
        var subscription = PastDue();
        subscription.Suspend(SuspensionReason.PaymentOverdue, _clock);
        subscription.ClearDomainEvents();
        return subscription;
    }

    private SaasSubscription SuspendedAtTrialEnd()
    {
        var subscription = Trialing();
        _clock.Set(Trial.EndUtc);
        subscription.Suspend(SuspensionReason.TrialExpired, _clock);
        subscription.ClearDomainEvents();
        return subscription;
    }

    private SaasSubscription Cancelled()
    {
        var subscription = Active();
        subscription.Cancel(_clock);
        subscription.ClearDomainEvents();
        return subscription;
    }

    [Fact]
    public void StartTrial_creates_a_trialing_subscription()
    {
        var subscription = SaasSubscription.StartTrial(Tenant, _starter, Trial, true, _clock);

        Assert.Equal(Tenant, subscription.TenantId);
        Assert.Equal(_starter.PlanId, subscription.PlanId);
        Assert.Equal(SubscriptionStatus.Trialing, subscription.Status);
        Assert.Equal(Trial, subscription.CurrentPeriod);
        Assert.False(subscription.CancelAtPeriodEnd);
        Assert.Equal(Now, subscription.StartedAtUtc);
        Assert.Null(subscription.CancelledAtUtc);

        var started = DomainAssert.Raised<SubscriptionTrialStarted>(subscription);
        Assert.Equal(
            (Tenant, subscription.Id, _starter.PlanId, Trial, Now),
            (started.TenantId, started.SubscriptionId, started.PlanId, started.TrialPeriod, started.OccurredAtUtc));
    }

    [Fact]
    public void StartTrial_rejects_a_tenant_that_already_has_a_live_subscription() =>
        DomainAssert.Throws(
            SubscriptionErrors.AlreadySubscribed,
            () => SaasSubscription.StartTrial(Tenant, _starter, Trial, tenantHasNoLiveSubscription: false, _clock));

    [Fact]
    public void StartTrial_rejects_an_empty_tenant() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => SaasSubscription.StartTrial(default, _starter, Trial, true, _clock));

    [Fact]
    public void A_plan_reference_can_only_be_built_by_its_plan()
    {
        // Aucun constructeur public ni membre modifiable : un appelant ne peut pas déclarer actif un plan archivé.
        Assert.Empty(typeof(PlanReference).GetConstructors());
        Assert.DoesNotContain(typeof(PlanReference).GetProperties(), property => property.CanWrite);
    }

    [Fact]
    public void StartTrial_stamps_the_state_and_the_event_with_the_same_instant()
    {
        var subscription = SaasSubscription.StartTrial(Tenant, _starter, Trial, true, new TickingClock(Now));

        Assert.Equal(subscription.StartedAtUtc, DomainAssert.Raised<SubscriptionTrialStarted>(subscription).OccurredAtUtc);
    }

    [Fact]
    public void RenewPeriod_with_a_scheduled_cancellation_stamps_the_state_and_the_event_with_the_same_instant()
    {
        var subscription = Active();
        subscription.ScheduleCancellation(_clock);
        subscription.ClearDomainEvents();

        subscription.RenewPeriod(SecondMonth, new TickingClock(FirstMonth.EndUtc));

        Assert.Equal(subscription.CancelledAtUtc, DomainAssert.Raised<SubscriptionCancelled>(subscription).OccurredAtUtc);
        Assert.Equal(FirstMonth.EndUtc, subscription.CancelledAtUtc);
    }

    [Fact]
    public void StartTrial_rejects_an_archived_plan() =>
        DomainAssert.Throws(
            SubscriptionErrors.PlanArchived,
            () => SaasSubscription.StartTrial(Tenant, Archived(_starterPlan), Trial, true, _clock));

    [Fact]
    public void StartTrial_rejects_a_trial_period_already_over() =>
        DomainAssert.Throws(
            SubscriptionErrors.PeriodEnded,
            () => SaasSubscription.StartTrial(Tenant, _starter, Period.Create(Now.AddDays(-14), Now), true, _clock));

    [Fact]
    public void Activate_converts_a_trial_into_a_paid_period()
    {
        var subscription = Trialing();

        subscription.Activate(FirstMonth, _clock);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(FirstMonth, subscription.CurrentPeriod);
        var activated = DomainAssert.Raised<SubscriptionActivated>(subscription);
        Assert.Equal(
            (Tenant, subscription.Id, _starter.PlanId, SubscriptionStatus.Trialing, FirstMonth, Now),
            (activated.TenantId, activated.SubscriptionId, activated.PlanId, activated.PreviousStatus, activated.CurrentPeriod, activated.OccurredAtUtc));
    }

    [Fact]
    public void Activate_can_convert_a_trial_early()
    {
        var subscription = Trialing();
        var early = Period.Create(Now.AddDays(3), Now.AddDays(33));
        _clock.Set(early.StartUtc);

        subscription.Activate(early, _clock);

        Assert.Equal(early, subscription.CurrentPeriod);
        Assert.True(subscription.CurrentPeriod.Contains(_clock.UtcNow));
    }

    [Fact]
    public void Activate_can_be_paid_in_advance_for_the_period_following_the_running_trial()
    {
        var subscription = Trialing();
        _clock.Set(Trial.EndUtc.AddTicks(-1));

        subscription.Activate(FirstMonth, _clock);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(FirstMonth, subscription.CurrentPeriod);
    }

    [Fact]
    public void Activate_rejects_a_period_that_neither_contains_now_nor_follows_the_running_period()
    {
        var subscription = Trialing();
        var inThreeYears = Period.Create(Now.AddYears(3), Now.AddYears(3).AddDays(30));
        var afterAGap = Period.Create(Trial.EndUtc.AddTicks(1), Trial.EndUtc.AddDays(30));
        var insideTheTrialButLater = Period.Create(Now.AddDays(3), Now.AddDays(33));

        DomainAssert.Throws(SubscriptionErrors.PeriodNotConsistent, () => subscription.Activate(inThreeYears, _clock));
        DomainAssert.Throws(SubscriptionErrors.PeriodNotConsistent, () => subscription.Activate(afterAGap, _clock));
        DomainAssert.Throws(SubscriptionErrors.PeriodNotConsistent, () => subscription.Activate(insideTheTrialButLater, _clock));

        Assert.Equal(SubscriptionStatus.Trialing, subscription.Status);
        Assert.Equal(Trial, subscription.CurrentPeriod);
        DomainAssert.NoEvents(subscription);
    }

    [Fact]
    public void Activate_rejects_a_future_period_once_the_current_period_has_ended()
    {
        var subscription = Trialing();
        _clock.Set(Trial.EndUtc);

        // FirstMonth commence pile à la fin de l'essai, soit maintenant : il contient « maintenant », donc accepté...
        // ...mais une période commençant plus tard laisserait un abonnement actif hors de sa propre période.
        DomainAssert.Throws(SubscriptionErrors.PeriodNotConsistent, () => subscription.Activate(SecondMonth, _clock));

        subscription.Activate(FirstMonth, _clock);
        Assert.True(subscription.CurrentPeriod.Contains(_clock.UtcNow));
    }

    [Fact]
    public void Activate_brings_back_a_subscription_suspended_at_trial_end_without_a_new_trial()
    {
        var subscription = SuspendedAtTrialEnd();
        var id = subscription.Id;
        _clock.Advance(TimeSpan.FromDays(6));
        var paid = Period.Create(_clock.UtcNow, _clock.UtcNow.AddDays(30));

        subscription.Activate(paid, _clock);

        Assert.Equal(id, subscription.Id);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(paid, subscription.CurrentPeriod);
        Assert.Null(subscription.SuspensionReason);
        var activated = DomainAssert.Raised<SubscriptionActivated>(subscription);
        Assert.Equal(SubscriptionStatus.Suspended, activated.PreviousStatus);
        Assert.Equal(paid, activated.CurrentPeriod);
    }

    [Fact]
    public void Activate_after_a_trial_expiry_suspension_rejects_a_period_starting_after_now()
    {
        var subscription = SuspendedAtTrialEnd();
        var later = Period.Create(_clock.UtcNow.AddDays(1), _clock.UtcNow.AddDays(31));

        DomainAssert.Throws(SubscriptionErrors.PeriodNotConsistent, () => subscription.Activate(later, _clock));

        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
        Assert.Equal(SuspensionReason.TrialExpired, subscription.SuspensionReason);
    }

    [Fact]
    public void Activate_recovers_a_past_due_subscription_on_the_same_period()
    {
        var subscription = PastDue();
        _clock.Set(FirstMonth.StartUtc);

        subscription.Activate(FirstMonth, _clock);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(SubscriptionStatus.PastDue, DomainAssert.Raised<SubscriptionActivated>(subscription).PreviousStatus);
    }

    [Fact]
    public void Activate_recovers_a_suspended_subscription_with_a_new_period()
    {
        var subscription = Suspended();
        _clock.Set(SecondMonth.StartUtc);

        subscription.Activate(SecondMonth, _clock);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(SecondMonth, subscription.CurrentPeriod);
        Assert.Null(subscription.SuspensionReason);
        Assert.Equal(SubscriptionStatus.Suspended, DomainAssert.Raised<SubscriptionActivated>(subscription).PreviousStatus);
    }

    [Fact]
    public void Activate_rejects_an_already_active_subscription()
    {
        var subscription = Active();

        DomainAssert.Throws(SubscriptionErrors.CannotActivate, () => subscription.Activate(SecondMonth, _clock));

        Assert.Equal(FirstMonth, subscription.CurrentPeriod);
        DomainAssert.NoEvents(subscription);
    }

    [Fact]
    public void Activate_rejects_a_period_starting_before_the_current_one()
    {
        var subscription = Trialing();

        DomainAssert.Throws(
            SubscriptionErrors.PeriodNotConsistent,
            () => subscription.Activate(Period.Create(Now.AddDays(-1), Now.AddDays(29)), _clock));

        Assert.Equal(SubscriptionStatus.Trialing, subscription.Status);
        Assert.Equal(Trial, subscription.CurrentPeriod);
        DomainAssert.NoEvents(subscription);
    }

    [Fact]
    public void Activate_rejects_a_period_already_over()
    {
        var subscription = Suspended();
        _clock.Set(FirstMonth.EndUtc);

        DomainAssert.Throws(SubscriptionErrors.PeriodEnded, () => subscription.Activate(FirstMonth, _clock));

        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
        DomainAssert.NoEvents(subscription);
    }

    [Fact]
    public void ChangePlan_during_a_paid_period_is_scheduled_for_its_end()
    {
        var subscription = Active();

        subscription.ChangePlan(_pro, _clock);

        Assert.Equal(_starter.PlanId, subscription.PlanId);
        Assert.Equal((_pro.PlanId, FirstMonth.EndUtc, _pro.PlanId), (subscription.ScheduledPlanId, subscription.ScheduledPlanEffectiveAtUtc, subscription.NextPeriodPlanId));
        Assert.Equal(FirstMonth, subscription.CurrentPeriod);
        var scheduled = DomainAssert.Raised<SubscriptionPlanChangeScheduled>(subscription);
        Assert.Equal(
            (Tenant, subscription.Id, _starter.PlanId, _pro.PlanId, FirstMonth.EndUtc, Now),
            (scheduled.TenantId, scheduled.SubscriptionId, scheduled.PlanId, scheduled.ScheduledPlanId, scheduled.EffectiveAtUtc, scheduled.OccurredAtUtc));
        DomainAssert.NotRaised<SubscriptionPlanChanged>(subscription);
    }

    [Fact]
    public void The_scheduled_plan_becomes_current_when_the_next_period_starts()
    {
        var subscription = Active();
        subscription.ChangePlan(_pro, _clock);
        _clock.Set(FirstMonth.EndUtc);

        subscription.RenewPeriod(SecondMonth, _clock);

        Assert.Equal((_pro.PlanId, (PlanId?)null, SecondMonth), (subscription.PlanId, subscription.ScheduledPlanId, subscription.CurrentPeriod));
        var changed = DomainAssert.Raised<SubscriptionPlanChanged>(subscription);
        Assert.Equal((_starter.PlanId, _pro.PlanId), (changed.PreviousPlanId, changed.NewPlanId));
        Assert.Equal(_pro.PlanId, DomainAssert.Raised<SubscriptionPeriodRenewed>(subscription).PlanId);
    }

    [Fact]
    public void ApplyScheduledPlan_waits_for_the_period_end()
    {
        var subscription = Active();
        Assert.False(subscription.ApplyScheduledPlan(_clock));
        subscription.ChangePlan(_pro, _clock);

        DomainAssert.Throws(SubscriptionErrors.PeriodNotEnded, () => subscription.ApplyScheduledPlan(_clock));
        _clock.Set(FirstMonth.EndUtc);
        Assert.True(subscription.ApplyScheduledPlan(_clock));

        Assert.Equal((_pro.PlanId, (PlanId?)null), (subscription.PlanId, subscription.ScheduledPlanId));
    }

    [Fact]
    public void A_scheduled_plan_change_is_withdrawn_or_replaced()
    {
        var subscription = Active();
        var other = NewPlan("Max").ToReference();

        subscription.ChangePlan(_pro, _clock);
        subscription.ChangePlan(_pro, _clock);
        subscription.ChangePlan(other, _clock);
        Assert.Equal(other.PlanId, subscription.ScheduledPlanId);
        subscription.ChangePlan(_starter, _clock);
        Assert.Null(subscription.ScheduledPlanId);
        subscription.ChangePlan(_pro, _clock);
        subscription.UnschedulePlanChange(_clock);

        Assert.Equal((_starter.PlanId, (PlanId?)null), (subscription.PlanId, subscription.ScheduledPlanId));
        DomainAssert.Throws(SubscriptionErrors.PlanChangeNotScheduled, () => subscription.UnschedulePlanChange(_clock));
        Assert.Equal(3, subscription.DomainEvents.OfType<SubscriptionPlanChangeScheduled>().Count());
        Assert.Equal(2, subscription.DomainEvents.OfType<SubscriptionPlanChangeUnscheduled>().Count());
        DomainAssert.NotRaised<SubscriptionPlanChanged>(subscription);
    }

    [Fact]
    public void A_scheduled_plan_change_and_a_scheduled_cancellation_exclude_each_other()
    {
        var changing = Active();
        changing.ChangePlan(_pro, _clock);
        var leaving = Active();
        leaving.ScheduleCancellation(_clock);

        DomainAssert.Throws(SubscriptionErrors.PlanChangeScheduled, () => changing.ScheduleCancellation(_clock));
        DomainAssert.Throws(SubscriptionErrors.CancellationScheduled, () => leaving.ChangePlan(_pro, _clock));

        Assert.False(changing.CancelAtPeriodEnd);
        Assert.Null(leaving.ScheduledPlanId);
    }

    [Fact]
    public void An_offer_withdraws_the_scheduled_plan_change()
    {
        var subscription = Active();
        subscription.ChangePlan(_pro, _clock);

        subscription.GrantComplimentary(_starter, null, _clock);

        Assert.Null(subscription.ScheduledPlanId);
        DomainAssert.Raised<SubscriptionPlanChangeUnscheduled>(subscription);
    }

    [Fact]
    public void ChangePlan_is_allowed_during_the_trial()
    {
        var subscription = Trialing();

        subscription.ChangePlan(_pro, _clock);

        Assert.Equal(_pro.PlanId, subscription.PlanId);
        Assert.Null(subscription.ScheduledPlanId);
        Assert.Equal(SubscriptionStatus.Trialing, subscription.Status);
    }

    [Fact]
    public void ChangePlan_to_the_current_plan_changes_nothing()
    {
        var subscription = Active();

        subscription.ChangePlan(_starter, _clock);
        subscription.ChangePlan(Archived(_starterPlan), _clock);

        Assert.Equal(_starter.PlanId, subscription.PlanId);
        DomainAssert.NoEvents(subscription);
    }

    [Fact]
    public void ChangePlan_rejects_an_archived_plan()
    {
        var subscription = Active();

        DomainAssert.Throws(SubscriptionErrors.PlanArchived, () => subscription.ChangePlan(Archived(_proPlan), _clock));

        Assert.Equal(_starter.PlanId, subscription.PlanId);
        DomainAssert.NoEvents(subscription);
    }

    [Fact]
    public void ChangePlan_rejects_a_past_due_or_suspended_subscription()
    {
        foreach (var subscription in new[] { PastDue(), Suspended() })
        {
            DomainAssert.Throws(SubscriptionErrors.PlanChangeNotAllowed, () => subscription.ChangePlan(_pro, _clock));

            Assert.Equal(_starter.PlanId, subscription.PlanId);
            DomainAssert.NoEvents(subscription);
        }
    }

    [Fact]
    public void ChangePlan_is_allowed_after_an_expired_trial()
    {
        var subscription = SuspendedAtTrialEnd();

        subscription.ChangePlan(_pro, _clock);

        Assert.Equal(_pro.PlanId, subscription.PlanId);
        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
    }

    [Fact]
    public void Events_of_subscription_and_plan_have_distinct_names()
    {
        Assert.NotEqual(typeof(SubscriptionPlanChanged).Name, typeof(PlanDefinitionChanged).Name);
        Assert.Null(typeof(SaasSubscription).Assembly.GetType("Billing.Domain.PlanChanged"));
    }

    [Fact]
    public void MarkPastDue_flags_an_active_subscription()
    {
        var subscription = Active();

        subscription.MarkPastDue(_clock);

        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
        Assert.Equal(FirstMonth, subscription.CurrentPeriod);
        var pastDue = DomainAssert.Raised<SubscriptionPastDue>(subscription);
        Assert.Equal((Tenant, subscription.Id, FirstMonth, Now), (pastDue.TenantId, pastDue.SubscriptionId, pastDue.CurrentPeriod, pastDue.OccurredAtUtc));
    }

    [Fact]
    public void MarkPastDue_rejects_any_status_but_active()
    {
        foreach (var subscription in new[] { Trialing(), PastDue(), Suspended() })
        {
            var before = subscription.Status;

            DomainAssert.Throws(SubscriptionErrors.NotActive, () => subscription.MarkPastDue(_clock));

            Assert.Equal(before, subscription.Status);
            DomainAssert.NoEvents(subscription);
        }
    }

    [Fact]
    public void Suspend_is_allowed_from_trialing_past_due_and_active_and_records_the_reason()
    {
        foreach (var subscription in new[] { Trialing(), PastDue(), Active() })
        {
            var before = subscription.Status;
            var period = subscription.CurrentPeriod;
            Assert.Null(subscription.SuspensionReason);

            subscription.Suspend(SuspensionReason.PolicyViolation, _clock);

            Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
            Assert.Equal(SuspensionReason.PolicyViolation, subscription.SuspensionReason);
            Assert.Equal(period, subscription.CurrentPeriod);
            Assert.Equal(
                new SubscriptionSuspended(Tenant, subscription.Id, before, SuspensionReason.PolicyViolation, Now),
                DomainAssert.Raised<SubscriptionSuspended>(subscription));
        }
    }

    [Theory]
    [InlineData(SuspensionReason.PaymentOverdue)]
    [InlineData(SuspensionReason.PolicyViolation)]
    [InlineData(SuspensionReason.Other)]
    public void Suspend_carries_each_reason(SuspensionReason reason)
    {
        var subscription = PastDue();

        subscription.Suspend(reason, _clock);

        Assert.Equal(reason, subscription.SuspensionReason);
        Assert.Equal(reason, DomainAssert.Raised<SubscriptionSuspended>(subscription).Reason);
    }

    [Fact]
    public void Suspend_ends_an_expired_trial_without_cancelling_the_subscription()
    {
        var subscription = Trialing();
        _clock.Set(Trial.EndUtc);

        subscription.Suspend(SuspensionReason.TrialExpired, _clock);

        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
        Assert.Equal(SuspensionReason.TrialExpired, subscription.SuspensionReason);
        Assert.Null(subscription.CancelledAtUtc);
        var suspended = DomainAssert.Raised<SubscriptionSuspended>(subscription);
        Assert.Equal((SubscriptionStatus.Trialing, SuspensionReason.TrialExpired), (suspended.PreviousStatus, suspended.Reason));
        DomainAssert.NotRaised<SubscriptionCancelled>(subscription);
    }

    [Fact]
    public void Suspend_for_trial_expiry_requires_a_trial_that_is_over()
    {
        var runningTrial = Trialing();
        _clock.Set(Trial.EndUtc.AddTicks(-1));
        DomainAssert.Throws(SubscriptionErrors.TrialNotExpired, () => runningTrial.Suspend(SuspensionReason.TrialExpired, _clock));
        Assert.Equal(SubscriptionStatus.Trialing, runningTrial.Status);
        Assert.Null(runningTrial.SuspensionReason);
        DomainAssert.NoEvents(runningTrial);

        _clock.Set(Now);
        foreach (var subscription in new[] { Active(), PastDue() })
        {
            _clock.Set(FirstMonth.EndUtc);
            DomainAssert.Throws(SubscriptionErrors.TrialNotExpired, () => subscription.Suspend(SuspensionReason.TrialExpired, _clock));
            DomainAssert.NoEvents(subscription);
            _clock.Set(Now);
        }
    }

    [Fact]
    public void Suspend_rejects_an_unknown_reason()
    {
        var subscription = Active();

        DomainAssert.Throws(SubscriptionErrors.SuspensionReasonInvalid, () => subscription.Suspend((SuspensionReason)0, _clock));

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        DomainAssert.NoEvents(subscription);
    }

    [Fact]
    public void Suspend_rejects_an_already_suspended_subscription_and_keeps_its_reason()
    {
        var subscription = Suspended();

        DomainAssert.Throws(SubscriptionErrors.CannotSuspend, () => subscription.Suspend(SuspensionReason.Other, _clock));

        Assert.Equal(SuspensionReason.PaymentOverdue, subscription.SuspensionReason);
        DomainAssert.NoEvents(subscription);
    }

    [Fact]
    public void ScheduleCancellation_sets_the_flag_without_changing_the_status()
    {
        foreach (var subscription in new[] { Trialing(), Active(), PastDue(), Suspended() })
        {
            var before = subscription.Status;

            subscription.ScheduleCancellation(_clock);

            Assert.True(subscription.CancelAtPeriodEnd);
            Assert.Equal(before, subscription.Status);
            var scheduled = DomainAssert.Raised<SubscriptionCancellationScheduled>(subscription);
            Assert.Equal((Tenant, subscription.Id, subscription.CurrentPeriod.EndUtc), (scheduled.TenantId, scheduled.SubscriptionId, scheduled.EffectiveAtUtc));
        }
    }

    [Fact]
    public void ScheduleCancellation_rejects_a_second_scheduling()
    {
        var subscription = Active();
        subscription.ScheduleCancellation(_clock);
        subscription.ClearDomainEvents();

        DomainAssert.Throws(SubscriptionErrors.CancellationAlreadyScheduled, () => subscription.ScheduleCancellation(_clock));

        DomainAssert.NoEvents(subscription);
    }

    [Fact]
    public void UnscheduleCancellation_clears_the_flag()
    {
        var subscription = Active();
        subscription.ScheduleCancellation(_clock);
        subscription.ClearDomainEvents();

        subscription.UnscheduleCancellation(_clock);

        Assert.False(subscription.CancelAtPeriodEnd);
        Assert.Equal(subscription.Id, DomainAssert.Raised<SubscriptionCancellationUnscheduled>(subscription).SubscriptionId);

        _clock.Set(FirstMonth.EndUtc);
        subscription.RenewPeriod(SecondMonth, _clock);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public void UnscheduleCancellation_rejects_when_nothing_is_scheduled()
    {
        var subscription = Active();

        DomainAssert.Throws(SubscriptionErrors.CancellationNotScheduled, () => subscription.UnscheduleCancellation(_clock));

        DomainAssert.NoEvents(subscription);
    }

    [Fact]
    public void Activate_rejects_a_period_going_beyond_a_scheduled_cancellation()
    {
        var subscription = Trialing();
        subscription.ScheduleCancellation(_clock);
        subscription.ClearDomainEvents();

        DomainAssert.Throws(SubscriptionErrors.CancellationScheduled, () => subscription.Activate(FirstMonth, _clock));

        Assert.Equal(SubscriptionStatus.Trialing, subscription.Status);
        Assert.Equal(Trial, subscription.CurrentPeriod);
        Assert.True(subscription.CancelAtPeriodEnd);
        DomainAssert.NoEvents(subscription);

        subscription.UnscheduleCancellation(_clock);
        subscription.Activate(FirstMonth, _clock);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public void Activate_within_the_current_period_keeps_a_scheduled_cancellation()
    {
        var subscription = PastDue();
        subscription.ScheduleCancellation(_clock);
        _clock.Set(FirstMonth.StartUtc);

        subscription.Activate(FirstMonth, _clock);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.True(subscription.CancelAtPeriodEnd);
    }

    [Fact]
    public void Cancel_is_allowed_from_every_live_status()
    {
        foreach (var subscription in new[] { Trialing(), Active(), PastDue(), Suspended() })
        {
            subscription.Cancel(_clock);

            Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
            Assert.Equal(Now, subscription.CancelledAtUtc);
            var cancelled = DomainAssert.Raised<SubscriptionCancelled>(subscription);
            Assert.Equal(
                (Tenant, subscription.Id, _starter.PlanId, CancellationReason.Requested, Now),
                (cancelled.TenantId, cancelled.SubscriptionId, cancelled.PlanId, cancelled.Reason, cancelled.OccurredAtUtc));
        }
    }

    [Fact]
    public void Cancelled_is_terminal()
    {
        var subscription = Cancelled();

        DomainAssert.Throws(SubscriptionErrors.Cancelled, () => subscription.Activate(SecondMonth, _clock));
        DomainAssert.Throws(SubscriptionErrors.Cancelled, () => subscription.ChangePlan(_pro, _clock));
        DomainAssert.Throws(SubscriptionErrors.Cancelled, () => subscription.MarkPastDue(_clock));
        DomainAssert.Throws(SubscriptionErrors.Cancelled, () => subscription.Suspend(SuspensionReason.Other, _clock));
        DomainAssert.Throws(SubscriptionErrors.Cancelled, () => subscription.ScheduleCancellation(_clock));
        DomainAssert.Throws(SubscriptionErrors.Cancelled, () => subscription.UnscheduleCancellation(_clock));
        DomainAssert.Throws(SubscriptionErrors.Cancelled, () => subscription.RenewPeriod(SecondMonth, _clock));
        DomainAssert.Throws(SubscriptionErrors.Cancelled, () => subscription.Cancel(_clock));

        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.Equal(_starter.PlanId, subscription.PlanId);
        Assert.Equal(FirstMonth, subscription.CurrentPeriod);
        DomainAssert.NoEvents(subscription);
    }

    [Fact]
    public void RenewPeriod_moves_to_the_contiguous_next_period()
    {
        var subscription = Active();
        _clock.Set(FirstMonth.EndUtc);

        subscription.RenewPeriod(SecondMonth, _clock);

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(SecondMonth, subscription.CurrentPeriod);
        var renewed = DomainAssert.Raised<SubscriptionPeriodRenewed>(subscription);
        Assert.Equal((Tenant, subscription.Id, _starter.PlanId, SecondMonth), (renewed.TenantId, renewed.SubscriptionId, renewed.PlanId, renewed.CurrentPeriod));
    }

    [Fact]
    public void RenewPeriod_rejects_a_gap_or_an_overlap()
    {
        var subscription = Active();
        var gap = Period.Create(FirstMonth.EndUtc.AddSeconds(1), FirstMonth.EndUtc.AddDays(30));
        var overlap = Period.Create(FirstMonth.EndUtc.AddSeconds(-1), FirstMonth.EndUtc.AddDays(30));

        DomainAssert.Throws(SubscriptionErrors.PeriodNotContiguous, () => subscription.RenewPeriod(gap, _clock));
        DomainAssert.Throws(SubscriptionErrors.PeriodNotContiguous, () => subscription.RenewPeriod(overlap, _clock));
        DomainAssert.Throws(SubscriptionErrors.PeriodNotContiguous, () => subscription.RenewPeriod(FirstMonth, _clock));

        Assert.Equal(FirstMonth, subscription.CurrentPeriod);
        DomainAssert.NoEvents(subscription);
    }

    [Fact]
    public void RenewPeriod_requires_an_active_subscription()
    {
        var trialing = Trialing();
        DomainAssert.Throws(
            SubscriptionErrors.NotActive,
            () => trialing.RenewPeriod(Period.Create(Trial.EndUtc, Trial.EndUtc.AddDays(14)), _clock));

        foreach (var subscription in new[] { PastDue(), Suspended() })
        {
            DomainAssert.Throws(SubscriptionErrors.NotActive, () => subscription.RenewPeriod(SecondMonth, _clock));

            Assert.Equal(FirstMonth, subscription.CurrentPeriod);
            DomainAssert.NoEvents(subscription);
        }
    }

    [Fact]
    public void RenewPeriod_rejects_a_renewal_before_the_current_period_has_ended()
    {
        var subscription = Active();

        DomainAssert.Throws(SubscriptionErrors.PeriodNotEnded, () => subscription.RenewPeriod(SecondMonth, _clock));
        _clock.Set(FirstMonth.EndUtc.AddTicks(-1));
        DomainAssert.Throws(SubscriptionErrors.PeriodNotEnded, () => subscription.RenewPeriod(SecondMonth, _clock));

        Assert.Equal(FirstMonth, subscription.CurrentPeriod);
        DomainAssert.NoEvents(subscription);
    }

    [Fact]
    public void RenewPeriod_cannot_be_chained_in_advance()
    {
        var subscription = Active();
        _clock.Set(FirstMonth.EndUtc);
        subscription.RenewPeriod(SecondMonth, _clock);
        var thirdMonth = Period.Create(SecondMonth.EndUtc, SecondMonth.EndUtc.AddDays(30));

        DomainAssert.Throws(SubscriptionErrors.PeriodNotEnded, () => subscription.RenewPeriod(thirdMonth, _clock));

        Assert.Equal(SecondMonth, subscription.CurrentPeriod);
    }

    [Fact]
    public void RenewPeriod_does_not_cancel_before_the_scheduled_period_end()
    {
        var subscription = Active();
        subscription.ScheduleCancellation(_clock);
        subscription.ClearDomainEvents();
        _clock.Set(FirstMonth.EndUtc.AddTicks(-1));

        DomainAssert.Throws(SubscriptionErrors.PeriodNotEnded, () => subscription.RenewPeriod(SecondMonth, _clock));

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.True(subscription.CancelAtPeriodEnd);
        Assert.Null(subscription.CancelledAtUtc);
        DomainAssert.NoEvents(subscription);
    }

    [Fact]
    public void RenewPeriod_honours_a_scheduled_cancellation_whatever_the_live_status()
    {
        var trialEnd = Period.Create(Trial.EndUtc, Trial.EndUtc.AddDays(30));
        var cases = new (SaasSubscription Subscription, Period Next)[]
        {
            (Trialing(), trialEnd), (Active(), SecondMonth), (PastDue(), SecondMonth), (Suspended(), SecondMonth),
        };

        foreach (var (subscription, next) in cases)
        {
            subscription.ScheduleCancellation(_clock);
            subscription.ClearDomainEvents();
            _clock.Set(subscription.CurrentPeriod.EndUtc);

            subscription.RenewPeriod(next, _clock);

            Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
            Assert.Equal(CancellationReason.AtPeriodEnd, DomainAssert.Raised<SubscriptionCancelled>(subscription).Reason);
            DomainAssert.NotRaised<SubscriptionPeriodRenewed>(subscription);
            _clock.Set(Now);
        }
    }

    [Fact]
    public void RenewPeriod_cancels_instead_of_renewing_when_cancellation_is_scheduled()
    {
        var subscription = Active();
        subscription.ScheduleCancellation(_clock);
        subscription.ClearDomainEvents();
        _clock.Set(FirstMonth.EndUtc);

        subscription.RenewPeriod(SecondMonth, _clock);

        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.Equal(FirstMonth, subscription.CurrentPeriod);
        Assert.Equal(FirstMonth.EndUtc, subscription.CancelledAtUtc);
        Assert.Equal(CancellationReason.AtPeriodEnd, DomainAssert.Raised<SubscriptionCancelled>(subscription).Reason);
        DomainAssert.NotRaised<SubscriptionPeriodRenewed>(subscription);
    }

    [Fact]
    public void Full_lifecycle_trial_paid_past_due_recovered_renewed_cancelled()
    {
        var subscription = Trialing();

        subscription.Activate(FirstMonth, _clock);
        _clock.Set(FirstMonth.StartUtc);
        subscription.MarkPastDue(_clock);
        subscription.Suspend(SuspensionReason.PaymentOverdue, _clock);
        subscription.Activate(FirstMonth, _clock);
        subscription.ChangePlan(_pro, _clock);
        _clock.Set(FirstMonth.EndUtc);
        subscription.RenewPeriod(SecondMonth, _clock);
        subscription.Cancel(_clock);

        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.Equal(
            [
                typeof(SubscriptionActivated), typeof(SubscriptionPastDue), typeof(SubscriptionSuspended),
                typeof(SubscriptionActivated), typeof(SubscriptionPlanChangeScheduled), typeof(SubscriptionPlanChanged), typeof(SubscriptionPeriodRenewed),
                typeof(SubscriptionCancelled),
            ],
            subscription.DomainEvents.Select(domainEvent => domainEvent.GetType()));
    }
}
