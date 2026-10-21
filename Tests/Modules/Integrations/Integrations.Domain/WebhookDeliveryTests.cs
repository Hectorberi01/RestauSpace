using Integrations.Domain;

namespace Integrations.Domain;

public class WebhookDeliveryTests
{
    private static readonly WebhookRetryPolicy ThreeAttempts =
        WebhookRetryPolicy.Create(3, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));

    private static readonly EventTypeCode OrderPlaced = EventTypeCode.Create("orders.order_placed");
    private static readonly EventTypeCode OrderPaid = EventTypeCode.Create("orders.order_paid");

    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();
    private readonly EventId _eventId = EventId.New();
    private readonly WebhookEndpoint _endpoint;
    private readonly WebhookEndpointId _endpointId;

    public WebhookDeliveryTests()
    {
        _endpoint = Endpoint(_tenantId);
        _endpointId = _endpoint.Id;
    }

    private static WebhookPayloadSnapshot Payload() => WebhookPayloadSnapshot.Create(OrderPlaced, 1, "{\"id\":1}");

    private WebhookEndpoint Endpoint(TenantId tenantId, params EventTypeCode[] events) =>
        WebhookEndpoint.Create(
            tenantId,
            WebhookUrl.Create("https://hooks.example.com/saas"),
            events.Length == 0 ? [OrderPlaced] : events,
            SecretReference.Create("vault://ep/v1"),
            _clock);

    private WebhookDelivery Delivery(WebhookRetryPolicy? policy = null) =>
        WebhookDelivery.CreateFor(_endpoint, _eventId, Payload(), _clock, policy ?? ThreeAttempts);

    private WebhookDelivery InProgress()
    {
        var delivery = Delivery();
        delivery.StartAttempt(_clock);
        return delivery;
    }

    /// <summary>Démarre (quand elle est due) puis fait échouer une tentative.</summary>
    private void FailOnce(WebhookDelivery delivery)
    {
        if (delivery.NextAttemptAtUtc is { } dueAt && dueAt > _clock.UtcNow)
        {
            _clock.Set(dueAt);
        }

        delivery.StartAttempt(_clock);
        delivery.FailAndScheduleRetry("HTTP 500", _clock);
    }

    private WebhookDelivery Succeeded()
    {
        var delivery = InProgress();
        delivery.Succeed(_clock);
        delivery.ClearDomainEvents();
        return delivery;
    }

    private WebhookDelivery DeadLettered()
    {
        var delivery = Delivery();
        delivery.DeadLetter("endpoint removed", _clock);
        delivery.ClearDomainEvents();
        return delivery;
    }

    [Fact]
    public void CreateFor_builds_a_pending_delivery_due_immediately_from_its_endpoint()
    {
        var delivery = Delivery();

        Assert.NotEqual(default, delivery.Id);
        Assert.Equal(_tenantId, delivery.TenantId);
        Assert.Equal(_endpointId, delivery.EndpointId);
        Assert.Equal(_eventId, delivery.EventId);
        Assert.Equal(Payload(), delivery.Payload);
        Assert.Equal(ThreeAttempts, delivery.RetryPolicy);
        Assert.Equal(WebhookDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(0, delivery.AttemptCount);
        Assert.Equal(_clock.UtcNow, delivery.NextAttemptAtUtc);
        Assert.True(delivery.IsDueAt(_clock.UtcNow));
        Assert.False(delivery.IsAttemptInProgress);
        DomainAssert.NoEvents(delivery);
    }

    [Fact]
    public void CreateFor_takes_tenant_and_endpoint_id_from_the_endpoint()
    {
        var otherTenant = TenantId.New();
        var otherEndpoint = Endpoint(otherTenant);

        var delivery = WebhookDelivery.CreateFor(otherEndpoint, _eventId, Payload(), _clock);

        Assert.Equal(otherTenant, delivery.TenantId);
        Assert.Equal(otherEndpoint.Id, delivery.EndpointId);
        Assert.NotEqual(_tenantId, delivery.TenantId);
    }

    [Fact]
    public void CreateFor_is_the_only_public_factory()
    {
        var factories = typeof(WebhookDelivery)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(WebhookDelivery))
            .ToList();

        var factory = Assert.Single(factories);
        Assert.Equal(nameof(WebhookDelivery.CreateFor), factory.Name);
        Assert.DoesNotContain(
            factory.GetParameters(),
            parameter => parameter.ParameterType == typeof(TenantId) || parameter.ParameterType == typeof(WebhookEndpointId));
        Assert.Empty(typeof(WebhookDelivery).GetConstructors());
    }

    [Fact]
    public void CreateFor_rejects_a_default_event_id() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => WebhookDelivery.CreateFor(_endpoint, default, Payload(), _clock));

    [Fact]
    public void CreateFor_rejects_a_disabled_endpoint()
    {
        _endpoint.Disable(_clock);

        DomainAssert.Throws(
            WebhookDeliveryErrors.EndpointNotActive,
            () => WebhookDelivery.CreateFor(_endpoint, _eventId, Payload(), _clock));
    }

    [Fact]
    public void CreateFor_rejects_an_event_type_the_endpoint_does_not_subscribe_to()
    {
        var endpoint = Endpoint(_tenantId, OrderPaid);

        DomainAssert.Throws(
            WebhookDeliveryErrors.EventNotSubscribed,
            () => WebhookDelivery.CreateFor(endpoint, _eventId, Payload(), _clock));
    }

    [Fact]
    public void CreateFor_rejects_a_disabled_and_unsubscribed_endpoint_as_not_active()
    {
        var endpoint = Endpoint(_tenantId);
        endpoint.Disable(_clock);
        endpoint.Unsubscribe(OrderPlaced);

        DomainAssert.Throws(
            WebhookDeliveryErrors.EndpointNotActive,
            () => WebhookDelivery.CreateFor(endpoint, _eventId, Payload(), _clock));
    }

    [Fact]
    public void CreateFor_accepts_a_re_enabled_endpoint_and_a_newly_subscribed_event()
    {
        var endpoint = Endpoint(_tenantId, OrderPaid);
        endpoint.Disable(_clock);
        endpoint.Subscribe(OrderPlaced);
        endpoint.Enable(_clock);

        var delivery = WebhookDelivery.CreateFor(endpoint, _eventId, Payload(), _clock);

        Assert.Equal(endpoint.Id, delivery.EndpointId);
    }

    [Fact]
    public void CreateFor_pins_the_default_retry_policy_when_none_is_given()
    {
        var delivery = WebhookDelivery.CreateFor(_endpoint, _eventId, Payload(), _clock);

        Assert.Equal(WebhookRetryPolicy.Default, delivery.RetryPolicy);

        delivery.StartAttempt(_clock);
        delivery.FailAndScheduleRetry("HTTP 500", _clock);

        Assert.Equal(_clock.UtcNow + WebhookRetryPolicy.Default.InitialDelay, delivery.NextAttemptAtUtc);
    }

    [Fact]
    public void A_delivery_outlives_the_later_disabling_of_its_endpoint()
    {
        var delivery = Delivery();
        _endpoint.Disable(_clock);

        delivery.StartAttempt(_clock);
        delivery.Succeed(_clock);

        Assert.Equal(WebhookDeliveryStatus.Succeeded, delivery.Status);
    }

    [Fact]
    public void Backoff_at_the_widest_allowed_policy_does_not_overflow()
    {
        var widest = WebhookRetryPolicy.Create(
            WebhookRetryPolicy.MaxAllowedAttempts, WebhookRetryPolicy.MaxAllowedDelay, WebhookRetryPolicy.MaxAllowedDelay);
        var delivery = Delivery(widest);

        for (var attempt = 1; attempt < WebhookRetryPolicy.MaxAllowedAttempts; attempt++)
        {
            FailOnce(delivery);
            Assert.Equal(_clock.UtcNow + WebhookRetryPolicy.MaxAllowedDelay, delivery.NextAttemptAtUtc);
        }

        FailOnce(delivery);

        Assert.Equal(WebhookDeliveryStatus.DeadLetter, delivery.Status);
        Assert.Equal(WebhookRetryPolicy.MaxAllowedAttempts, delivery.AttemptCount);
    }

    [Fact]
    public void StartAttempt_increments_the_attempt_count()
    {
        var delivery = Delivery();

        delivery.StartAttempt(_clock);

        Assert.Equal(1, delivery.AttemptCount);
        Assert.True(delivery.IsAttemptInProgress);
        Assert.Equal(_clock.UtcNow, delivery.CurrentAttemptStartedAtUtc);
        Assert.Null(delivery.NextAttemptAtUtc);
        Assert.Equal(WebhookDeliveryStatus.Pending, delivery.Status);
        Assert.False(delivery.IsDueAt(_clock.UtcNow));
    }

    [Fact]
    public void StartAttempt_rejects_a_second_concurrent_attempt()
    {
        var delivery = InProgress();

        DomainAssert.Throws(WebhookDeliveryErrors.AttemptInProgress, () => delivery.StartAttempt(_clock));
        Assert.Equal(1, delivery.AttemptCount);
    }

    [Fact]
    public void Succeed_ends_the_delivery_and_raises_WebhookDelivered()
    {
        var delivery = InProgress();
        _clock.Advance(TimeSpan.FromSeconds(2));

        delivery.Succeed(_clock);

        Assert.Equal(WebhookDeliveryStatus.Succeeded, delivery.Status);
        Assert.Equal(_clock.UtcNow, delivery.SucceededAtUtc);
        Assert.False(delivery.IsAttemptInProgress);
        Assert.Null(delivery.NextAttemptAtUtc);
        var delivered = DomainAssert.Raised<WebhookDelivered>(delivery);
        Assert.Equal(_tenantId, delivered.TenantId);
        Assert.Equal(delivery.Id, delivered.DeliveryId);
        Assert.Equal(_endpointId, delivered.EndpointId);
        Assert.Equal(_eventId, delivered.EventId);
        Assert.Equal(1, delivered.AttemptCount);
    }

    [Fact]
    public void Succeed_requires_an_attempt_in_progress()
    {
        var delivery = Delivery();

        DomainAssert.Throws(WebhookDeliveryErrors.NoAttemptInProgress, () => delivery.Succeed(_clock));
        Assert.Equal(WebhookDeliveryStatus.Pending, delivery.Status);
        DomainAssert.NoEvents(delivery);
    }

    [Fact]
    public void FailAndScheduleRetry_schedules_the_next_attempt_with_the_policy_backoff()
    {
        var delivery = InProgress();
        _clock.Advance(TimeSpan.FromSeconds(5));

        delivery.FailAndScheduleRetry("  HTTP 503 ", _clock);

        Assert.Equal(WebhookDeliveryStatus.Failed, delivery.Status);
        Assert.Equal("HTTP 503", delivery.LastFailureReason);
        Assert.Equal(_clock.UtcNow + TimeSpan.FromMinutes(1), delivery.NextAttemptAtUtc);
        Assert.False(delivery.IsAttemptInProgress);
        var failed = DomainAssert.Raised<WebhookDeliveryFailed>(delivery);
        Assert.Equal(delivery.Id, failed.DeliveryId);
        Assert.Equal(1, failed.AttemptCount);
        Assert.Equal("HTTP 503", failed.Reason);
        Assert.Equal(delivery.NextAttemptAtUtc, failed.NextAttemptAtUtc);
        DomainAssert.NotRaised<WebhookDeadLettered>(delivery);
    }

    [Fact]
    public void FailAndScheduleRetry_backoff_grows_exponentially()
    {
        var delivery = Delivery();

        FailOnce(delivery);
        var firstDelay = delivery.NextAttemptAtUtc - _clock.UtcNow;
        FailOnce(delivery);
        var secondDelay = delivery.NextAttemptAtUtc - _clock.UtcNow;

        Assert.Equal(TimeSpan.FromMinutes(1), firstDelay);
        Assert.Equal(TimeSpan.FromMinutes(2), secondDelay);
    }

    [Fact]
    public void FailAndScheduleRetry_requires_an_attempt_in_progress()
    {
        var delivery = Delivery();

        DomainAssert.Throws(
            WebhookDeliveryErrors.NoAttemptInProgress,
            () => delivery.FailAndScheduleRetry("HTTP 500", _clock));

        FailOnce(delivery);
        DomainAssert.Throws(
            WebhookDeliveryErrors.NoAttemptInProgress,
            () => delivery.FailAndScheduleRetry("HTTP 500", _clock));
        Assert.Equal(1, delivery.AttemptCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void FailAndScheduleRetry_requires_a_reason(string? reason)
    {
        var delivery = InProgress();

        DomainAssert.Throws(
            WebhookDeliveryErrors.FailureReasonInvalid,
            () => delivery.FailAndScheduleRetry(reason, _clock));
        Assert.True(delivery.IsAttemptInProgress);
        DomainAssert.NoEvents(delivery);
    }

    [Fact]
    public void FailAndScheduleRetry_rejects_a_reason_over_the_limit() =>
        DomainAssert.Throws(
            WebhookDeliveryErrors.FailureReasonInvalid,
            () => InProgress().FailAndScheduleRetry(new string('r', WebhookDelivery.MaxFailureReasonLength + 1), _clock));

    [Fact]
    public void StartAttempt_is_rejected_before_the_retry_is_due()
    {
        var delivery = Delivery();
        FailOnce(delivery);
        _clock.Advance(TimeSpan.FromSeconds(59));

        DomainAssert.Throws(WebhookDeliveryErrors.RetryNotDue, () => delivery.StartAttempt(_clock));

        Assert.Equal(1, delivery.AttemptCount);
        Assert.False(delivery.IsDueAt(_clock.UtcNow));
    }

    [Fact]
    public void StartAttempt_is_allowed_exactly_when_the_retry_is_due()
    {
        var delivery = Delivery();
        FailOnce(delivery);
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(delivery.IsDueAt(_clock.UtcNow));

        delivery.StartAttempt(_clock);

        Assert.Equal(2, delivery.AttemptCount);
        Assert.Equal(WebhookDeliveryStatus.Failed, delivery.Status);
        Assert.True(delivery.IsAttemptInProgress);
    }

    [Fact]
    public void A_retry_can_succeed_after_a_failure()
    {
        var delivery = Delivery();
        FailOnce(delivery);
        delivery.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromMinutes(1));
        delivery.StartAttempt(_clock);

        delivery.Succeed(_clock);

        Assert.Equal(WebhookDeliveryStatus.Succeeded, delivery.Status);
        Assert.Equal(2, DomainAssert.Raised<WebhookDelivered>(delivery).AttemptCount);
    }

    [Fact]
    public void Retry_is_bounded_the_last_allowed_failure_dead_letters_the_delivery()
    {
        var delivery = Delivery();
        FailOnce(delivery);
        FailOnce(delivery);
        Assert.Equal(WebhookDeliveryStatus.Failed, delivery.Status);
        delivery.ClearDomainEvents();

        FailOnce(delivery);

        Assert.Equal(3, delivery.AttemptCount);
        Assert.Equal(WebhookDeliveryStatus.DeadLetter, delivery.Status);
        Assert.Null(delivery.NextAttemptAtUtc);
        Assert.Equal(_clock.UtcNow, delivery.DeadLetteredAtUtc);
        Assert.False(delivery.IsDueAt(_clock.UtcNow.AddYears(1)));
        var deadLettered = DomainAssert.Raised<WebhookDeadLettered>(delivery);
        Assert.Equal(delivery.Id, deadLettered.DeliveryId);
        Assert.Equal(_endpointId, deadLettered.EndpointId);
        Assert.Equal(_eventId, deadLettered.EventId);
        Assert.Equal(3, deadLettered.AttemptCount);
        Assert.Equal("HTTP 500", deadLettered.Reason);
        DomainAssert.NotRaised<WebhookDeliveryFailed>(delivery);

        DomainAssert.Throws(WebhookDeliveryErrors.AlreadyDeadLettered, () => delivery.StartAttempt(_clock));
        Assert.Equal(3, delivery.AttemptCount);
    }

    [Fact]
    public void A_single_attempt_policy_dead_letters_on_the_first_failure()
    {
        var delivery = Delivery(WebhookRetryPolicy.Create(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));

        FailOnce(delivery);

        Assert.Equal(WebhookDeliveryStatus.DeadLetter, delivery.Status);
        DomainAssert.Raised<WebhookDeadLettered>(delivery);
    }

    [Fact]
    public void DeadLetter_gives_up_explicitly_from_pending()
    {
        var delivery = Delivery();

        delivery.DeadLetter("endpoint removed", _clock);

        Assert.Equal(WebhookDeliveryStatus.DeadLetter, delivery.Status);
        Assert.Equal("endpoint removed", delivery.LastFailureReason);
        Assert.Null(delivery.NextAttemptAtUtc);
        Assert.Equal(0, DomainAssert.Raised<WebhookDeadLettered>(delivery).AttemptCount);
    }

    [Fact]
    public void DeadLetter_gives_up_explicitly_from_failed()
    {
        var failed = Delivery();
        FailOnce(failed);
        failed.DeadLetter("operator decision", _clock);
        Assert.Equal(WebhookDeliveryStatus.DeadLetter, failed.Status);
    }

    [Fact]
    public void DeadLetter_is_rejected_while_an_attempt_is_in_flight()
    {
        var inProgress = InProgress();

        DomainAssert.Throws(WebhookDeliveryErrors.AttemptInProgress, () => inProgress.DeadLetter("worker lost", _clock));

        Assert.Equal(WebhookDeliveryStatus.Pending, inProgress.Status);
        Assert.True(inProgress.IsAttemptInProgress);
        Assert.Null(inProgress.DeadLetteredAtUtc);
        DomainAssert.NoEvents(inProgress);

        // La tentative qui était en cours peut encore être enregistrée comme le succès qu'elle a été.
        inProgress.Succeed(_clock);
        Assert.Equal(WebhookDeliveryStatus.Succeeded, inProgress.Status);
    }

    [Fact]
    public void DeadLetter_is_possible_once_the_in_flight_attempt_has_been_ended_as_failed()
    {
        var delivery = InProgress();
        delivery.FailAndScheduleRetry("worker lost: timeout", _clock);

        delivery.DeadLetter("operator decision", _clock);

        Assert.Equal(WebhookDeliveryStatus.DeadLetter, delivery.Status);
    }

    [Fact]
    public void Each_operation_reads_the_clock_once_so_state_and_event_carry_the_same_instant()
    {
        var clock = new TickingClock(_clock.UtcNow);

        var succeeded = Delivery();
        succeeded.StartAttempt(clock);
        succeeded.Succeed(clock);
        Assert.Equal(succeeded.SucceededAtUtc, DomainAssert.Raised<WebhookDelivered>(succeeded).OccurredAtUtc);

        var failed = Delivery();
        failed.StartAttempt(clock);
        failed.FailAndScheduleRetry("HTTP 500", clock);
        var failure = DomainAssert.Raised<WebhookDeliveryFailed>(failed);
        Assert.Equal(failure.OccurredAtUtc + ThreeAttempts.DelayAfter(1), failure.NextAttemptAtUtc);
        Assert.Equal(failed.NextAttemptAtUtc, failure.NextAttemptAtUtc);
    }

    [Fact]
    public void StartAttempt_judges_and_stamps_the_attempt_at_the_same_instant()
    {
        var delivery = Delivery();
        var clock = new TickingClock(delivery.CreatedAtUtc);

        delivery.StartAttempt(clock);

        Assert.Equal(delivery.CreatedAtUtc, delivery.CurrentAttemptStartedAtUtc);
    }

    [Fact]
    public void DeadLetter_requires_a_reason()
    {
        var delivery = Delivery();

        DomainAssert.Throws(WebhookDeliveryErrors.FailureReasonInvalid, () => delivery.DeadLetter(" ", _clock));
        Assert.Equal(WebhookDeliveryStatus.Pending, delivery.Status);
    }

    [Fact]
    public void Succeeded_is_terminal()
    {
        var delivery = Succeeded();

        DomainAssert.Throws(WebhookDeliveryErrors.AlreadySucceeded, () => delivery.StartAttempt(_clock));
        DomainAssert.Throws(WebhookDeliveryErrors.AlreadySucceeded, () => delivery.Succeed(_clock));
        DomainAssert.Throws(WebhookDeliveryErrors.AlreadySucceeded, () => delivery.FailAndScheduleRetry("x", _clock));
        DomainAssert.Throws(WebhookDeliveryErrors.AlreadySucceeded, () => delivery.DeadLetter("x", _clock));
        Assert.Equal(WebhookDeliveryStatus.Succeeded, delivery.Status);
        Assert.True(delivery.IsTerminal);
        DomainAssert.NoEvents(delivery);
    }

    [Fact]
    public void DeadLetter_is_terminal()
    {
        var delivery = DeadLettered();

        DomainAssert.Throws(WebhookDeliveryErrors.AlreadyDeadLettered, () => delivery.StartAttempt(_clock));
        DomainAssert.Throws(WebhookDeliveryErrors.AlreadyDeadLettered, () => delivery.Succeed(_clock));
        DomainAssert.Throws(WebhookDeliveryErrors.AlreadyDeadLettered, () => delivery.FailAndScheduleRetry("x", _clock));
        DomainAssert.Throws(WebhookDeliveryErrors.AlreadyDeadLettered, () => delivery.DeadLetter("x", _clock));
        Assert.Equal(WebhookDeliveryStatus.DeadLetter, delivery.Status);
        Assert.True(delivery.IsTerminal);
        DomainAssert.NoEvents(delivery);
    }

    [Fact]
    public void Payload_snapshot_is_unchanged_across_retries()
    {
        var delivery = Delivery();
        var original = delivery.Payload;

        FailOnce(delivery);
        FailOnce(delivery);

        Assert.Same(original, delivery.Payload);
        Assert.Equal(1, delivery.Payload.SchemaVersion);
    }
}
