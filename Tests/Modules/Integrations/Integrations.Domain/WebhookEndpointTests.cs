using Integrations.Domain;

namespace Integrations.Domain;

public class WebhookEndpointTests
{
    private static readonly EventTypeCode OrderPlaced = EventTypeCode.Create("orders.order_placed");
    private static readonly EventTypeCode OrderPaid = EventTypeCode.Create("orders.order_paid");
    private static readonly SecretReference Secret = SecretReference.Create("vault://ep/v1");

    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();

    private WebhookEndpoint Endpoint(params EventTypeCode[] events)
    {
        var endpoint = WebhookEndpoint.Create(
            _tenantId,
            WebhookUrl.Create("https://hooks.example.com/saas"),
            events.Length == 0 ? [OrderPlaced] : events,
            Secret,
            _clock);
        endpoint.ClearDomainEvents();
        return endpoint;
    }

    private WebhookEndpoint DisabledWithoutEvents()
    {
        var endpoint = Endpoint();
        endpoint.Disable(_clock);
        endpoint.Unsubscribe(OrderPlaced);
        endpoint.ClearDomainEvents();
        return endpoint;
    }

    [Fact]
    public void Create_builds_an_active_endpoint_and_raises_WebhookEndpointCreated()
    {
        var endpoint = WebhookEndpoint.Create(
            _tenantId, WebhookUrl.Create("https://hooks.example.com/saas"), [OrderPlaced, OrderPaid, OrderPlaced], Secret, _clock);

        Assert.NotEqual(default, endpoint.Id);
        Assert.Equal(_tenantId, endpoint.TenantId);
        Assert.Equal("https://hooks.example.com/saas", endpoint.Url.Value);
        Assert.Equal(2, endpoint.SubscribedEvents.Count);
        Assert.Equal(Secret, endpoint.SecretReference);
        Assert.Equal(WebhookEndpointStatus.Active, endpoint.Status);
        Assert.Equal(_clock.UtcNow, endpoint.CreatedAtUtc);
        var created = DomainAssert.Raised<WebhookEndpointCreated>(endpoint);
        Assert.Equal(_tenantId, created.TenantId);
        Assert.Equal(endpoint.Id, created.EndpointId);
        Assert.Equal("https://hooks.example.com/***", created.RedactedUrl);
    }

    [Fact]
    public void Create_cannot_receive_an_http_url() =>
        DomainAssert.Throws(
            WebhookEndpointErrors.UrlNotHttps,
            () => WebhookEndpoint.Create(_tenantId, WebhookUrl.Create("http://hooks.example.com/saas"), [OrderPlaced], Secret, _clock));

    [Fact]
    public void Create_requires_at_least_one_event() =>
        DomainAssert.Throws(
            WebhookEndpointErrors.EventsRequired,
            () => WebhookEndpoint.Create(_tenantId, WebhookUrl.Create("https://hooks.example.com"), [], Secret, _clock));

    [Fact]
    public void Create_rejects_more_events_than_the_limit()
    {
        var events = Enumerable.Range(0, WebhookEndpoint.MaxSubscribedEvents + 1).Select(i => EventTypeCode.Create($"orders.e{i}"));

        DomainAssert.Throws(
            WebhookEndpointErrors.TooManyEvents,
            () => WebhookEndpoint.Create(_tenantId, WebhookUrl.Create("https://hooks.example.com"), events, Secret, _clock));
    }

    [Fact]
    public void Create_rejects_a_default_tenant() =>
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => WebhookEndpoint.Create(default, WebhookUrl.Create("https://hooks.example.com"), [OrderPlaced], Secret, _clock));

    [Fact]
    public void Subscribe_adds_an_event()
    {
        var endpoint = Endpoint();

        endpoint.Subscribe(OrderPaid);

        Assert.True(endpoint.IsSubscribedTo(OrderPaid));
        Assert.Equal(2, endpoint.SubscribedEvents.Count);
    }

    [Fact]
    public void Subscribe_rejects_an_event_already_subscribed()
    {
        var endpoint = Endpoint();

        DomainAssert.Throws(WebhookEndpointErrors.AlreadySubscribed, () => endpoint.Subscribe(EventTypeCode.Create("orders.order_placed")));
        Assert.Single(endpoint.SubscribedEvents);
    }

    [Fact]
    public void Subscribe_rejects_going_over_the_limit()
    {
        var endpoint = Endpoint(
            [.. Enumerable.Range(0, WebhookEndpoint.MaxSubscribedEvents).Select(i => EventTypeCode.Create($"orders.e{i}"))]);

        DomainAssert.Throws(WebhookEndpointErrors.TooManyEvents, () => endpoint.Subscribe(OrderPaid));
    }

    [Fact]
    public void Unsubscribe_removes_an_event_when_another_one_remains()
    {
        var endpoint = Endpoint(OrderPlaced, OrderPaid);

        endpoint.Unsubscribe(OrderPaid);

        Assert.False(endpoint.IsSubscribedTo(OrderPaid));
        Assert.Equal(OrderPlaced, Assert.Single(endpoint.SubscribedEvents));
    }

    [Fact]
    public void Unsubscribe_rejects_the_last_event_of_an_active_endpoint()
    {
        var endpoint = Endpoint();

        DomainAssert.Throws(WebhookEndpointErrors.EventsRequired, () => endpoint.Unsubscribe(OrderPlaced));
        Assert.True(endpoint.IsSubscribedTo(OrderPlaced));
    }

    [Fact]
    public void Unsubscribe_allows_the_last_event_of_a_disabled_endpoint()
    {
        var endpoint = Endpoint();
        endpoint.Disable(_clock);

        endpoint.Unsubscribe(OrderPlaced);

        Assert.Empty(endpoint.SubscribedEvents);
    }

    [Fact]
    public void Unsubscribe_rejects_an_event_that_is_not_subscribed() =>
        DomainAssert.Throws(WebhookEndpointErrors.NotSubscribed, () => Endpoint().Unsubscribe(OrderPaid));

    [Fact]
    public void RotateSecretReference_replaces_the_reference_and_raises_an_event_without_it()
    {
        var endpoint = Endpoint();
        var newSecret = SecretReference.Create("vault://ep/v2");
        _clock.Advance(TimeSpan.FromDays(30));

        endpoint.RotateSecretReference(newSecret, _clock);

        Assert.Equal(newSecret, endpoint.SecretReference);
        Assert.Equal(_clock.UtcNow, endpoint.SecretRotatedAtUtc);
        var rotated = DomainAssert.Raised<WebhookSecretReferenceRotated>(endpoint);
        Assert.Equal(endpoint.Id, rotated.EndpointId);
        Assert.DoesNotContain("vault://", rotated.ToString());
    }

    [Fact]
    public void RotateSecretReference_with_the_same_reference_is_a_silent_no_op()
    {
        var endpoint = Endpoint();
        _clock.Advance(TimeSpan.FromDays(1));

        endpoint.RotateSecretReference(SecretReference.Create("vault://ep/v1"), _clock);

        Assert.Equal(Secret, endpoint.SecretReference);
        Assert.Null(endpoint.SecretRotatedAtUtc);
        DomainAssert.NoEvents(endpoint);
    }

    [Fact]
    public void Disable_stops_deliveries_and_raises_WebhookEndpointDisabled()
    {
        var endpoint = Endpoint();
        Assert.True(endpoint.ShouldReceive(OrderPlaced));

        endpoint.Disable(_clock);

        Assert.Equal(WebhookEndpointStatus.Disabled, endpoint.Status);
        Assert.False(endpoint.ShouldReceive(OrderPlaced));
        Assert.True(endpoint.IsSubscribedTo(OrderPlaced));
        Assert.Equal(endpoint.Id, DomainAssert.Raised<WebhookEndpointDisabled>(endpoint).EndpointId);
    }

    [Fact]
    public void Disable_rejects_an_already_disabled_endpoint()
    {
        var endpoint = Endpoint();
        endpoint.Disable(_clock);
        endpoint.ClearDomainEvents();

        DomainAssert.Throws(WebhookEndpointErrors.AlreadyDisabled, () => endpoint.Disable(_clock));
        DomainAssert.NoEvents(endpoint);
    }

    [Fact]
    public void Enable_reactivates_a_disabled_endpoint_and_raises_WebhookEndpointEnabled()
    {
        var endpoint = Endpoint();
        endpoint.Disable(_clock);
        endpoint.ClearDomainEvents();

        endpoint.Enable(_clock);

        Assert.Equal(WebhookEndpointStatus.Active, endpoint.Status);
        Assert.True(endpoint.ShouldReceive(OrderPlaced));
        DomainAssert.Raised<WebhookEndpointEnabled>(endpoint);
    }

    [Fact]
    public void Enable_rejects_an_endpoint_without_any_event()
    {
        var endpoint = DisabledWithoutEvents();

        DomainAssert.Throws(WebhookEndpointErrors.EventsRequired, () => endpoint.Enable(_clock));
        Assert.Equal(WebhookEndpointStatus.Disabled, endpoint.Status);
        DomainAssert.NoEvents(endpoint);
    }

    [Fact]
    public void Enable_succeeds_once_an_event_is_subscribed_again()
    {
        var endpoint = DisabledWithoutEvents();
        endpoint.Subscribe(OrderPaid);

        endpoint.Enable(_clock);

        Assert.Equal(WebhookEndpointStatus.Active, endpoint.Status);
    }

    [Fact]
    public void Enable_rejects_an_already_active_endpoint()
    {
        var endpoint = Endpoint();

        DomainAssert.Throws(WebhookEndpointErrors.AlreadyActive, () => endpoint.Enable(_clock));
        DomainAssert.NoEvents(endpoint);
    }

    [Fact]
    public void ShouldReceive_is_false_for_an_event_that_is_not_subscribed() =>
        Assert.False(Endpoint().ShouldReceive(OrderPaid));

    [Fact]
    public void SubscribedEvents_cannot_be_mutated_from_outside()
    {
        var endpoint = Endpoint();

        var exposed = Assert.IsAssignableFrom<ICollection<EventTypeCode>>(endpoint.SubscribedEvents);

        Assert.Throws<NotSupportedException>(() => exposed.Add(OrderPaid));
        Assert.Throws<NotSupportedException>(exposed.Clear);
        Assert.Single(endpoint.SubscribedEvents);
    }

    [Fact]
    public void Each_operation_reads_the_clock_once_so_state_and_event_carry_the_same_instant()
    {
        var clock = new TickingClock(_clock.UtcNow);

        var endpoint = WebhookEndpoint.Create(
            _tenantId, WebhookUrl.Create("https://hooks.example.com/saas"), [OrderPlaced], Secret, clock);
        Assert.Equal(endpoint.CreatedAtUtc, DomainAssert.Raised<WebhookEndpointCreated>(endpoint).OccurredAtUtc);

        endpoint.RotateSecretReference(SecretReference.Create("vault://ep/v2"), clock);
        Assert.Equal(endpoint.SecretRotatedAtUtc, DomainAssert.Raised<WebhookSecretReferenceRotated>(endpoint).OccurredAtUtc);
    }

    // --- Ajouts : URL masquée, période de grâce du secret, désactivation automatique

    [Theory]
    [InlineData("https://hooks.example.com/saas?token=abc", "https://hooks.example.com/***")]
    [InlineData("https://hooks.example.com:8443/t/abc", "https://hooks.example.com:8443/***")]
    [InlineData("https://hooks.example.com", "https://hooks.example.com/")]
    public void A_url_is_published_without_its_path_nor_its_query(string url, string redacted) =>
        Assert.Equal(redacted, WebhookUrl.Create(url).Redacted);

    [Fact]
    public void A_rotation_with_a_grace_period_keeps_the_previous_secret_signing_until_it_expires()
    {
        var endpoint = Endpoint();
        var next = SecretReference.Create("vault://ep/v2");

        endpoint.RotateSecretReference(next, _clock, TimeSpan.FromHours(24));

        Assert.Equal((next, Secret), (endpoint.SecretReference, endpoint.PreviousSecretReference));
        Assert.Equal(Secret, endpoint.PreviousSecretActiveAt(_clock.UtcNow.AddHours(23)));
        Assert.Null(endpoint.PreviousSecretActiveAt(_clock.UtcNow.AddHours(24)));
        Assert.Equal(_clock.UtcNow.AddHours(24), DomainAssert.Raised<WebhookSecretReferenceRotated>(endpoint).PreviousValidUntilUtc);
        Assert.Null(endpoint.ForgetExpiredPreviousSecret(_clock));
        _clock.Advance(TimeSpan.FromHours(24));
        Assert.Equal(Secret, endpoint.ForgetExpiredPreviousSecret(_clock));
        Assert.Null(endpoint.PreviousSecretReference);
    }

    [Fact]
    public void An_immediate_rotation_revokes_the_previous_secret_at_once()
    {
        var endpoint = Endpoint();
        endpoint.RotateSecretReference(SecretReference.Create("vault://ep/v2"), _clock, TimeSpan.FromHours(24));

        endpoint.RotateSecretReference(SecretReference.Create("vault://ep/v3"), _clock);

        Assert.Null(endpoint.PreviousSecretReference);
        Assert.Null(endpoint.PreviousSecretExpiresAtUtc);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            endpoint.RotateSecretReference(SecretReference.Create("vault://ep/v4"), _clock, TimeSpan.FromDays(8)));
    }

    [Fact]
    public void Too_many_abandoned_deliveries_in_a_row_disable_the_endpoint_once()
    {
        var endpoint = Endpoint();

        Assert.False(endpoint.RecordDeliveryAbandoned(3, _clock));
        Assert.False(endpoint.RecordDeliveryAbandoned(3, _clock));
        Assert.True(endpoint.RecordDeliveryAbandoned(3, _clock));
        Assert.False(endpoint.RecordDeliveryAbandoned(3, _clock));

        Assert.Equal(WebhookEndpointStatus.Disabled, endpoint.Status);
        Assert.Equal(_clock.UtcNow, endpoint.AutoDisabledAtUtc);
        var disabled = DomainAssert.Raised<WebhookEndpointAutoDisabled>(endpoint);
        Assert.Equal((3, "https://hooks.example.com/***"), (disabled.ConsecutiveDeadLetters, disabled.RedactedUrl));
    }

    [Fact]
    public void A_success_or_a_reactivation_resets_the_abandon_count()
    {
        var endpoint = Endpoint();
        endpoint.RecordDeliveryAbandoned(2, _clock);
        endpoint.RecordDeliverySucceeded();
        Assert.False(endpoint.RecordDeliveryAbandoned(2, _clock));
        Assert.True(endpoint.RecordDeliveryAbandoned(2, _clock));

        endpoint.Enable(_clock);

        Assert.Equal((WebhookEndpointStatus.Active, 0), (endpoint.Status, endpoint.ConsecutiveDeadLetters));
        Assert.Null(endpoint.AutoDisabledAtUtc);
    }

    [Fact]
    public void The_author_of_a_change_is_carried_by_its_event()
    {
        var actor = ChangeActor.ForUser(Guid.NewGuid());
        var endpoint = WebhookEndpoint.Create(_tenantId, WebhookUrl.Create("https://hooks.example.com/saas"), [OrderPlaced], Secret, _clock, actor);

        endpoint.Disable(_clock, actor);

        Assert.Equal(actor, DomainAssert.Raised<WebhookEndpointCreated>(endpoint).Actor);
        Assert.Equal(actor, DomainAssert.Raised<WebhookEndpointDisabled>(endpoint).Actor);
    }
}
