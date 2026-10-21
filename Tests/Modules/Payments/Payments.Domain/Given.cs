namespace Payments.Domain;

/// <summary>Builders partagés des tests Payments.</summary>
internal static class Given
{
    public static readonly TenantId Tenant = TenantId.New();
    public static readonly LocationId Location = LocationId.New();
    public static readonly PaymentProviderCode Stripe = PaymentProviderCode.Create("stripe");

    /// <summary>Le restaurant de test accepte « stripe » (carte, Apple Pay) en ligne, avec son compte : prête et active.</summary>
    public static readonly RestaurantPaymentConfiguration Online = OnlineConfiguration(Tenant);

    /// <summary>Le restaurant de test accepte les espèces au comptoir.</summary>
    public static readonly RestaurantPaymentConfiguration Cash = OnSiteConfiguration("cash", PaymentMethodType.Cash);

    /// <summary>Une configuration en ligne « stripe » prête et active pour <paramref name="tenant"/>, valable partout.</summary>
    public static RestaurantPaymentConfiguration OnlineConfiguration(TenantId tenant)
    {
        var clock = new FakeClock();
        var provider = PaymentProvider.Register(
            Stripe,
            PaymentProviderType.Online,
            "Stripe",
            PaymentProviderCapability.Create(new[] { PaymentMethodType.Card, PaymentMethodType.ApplePay }, supportsRefund: true, supportsWebhooks: true),
            isCodeUnique: true,
            clock);
        var configuration = RestaurantPaymentConfiguration.Create(tenant, provider, PaymentMode.TenantManaged, isFirstForTenant: true, clock);
        configuration.Configure(PaymentCredentialReference.ForTenant(tenant, Stripe), clock);
        configuration.Enable(providerIsAvailable: true, clock);
        return configuration;
    }

    /// <summary>Une configuration sur place (active aussitôt) du restaurant de test, pour ce prestataire et ce moyen.</summary>
    public static RestaurantPaymentConfiguration OnSiteConfiguration(string code, PaymentMethodType method)
    {
        var clock = new FakeClock();
        var provider = PaymentProvider.Register(
            PaymentProviderCode.Create(code),
            PaymentProviderType.OnSite,
            code,
            PaymentProviderCapability.Create(new[] { method }, supportsRefund: true),
            isCodeUnique: true,
            clock);
        return RestaurantPaymentConfiguration.Create(Tenant, provider, PaymentMode.TenantManaged, isFirstForTenant: true, clock);
    }

    public static Money Eur(decimal amount) => Money.Of(amount, Currency.EUR);

    public static Money Xof(decimal amount) => Money.Of(amount, Currency.XOF);

    public static ProviderReference Ref(string value) => ProviderReference.Create(value);

    public static PaymentFailureCode Code(string value) => PaymentFailureCode.Create(value);

    /// <summary>Un paiement en attente de 100.00 EUR (sauf indication contraire), encore sans tentative.</summary>
    public static Payment NewPayment(Money? amount = null, IClock? clock = null) =>
        Payment.Create(Online, PaymentMethodType.Card, Order(amount ?? Eur(100m)), amount ?? Eur(100m), clock ?? new FakeClock());

    /// <summary>Une commande du tenant de test, passée dans <see cref="Location"/>, dont il reste <paramref name="amountDue"/> à payer.</summary>
    public static OrderReference Order(Money amountDue, TenantId? tenant = null, OrderId? orderId = null, LocationId? location = null) =>
        OrderReference.Create(tenant ?? Tenant, orderId ?? OrderId.New(), location ?? Location, amountDue);

    /// <summary>Paiement annulé de 100.00 EUR dont la tentative "pi_1" a été autorisée, puis close par l'annulation.</summary>
    public static Payment CancelledAfterAuthorization(IClock? clock = null)
    {
        clock ??= new FakeClock();
        var payment = PaymentIn(PaymentStatus.Authorized, clock);
        payment.Cancel("Order cancelled", clock);
        return payment;
    }

    /// <summary>Un paiement en attente avec une tentative ouverte "pi_1".</summary>
    public static Payment PaymentWithOpenAttempt(Money? amount = null, IClock? clock = null)
    {
        clock ??= new FakeClock();
        var payment = NewPayment(amount, clock);
        payment.RegisterAttempt(Ref("pi_1"), clock);
        return payment;
    }

    /// <summary>Un paiement de 100.00 EUR entièrement capturé via la tentative "pi_1".</summary>
    public static Payment CapturedPayment(IClock? clock = null)
    {
        clock ??= new FakeClock();
        var payment = PaymentWithOpenAttempt(clock: clock);
        payment.Capture(Ref("pi_1"), Eur(100m), clock);
        return payment;
    }

    /// <summary>Un paiement de 100.00 EUR amené au statut demandé, toujours via la tentative "pi_1".</summary>
    public static Payment PaymentIn(PaymentStatus status, IClock? clock = null)
    {
        clock ??= new FakeClock();
        Payment payment;
        switch (status)
        {
            case PaymentStatus.Pending:
                payment = PaymentWithOpenAttempt(clock: clock);
                break;
            case PaymentStatus.Authorized:
                payment = PaymentWithOpenAttempt(clock: clock);
                payment.Authorize(Ref("pi_1"), clock);
                break;
            case PaymentStatus.Failed:
                payment = PaymentWithOpenAttempt(clock: clock);
                payment.Fail(Ref("pi_1"), Given.Code("card_declined"), clock);
                break;
            case PaymentStatus.Cancelled:
                payment = PaymentWithOpenAttempt(clock: clock);
                payment.Cancel("Order cancelled", clock);
                break;
            case PaymentStatus.Captured:
                payment = CapturedPayment(clock);
                break;
            case PaymentStatus.PartiallyRefunded:
                payment = CapturedPayment(clock);
                payment.RegisterRefund(payment.RequestRefund(Eur(40m), "Missing dish", clock), RefundOutcome.Succeeded, clock);
                break;
            default:
                payment = CapturedPayment(clock);
                payment.RegisterRefund(payment.RequestRefund(Eur(100m), "Order cancelled", clock), RefundOutcome.Succeeded, clock);
                break;
        }

        Assert.Equal(status, payment.Status);
        return payment;
    }
}
