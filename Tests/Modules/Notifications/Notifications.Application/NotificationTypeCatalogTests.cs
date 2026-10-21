using Notifications.Application.Commands;
using Notifications.Application.Queries;
using Notifications.Domain;

namespace Notifications.Application;

/// <summary>Le catalogue des types servi au front : un restaurant ne voit que les messages adressés à ses clients.</summary>
public sealed class NotificationTypeCatalogTests
{
    private readonly ListNotificationTypesHandler _handler = new();

    [Fact]
    public async Task A_restaurant_sees_only_customer_types_with_their_variables_in_catalog_order()
    {
        var types = await _handler.HandleAsync(new ListNotificationTypesQuery(TemplateOwner.Tenant));

        Assert.Equal(19, types.Count);
        Assert.All(types, type => Assert.Equal("customer", type.Audience));
        Assert.DoesNotContain(types, type => type.Key.StartsWith("staff.", StringComparison.Ordinal) || type.Key == "security.otp");
        var ready = Assert.Single(types, type => type.Key == "order.ready");
        Assert.Equal(["restaurant.name", "location.name", "customer.firstName", "order.number", "order.total"], ready.Variables);
        Assert.Contains(NotificationTypes.UnsubscribeUrl, Assert.Single(types, type => type.Key == "marketing.new_menu").Variables);
        Assert.Equal(5, types.Count(type => type.Purpose == NotificationPurpose.Marketing));
    }

    [Fact]
    public async Task The_platform_sees_every_type()
    {
        var types = await _handler.HandleAsync(new ListNotificationTypesQuery(TemplateOwner.Platform));

        Assert.Equal(NotificationTypes.All.Count, types.Count);
        Assert.Equal("account", Assert.Single(types, type => type.Key == "security.otp").Audience);
        Assert.Equal("staff", Assert.Single(types, type => type.Key == "staff.report_ready").Audience);
    }
}
