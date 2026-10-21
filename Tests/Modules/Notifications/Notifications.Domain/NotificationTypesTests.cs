namespace Notifications.Domain;

/// <summary>Étape 5 : le catalogue des types de notification et de leurs variables.</summary>
public class NotificationTypesTests
{
    [Theory]
    // Les types du cahier des charges (ORDER_CONFIRMED -> order.confirmed...).
    [InlineData("order.confirmed")]
    [InlineData("order.ready")]
    [InlineData("order.cancelled")]
    [InlineData("reservation.confirmed")]
    [InlineData("reservation.reminder")]
    [InlineData("reservation.cancelled")]
    [InlineData("payment.succeeded")]
    [InlineData("payment.failed")]
    [InlineData("refund.succeeded")]
    [InlineData("account.welcome")]
    [InlineData("account.password_reset")]
    [InlineData("security.otp")]
    [InlineData("loyalty.reward_earned")]
    [InlineData("marketing.promotional_campaign")]
    public void The_types_of_the_specification_exist(string key) =>
        Assert.NotNull(NotificationTypes.Find(TemplateKey.Create(key)));

    [Fact]
    public void Transactional_and_marketing_types_are_never_mixed()
    {
        var marketing = NotificationTypes.All.Where(type => type.IsMarketing).Select(type => type.Key.Value).ToList();

        Assert.All(marketing, key => Assert.StartsWith("marketing.", key, StringComparison.Ordinal));
        Assert.All(
            NotificationTypes.All.Where(type => !type.Key.Value.StartsWith("marketing.", StringComparison.Ordinal)),
            type => Assert.Equal(NotificationPurpose.Transactional, type.Purpose));
        Assert.Equal(5, marketing.Count);
    }

    [Fact]
    public void An_otp_goes_before_a_campaign()
    {
        Assert.Equal(NotificationPriority.Critical, NotificationTypes.SecurityOtp.Priority);
        Assert.Equal(NotificationPriority.High, NotificationTypes.PaymentFailed.Priority);
        Assert.Equal(NotificationPriority.High, NotificationTypes.OrderReady.Priority);
        Assert.Equal(NotificationPriority.Normal, NotificationTypes.ReservationConfirmed.Priority);
        Assert.All(NotificationTypes.All.Where(type => type.IsMarketing), type => Assert.Equal(NotificationPriority.Low, type.Priority));
    }

    [Fact]
    public void Every_marketing_type_offers_an_unsubscribe_link() =>
        Assert.All(NotificationTypes.All.Where(type => type.IsMarketing), type => Assert.True(type.Allows(NotificationTypes.UnsubscribeUrl)));

    [Fact]
    public void A_type_lists_only_the_variables_it_provides()
    {
        var reservation = NotificationTypes.ReservationConfirmed;

        Assert.True(reservation.Allows("customer.firstName"));
        Assert.True(reservation.Allows("reservation.guestCount"));
        Assert.False(reservation.Allows("order.total"));
        Assert.False(reservation.Allows("Customer.FirstName"));
        Assert.True(NotificationTypes.SecurityOtp.Allows("otp.code"));
        Assert.False(NotificationTypes.OrderConfirmed.Allows("otp.code"));
    }

    [Fact]
    public void Every_variable_name_is_well_formed_and_every_key_is_unique()
    {
        Assert.All(
            NotificationTypes.All.SelectMany(type => type.Variables),
            name => Assert.Equal(new[] { name }, TemplateVariables.Extract("{{" + name + "}}").ToArray()));
        Assert.Equal(NotificationTypes.All.Count, NotificationTypes.All.Select(type => type.Key).Distinct().Count());
    }

    [Fact]
    public void An_unknown_type_is_a_business_error() =>
        DomainAssert.Throws(TemplateErrors.TypeUnknown, () => NotificationTypes.Require(TemplateKey.Create("order.teleported")));
}
