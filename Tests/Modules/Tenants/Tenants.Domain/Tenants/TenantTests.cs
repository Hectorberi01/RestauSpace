using Tenants.Domain.Tenants;

namespace Tenants.Domain.Tenants;

public sealed class TenantTests
{
    private readonly FakeClock _clock = new();

    private Tenant TrialTenant(Branding? branding = null)
    {
        var tenant = Tenant.Create(
            TenantName.Create("Chez Hector"), Currency.XOF, TimeZoneId.Create("Africa/Porto-Novo"), _clock, branding);
        tenant.ClearDomainEvents();
        return tenant;
    }

    private Tenant ActiveTenant()
    {
        var tenant = TrialTenant();
        tenant.Activate(_clock);
        tenant.ClearDomainEvents();
        return tenant;
    }

    private Tenant SuspendedTenant()
    {
        var tenant = ActiveTenant();
        tenant.Suspend(TenantSuspensionReason.PaymentOverdue, _clock);
        tenant.ClearDomainEvents();
        return tenant;
    }

    private Tenant ClosedTenant()
    {
        var tenant = ActiveTenant();
        tenant.Close(_clock);
        tenant.ClearDomainEvents();
        return tenant;
    }

    // ---- Create ----

    [Fact]
    public void Create_starts_a_trial_tenant_with_its_settings()
    {
        var tenant = Tenant.Create(
            TenantName.Create("Chez Hector"), Currency.XOF, TimeZoneId.Create("Africa/Porto-Novo"), _clock);

        Assert.NotEqual(Guid.Empty, tenant.Id.Value);
        Assert.Equal("Chez Hector", tenant.Name.Value);
        Assert.Equal(TenantStatus.Trial, tenant.Status);
        Assert.Equal(Currency.XOF, tenant.DefaultCurrency);
        Assert.Equal("Africa/Porto-Novo", tenant.TimeZone.Value);
        Assert.True(tenant.PricesIncludeTax);
        Assert.Equal(_clock.UtcNow, tenant.CreatedAtUtc);
        Assert.Null(tenant.SuspensionReason);
        Assert.Null(tenant.ClosedAtUtc);
    }

    [Fact]
    public void Create_defaults_the_branding_to_the_tenant_name()
    {
        var tenant = TrialTenant();

        Assert.Equal(Branding.Create("Chez Hector"), tenant.Branding);
    }

    [Fact]
    public void Create_keeps_the_given_branding()
    {
        var branding = Branding.Create("Hector's", new Uri("https://cdn.example.com/logo.png"), "#ff0000");

        Assert.Equal(branding, TrialTenant(branding).Branding);
    }

    [Fact]
    public void Create_raises_TenantCreated()
    {
        var tenant = Tenant.Create(TenantName.Create("Chez Hector"), Currency.EUR, TimeZoneId.Create("Europe/Paris"), _clock);

        var created = DomainAssert.Raised<TenantCreated>(tenant);
        Assert.Equal(tenant.Id, created.TenantId);
        Assert.Equal("Chez Hector", created.Name);
        Assert.Equal("EUR", created.DefaultCurrencyCode);
        Assert.Equal("Europe/Paris", created.TimeZoneId);
        Assert.Equal(_clock.UtcNow, created.OccurredAtUtc);
    }

    [Fact]
    public void Create_cannot_be_reached_with_an_invalid_currency_or_time_zone()
    {
        DomainAssert.Throws(
            CommonErrors.CurrencyUnknown,
            () => Tenant.Create(TenantName.Create("Chez Hector"), Currency.FromCode("ZZZ"), TimeZoneId.Utc, _clock));
        DomainAssert.Throws(
            CommonErrors.TimeZoneUnknown,
            () => Tenant.Create(TenantName.Create("Chez Hector"), Currency.EUR, TimeZoneId.Create("Nowhere/City"), _clock));
    }

    [Fact]
    public void Create_rejects_null_arguments()
    {
        var name = TenantName.Create("Chez Hector");

        Assert.Throws<ArgumentNullException>(() => Tenant.Create(null!, Currency.EUR, TimeZoneId.Utc, _clock));
        Assert.Throws<ArgumentNullException>(() => Tenant.Create(name, null!, TimeZoneId.Utc, _clock));
        Assert.Throws<ArgumentNullException>(() => Tenant.Create(name, Currency.EUR, null!, _clock));
        Assert.Throws<ArgumentNullException>(() => Tenant.Create(name, Currency.EUR, TimeZoneId.Utc, null!));
    }

    // ---- Activate ----

    [Fact]
    public void Activate_moves_a_trial_tenant_to_active_and_raises_TenantActivated()
    {
        var tenant = TrialTenant();

        tenant.Activate(_clock);

        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal(tenant.Id, DomainAssert.Raised<TenantActivated>(tenant).TenantId);
    }

    [Fact]
    public void Activate_rejects_an_active_tenant()
    {
        var tenant = ActiveTenant();

        DomainAssert.Throws(TenantErrors.OnlyTrialCanBeActivated, () => tenant.Activate(_clock));
        DomainAssert.NoEvents(tenant);
    }

    [Fact]
    public void Activate_rejects_a_suspended_tenant()
    {
        var tenant = SuspendedTenant();

        DomainAssert.Throws(TenantErrors.OnlyTrialCanBeActivated, () => tenant.Activate(_clock));
        Assert.Equal(TenantStatus.Suspended, tenant.Status);
    }

    // ---- Suspend / Reactivate ----

    [Theory]
    [InlineData(false, TenantStatus.Trial)]
    [InlineData(true, TenantStatus.Active)]
    public void Suspend_suspends_a_trial_or_active_tenant_and_raises_TenantSuspended(bool active, TenantStatus previous)
    {
        var tenant = active ? ActiveTenant() : TrialTenant();
        _clock.Advance(TimeSpan.FromDays(3));

        tenant.Suspend(TenantSuspensionReason.PaymentOverdue, _clock);

        Assert.Equal(TenantStatus.Suspended, tenant.Status);
        Assert.Equal(TenantSuspensionReason.PaymentOverdue, tenant.SuspensionReason);
        Assert.Equal(_clock.UtcNow, tenant.SuspendedAtUtc);
        var suspended = DomainAssert.Raised<TenantSuspended>(tenant);
        Assert.Equal(tenant.Id, suspended.TenantId);
        Assert.Equal(TenantSuspensionReason.PaymentOverdue, suspended.Reason);
        Assert.Equal(previous, suspended.PreviousStatus);
    }

    [Fact]
    public void Suspend_rejects_an_already_suspended_tenant()
    {
        var tenant = SuspendedTenant();

        DomainAssert.Throws(TenantErrors.AlreadySuspended, () => tenant.Suspend(TenantSuspensionReason.PolicyViolation, _clock));
        Assert.Equal(TenantSuspensionReason.PaymentOverdue, tenant.SuspensionReason);
        DomainAssert.NoEvents(tenant);
    }

    [Fact]
    public void Suspend_rejects_an_undefined_reason()
    {
        var tenant = ActiveTenant();

        DomainAssert.Throws(TenantErrors.SuspensionReasonInvalid, () => tenant.Suspend((TenantSuspensionReason)0, _clock));
        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Fact]
    public void Suspend_on_a_closed_tenant_reports_the_closed_state_before_an_invalid_reason()
    {
        var tenant = ClosedTenant();

        DomainAssert.Throws(TenantErrors.Closed, () => tenant.Suspend((TenantSuspensionReason)0, _clock));
        DomainAssert.NoEvents(tenant);
    }

    [Fact]
    public void State_timestamps_equal_the_time_of_the_event_they_come_with()
    {
        var tenant = ActiveTenant();
        _clock.Advance(TimeSpan.FromDays(3));

        tenant.Suspend(TenantSuspensionReason.PaymentOverdue, _clock);
        Assert.Equal(DomainAssert.Raised<TenantSuspended>(tenant).OccurredAtUtc, tenant.SuspendedAtUtc);

        _clock.Advance(TimeSpan.FromDays(1));
        tenant.Close(_clock);
        Assert.Equal(DomainAssert.Raised<TenantClosed>(tenant).OccurredAtUtc, tenant.ClosedAtUtc);
    }

    [Fact]
    public void Suspended_tenant_refuses_business_writes_but_remains_a_live_tenant()
    {
        var tenant = SuspendedTenant();

        Assert.False(tenant.CanAcceptBusinessWrites);
        DomainAssert.Throws(TenantErrors.Suspended, tenant.EnsureCanAcceptBusinessWrites);
        Assert.Equal("Chez Hector", tenant.Name.Value);
        Assert.Null(tenant.ClosedAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Trial_and_active_tenants_accept_business_writes(bool active)
    {
        var tenant = active ? ActiveTenant() : TrialTenant();

        Assert.True(tenant.CanAcceptBusinessWrites);
        tenant.EnsureCanAcceptBusinessWrites();
    }

    [Fact]
    public void Suspended_tenant_can_still_update_its_settings()
    {
        var tenant = SuspendedTenant();

        tenant.Rename(TenantName.Create("Chez Paul"), _clock);
        tenant.ChangeDefaultCurrency(Currency.EUR, hasPricedCatalog: false, _clock);
        tenant.ChangeTimeZone(TimeZoneId.Create("Europe/Paris"), _clock);
        tenant.UpdateBranding(Branding.Create("Paul", primaryColor: "#000"), _clock);

        Assert.Equal(TenantStatus.Suspended, tenant.Status);
        Assert.Equal(4, tenant.DomainEvents.Count);
    }

    [Fact]
    public void Reactivate_moves_a_suspended_tenant_to_active_and_clears_the_suspension()
    {
        var tenant = SuspendedTenant();

        tenant.Reactivate(_clock);

        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Null(tenant.SuspensionReason);
        Assert.Null(tenant.SuspendedAtUtc);
        Assert.True(tenant.CanAcceptBusinessWrites);
        Assert.Equal(tenant.Id, DomainAssert.Raised<TenantReactivated>(tenant).TenantId);
    }

    [Fact]
    public void Reactivate_of_a_tenant_suspended_during_its_trial_makes_it_active()
    {
        var tenant = TrialTenant();
        tenant.Suspend(TenantSuspensionReason.TrialExpired, _clock);

        tenant.Reactivate(_clock);

        Assert.Equal(TenantStatus.Active, tenant.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reactivate_rejects_a_tenant_that_is_not_suspended(bool active)
    {
        var tenant = active ? ActiveTenant() : TrialTenant();

        DomainAssert.Throws(TenantErrors.NotSuspended, () => tenant.Reactivate(_clock));
        DomainAssert.NoEvents(tenant);
    }

    // ---- Close (état terminal) ----

    [Fact]
    public void Close_can_be_reached_from_trial_active_and_suspended()
    {
        var cases = new (Tenant Tenant, TenantStatus Previous)[]
        {
            (TrialTenant(), TenantStatus.Trial),
            (ActiveTenant(), TenantStatus.Active),
            (SuspendedTenant(), TenantStatus.Suspended),
        };

        foreach (var (tenant, previous) in cases)
        {
            tenant.Close(_clock);

            Assert.Equal(TenantStatus.Closed, tenant.Status);
            Assert.Equal(_clock.UtcNow, tenant.ClosedAtUtc);
            var closed = DomainAssert.Raised<TenantClosed>(tenant);
            Assert.Equal(tenant.Id, closed.TenantId);
            Assert.Equal(previous, closed.PreviousStatus);
        }
    }

    [Fact]
    public void Closed_is_terminal_and_rejects_every_transition()
    {
        var tenant = ClosedTenant();

        DomainAssert.Throws(TenantErrors.Closed, () => tenant.Close(_clock));
        DomainAssert.Throws(TenantErrors.Closed, () => tenant.Activate(_clock));
        DomainAssert.Throws(TenantErrors.Closed, () => tenant.Suspend(TenantSuspensionReason.Other, _clock));
        DomainAssert.Throws(TenantErrors.Closed, () => tenant.Reactivate(_clock));
        Assert.Equal(TenantStatus.Closed, tenant.Status);
        DomainAssert.NoEvents(tenant);
    }

    [Fact]
    public void Closed_is_terminal_and_rejects_every_settings_change()
    {
        var tenant = ClosedTenant();

        DomainAssert.Throws(TenantErrors.Closed, () => tenant.Rename(TenantName.Create("Chez Paul"), _clock));
        DomainAssert.Throws(TenantErrors.Closed, () => tenant.ChangeDefaultCurrency(Currency.EUR, hasPricedCatalog: false, _clock));
        DomainAssert.Throws(TenantErrors.Closed, () => tenant.ChangeTimeZone(TimeZoneId.Utc, _clock));
        DomainAssert.Throws(TenantErrors.Closed, () => tenant.UpdateBranding(Branding.Create("Paul"), _clock));
        DomainAssert.Throws(TenantErrors.Closed, () => tenant.Rename(tenant.Name, _clock));
        DomainAssert.Throws(TenantErrors.Closed, () => tenant.ChangePriceTaxMode(false, _clock));
        DomainAssert.Throws(TenantErrors.Closed, () => tenant.ChangePriceTaxMode(true, _clock));
        Assert.Equal("Chez Hector", tenant.Name.Value);
        Assert.Equal(Currency.XOF, tenant.DefaultCurrency);
        Assert.True(tenant.PricesIncludeTax);
        DomainAssert.NoEvents(tenant);
    }

    [Fact]
    public void Closed_tenant_refuses_business_writes()
    {
        var tenant = ClosedTenant();

        Assert.False(tenant.CanAcceptBusinessWrites);
        DomainAssert.Throws(TenantErrors.Closed, tenant.EnsureCanAcceptBusinessWrites);
    }

    // ---- Paramètres ----

    [Fact]
    public void Rename_changes_the_name_and_raises_TenantRenamed()
    {
        var tenant = ActiveTenant();

        tenant.Rename(TenantName.Create("Chez Paul"), _clock);

        Assert.Equal("Chez Paul", tenant.Name.Value);
        var renamed = DomainAssert.Raised<TenantRenamed>(tenant);
        Assert.Equal("Chez Hector", renamed.PreviousName);
        Assert.Equal("Chez Paul", renamed.NewName);
    }

    [Fact]
    public void Rename_does_not_touch_the_branding()
    {
        var tenant = ActiveTenant();

        tenant.Rename(TenantName.Create("Chez Paul"), _clock);

        Assert.Equal("Chez Hector", tenant.Branding.DisplayName);
    }

    [Fact]
    public void ChangeDefaultCurrency_changes_the_currency_and_raises_an_event()
    {
        var tenant = ActiveTenant();

        tenant.ChangeDefaultCurrency(Currency.EUR, hasPricedCatalog: false, _clock);

        Assert.Equal(Currency.EUR, tenant.DefaultCurrency);
        var changed = DomainAssert.Raised<TenantDefaultCurrencyChanged>(tenant);
        Assert.Equal("XOF", changed.PreviousCurrencyCode);
        Assert.Equal("EUR", changed.NewCurrencyCode);
    }

    [Fact]
    public void ChangeDefaultCurrency_is_rejected_once_the_catalog_is_priced()
    {
        var tenant = ActiveTenant();

        DomainAssert.Throws(
            TenantErrors.CurrencyLockedByPricedCatalog,
            () => tenant.ChangeDefaultCurrency(Currency.EUR, hasPricedCatalog: true, _clock));

        Assert.Equal(Currency.XOF, tenant.DefaultCurrency);
        DomainAssert.NoEvents(tenant);
    }

    [Fact]
    public void ChangeDefaultCurrency_to_the_same_currency_is_a_no_op_even_with_a_priced_catalog()
    {
        var tenant = ActiveTenant();

        tenant.ChangeDefaultCurrency(Currency.XOF, hasPricedCatalog: true, _clock);

        Assert.Equal(Currency.XOF, tenant.DefaultCurrency);
        DomainAssert.NoEvents(tenant);
    }

    [Fact]
    public void ChangePriceTaxMode_switches_the_mode_and_raises_TenantPriceTaxModeChanged()
    {
        var tenant = ActiveTenant();

        tenant.ChangePriceTaxMode(false, _clock);

        Assert.False(tenant.PricesIncludeTax);
        var changed = DomainAssert.Raised<TenantPriceTaxModeChanged>(tenant);
        Assert.Equal(tenant.Id, changed.TenantId);
        Assert.False(changed.PricesIncludeTax);
        Assert.Equal(_clock.UtcNow, changed.OccurredAtUtc);

        tenant.ClearDomainEvents();
        tenant.ChangePriceTaxMode(true, _clock);

        Assert.True(tenant.PricesIncludeTax);
        Assert.True(DomainAssert.Raised<TenantPriceTaxModeChanged>(tenant).PricesIncludeTax);
    }

    [Fact]
    public void ChangePriceTaxMode_is_allowed_during_the_trial_and_while_suspended()
    {
        var trial = TrialTenant();
        var suspended = SuspendedTenant();

        trial.ChangePriceTaxMode(false, _clock);
        suspended.ChangePriceTaxMode(false, _clock);

        Assert.False(trial.PricesIncludeTax);
        Assert.False(suspended.PricesIncludeTax);
    }

    [Fact]
    public void ChangeTimeZone_changes_the_time_zone_and_raises_an_event()
    {
        var tenant = ActiveTenant();

        tenant.ChangeTimeZone(TimeZoneId.Create("Europe/Paris"), _clock);

        Assert.Equal("Europe/Paris", tenant.TimeZone.Value);
        var changed = DomainAssert.Raised<TenantTimeZoneChanged>(tenant);
        Assert.Equal("Africa/Porto-Novo", changed.PreviousTimeZoneId);
        Assert.Equal("Europe/Paris", changed.NewTimeZoneId);
    }

    [Fact]
    public void UpdateBranding_replaces_the_branding_and_raises_an_event()
    {
        var tenant = ActiveTenant();
        var branding = Branding.Create("Hector's", new Uri("https://cdn.example.com/logo.png"), "#123456");

        tenant.UpdateBranding(branding, _clock);

        Assert.Equal(branding, tenant.Branding);
        Assert.Equal(branding, DomainAssert.Raised<TenantBrandingUpdated>(tenant).Branding);
    }

    [Fact]
    public void Settings_changes_to_the_same_value_raise_nothing()
    {
        var tenant = ActiveTenant();

        tenant.Rename(TenantName.Create("Chez Hector"), _clock);
        tenant.ChangeDefaultCurrency(Currency.XOF, hasPricedCatalog: false, _clock);
        tenant.ChangeTimeZone(TimeZoneId.Create("Africa/Porto-Novo"), _clock);
        tenant.UpdateBranding(Branding.Create("Chez Hector"), _clock);
        tenant.ChangePriceTaxMode(true, _clock);

        DomainAssert.NoEvents(tenant);
    }

    [Fact]
    public void Settings_changes_are_allowed_during_the_trial()
    {
        var tenant = TrialTenant();

        tenant.Rename(TenantName.Create("Chez Paul"), _clock);
        tenant.ChangeDefaultCurrency(Currency.USD, hasPricedCatalog: false, _clock);

        Assert.Equal(TenantStatus.Trial, tenant.Status);
        Assert.Equal(2, tenant.DomainEvents.Count);
    }
}
