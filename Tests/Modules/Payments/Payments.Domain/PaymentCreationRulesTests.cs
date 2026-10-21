namespace Payments.Domain;

/// <summary>
/// Un paiement naît d'une configuration du restaurant : même tenant, configuration active et prête, moyen accepté,
/// établissement de la commande affecté, montant dans ses bornes. Il garde l'établissement, le moyen et ses dates.
/// </summary>
public class PaymentCreationRulesTests
{
    private static readonly FakeClock Clock = new();

    /// <summary>Une configuration en ligne fraîche (modifiable par le test) du tenant de test.</summary>
    private static RestaurantPaymentConfiguration Fresh() => Given.OnlineConfiguration(Given.Tenant);

    private static LocationReference Location(LocationId? id = null) => new(Given.Tenant, id ?? Given.Location, CountryCode.Create("FR"));

    [Fact]
    public void A_payment_keeps_the_location_of_its_order_its_method_and_the_provider_of_its_configuration()
    {
        var order = Given.Order(Given.Eur(80m));

        var payment = Payment.Create(Given.Online, PaymentMethodType.ApplePay, order, Given.Eur(80m), Clock);

        Assert.Equal(Given.Location, payment.LocationId);
        Assert.Equal(PaymentMethodType.ApplePay, payment.Method);
        Assert.Equal(Given.Stripe, payment.Provider);
        var created = DomainAssert.Raised<PaymentCreated>(payment);
        Assert.Equal((Given.Location, PaymentMethodType.ApplePay, Given.Stripe), (created.LocationId, created.Method, created.Provider));
        Assert.Null(payment.AuthorizedAtUtc);
        Assert.Null(payment.FailedAtUtc);
    }

    [Fact]
    public void A_disabled_or_unconfigured_configuration_takes_no_payment()
    {
        var disabled = Fresh();
        disabled.Disable(Clock);
        var provider = PaymentProvider.Register(
            PaymentProviderCode.Create("waiting"),
            PaymentProviderType.Online,
            "Waiting",
            PaymentProviderCapability.Create(new[] { PaymentMethodType.Card }),
            isCodeUnique: true,
            Clock);
        var waiting = RestaurantPaymentConfiguration.Create(Given.Tenant, provider, PaymentMode.TenantManaged, isFirstForTenant: true, Clock);

        DomainAssert.Throws(PaymentConfigurationErrors.Disabled, () =>
            Payment.Create(disabled, PaymentMethodType.Card, Given.Order(Given.Eur(10m)), Given.Eur(10m), Clock));
        DomainAssert.Throws(PaymentConfigurationErrors.Disabled, () =>
            Payment.Create(waiting, PaymentMethodType.Card, Given.Order(Given.Eur(10m)), Given.Eur(10m), Clock));
    }

    [Fact]
    public void A_method_the_restaurant_does_not_accept_is_refused()
    {
        var configuration = Fresh();
        configuration.DeactivatePaymentMethod(PaymentMethodType.ApplePay, Clock);

        DomainAssert.Throws(PaymentConfigurationErrors.MethodNotAccepted, () =>
            Payment.Create(configuration, PaymentMethodType.ApplePay, Given.Order(Given.Eur(10m)), Given.Eur(10m), Clock));
        DomainAssert.Throws(PaymentConfigurationErrors.MethodNotAccepted, () =>
            Payment.Create(configuration, PaymentMethodType.MobileMoney, Given.Order(Given.Eur(10m)), Given.Eur(10m), Clock));
    }

    [Fact]
    public void A_location_without_assignment_is_refused_once_the_configuration_is_assigned()
    {
        var configuration = Fresh();
        configuration.AssignToLocation(Location(), PaymentMethodType.Card, priority: 0, Clock);
        var elsewhere = Given.Order(Given.Eur(10m), location: LocationId.New());

        DomainAssert.Throws(PaymentConfigurationErrors.NotAssignedToLocation, () =>
            Payment.Create(configuration, PaymentMethodType.Card, elsewhere, Given.Eur(10m), Clock));
        // Le moyen non affecté dans l'établissement affecté est refusé lui aussi.
        DomainAssert.Throws(PaymentConfigurationErrors.NotAssignedToLocation, () =>
            Payment.Create(configuration, PaymentMethodType.ApplePay, Given.Order(Given.Eur(10m)), Given.Eur(10m), Clock));
        Assert.Equal(Given.Location, Payment.Create(configuration, PaymentMethodType.Card, Given.Order(Given.Eur(10m)), Given.Eur(10m), Clock).LocationId);
    }

    [Fact]
    public void An_amount_below_the_minimum_or_above_the_maximum_of_the_location_is_refused()
    {
        var configuration = Fresh();
        configuration.AssignToLocation(Location(), PaymentMethodType.Card, priority: 0, Clock, minAmount: Given.Eur(5m), maxAmount: Given.Eur(50m));
        var order = Given.Order(Given.Eur(100m));

        DomainAssert.Throws(PaymentConfigurationErrors.AmountOutOfBounds, () =>
            Payment.Create(configuration, PaymentMethodType.Card, order, Given.Eur(4.99m), Clock));
        DomainAssert.Throws(PaymentConfigurationErrors.AmountOutOfBounds, () =>
            Payment.Create(configuration, PaymentMethodType.Card, order, Given.Eur(50.01m), Clock));
        Assert.Equal(Given.Eur(50m), Payment.Create(configuration, PaymentMethodType.Card, order, Given.Eur(50m), Clock).Amount);
    }

    [Fact]
    public void A_disabled_assignment_takes_no_payment_at_its_location()
    {
        var configuration = Fresh();
        configuration.AssignToLocation(Location(), PaymentMethodType.Card, priority: 0, Clock, isEnabled: false);

        DomainAssert.Throws(PaymentConfigurationErrors.NotAssignedToLocation, () =>
            Payment.Create(configuration, PaymentMethodType.Card, Given.Order(Given.Eur(10m)), Given.Eur(10m), Clock));
    }

    [Fact]
    public void Online_and_counter_payments_need_the_matching_kind_of_provider()
    {
        DomainAssert.Throws(PaymentErrors.OnlineNeedsOnlineProvider, () =>
            Payment.Create(Given.Cash, PaymentMethodType.Cash, Given.Order(Given.Eur(10m)), Given.Eur(10m), Clock));
        DomainAssert.Throws(PaymentErrors.OfflineNeedsOnSiteProvider, () =>
            Payment.RecordOffline(Given.Online, PaymentMethodType.Card, Given.Order(Given.Eur(10m)), Given.Eur(10m), Given.Ref("T-1"), Clock));
    }

    [Fact]
    public void Authorization_and_failure_dates_follow_the_current_attempt()
    {
        var clock = new FakeClock();
        var payment = Given.PaymentWithOpenAttempt(clock: clock);

        clock.Advance(TimeSpan.FromMinutes(1));
        payment.Authorize(Given.Ref("pi_1"), clock);
        var authorizedAt = payment.AuthorizedAtUtc;
        clock.Advance(TimeSpan.FromMinutes(1));
        payment.Fail(Given.Ref("pi_1"), Given.Code("authorization_expired"), clock);
        var failedAt = payment.FailedAtUtc;
        payment.RegisterAttempt(Given.Ref("pi_2"), clock);

        Assert.Equal(clock.UtcNow.AddMinutes(-1), authorizedAt);
        Assert.Equal(clock.UtcNow, failedAt);
        // Le nouvel essai rouvre le paiement : ses dates repartent de zéro, l'historique reste sur les tentatives.
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.AuthorizedAtUtc);
        Assert.Null(payment.FailedAtUtc);
        Assert.Equal(2, payment.Attempts.Count);
    }

    [Fact]
    public void A_configuration_says_why_it_refuses_a_payment_without_creating_one()
    {
        var configuration = Fresh();

        Assert.True(configuration.CanTake(Given.Location, PaymentMethodType.Card, Given.Eur(10m)));
        configuration.EnsureCanTake(Given.Location, PaymentMethodType.Card, Given.Eur(10m));
        DomainAssert.Throws(PaymentConfigurationErrors.MethodNotAccepted, () =>
            configuration.EnsureCanTake(Given.Location, PaymentMethodType.Cash, Given.Eur(10m)));
    }
}
