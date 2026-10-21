using Integrations.Domain;

namespace Integrations.Domain;

public class IntegrationValueObjectsTests
{
    [Theory]
    [InlineData("https://hooks.example.com/saas", "https://hooks.example.com/saas")]
    [InlineData("  HTTPS://Hooks.Example.com/saas?x=1 ", "https://hooks.example.com/saas?x=1")]
    [InlineData("https://hooks.example.com:8443", "https://hooks.example.com:8443/")]
    public void WebhookUrl_accepts_and_normalizes_an_https_url(string input, string expected) =>
        Assert.Equal(expected, WebhookUrl.Create(input).Value);

    [Fact]
    public void WebhookUrl_exposes_its_host() =>
        Assert.Equal("hooks.example.com", WebhookUrl.Create("https://hooks.example.com/saas").Host);

    [Theory]
    [InlineData("http://hooks.example.com/saas")]
    [InlineData("HTTP://hooks.example.com")]
    [InlineData("ftp://hooks.example.com/saas")]
    [InlineData("ws://hooks.example.com")]
    public void WebhookUrl_rejects_http_and_any_other_non_https_scheme(string value) =>
        DomainAssert.Throws(WebhookEndpointErrors.UrlNotHttps, () => WebhookUrl.Create(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hooks.example.com/saas")]
    [InlineData("/relative/path")]
    [InlineData("https://")]
    [InlineData("not a url")]
    public void WebhookUrl_rejects_a_value_that_is_not_an_absolute_url(string? value) =>
        DomainAssert.Throws(WebhookEndpointErrors.UrlInvalid, () => WebhookUrl.Create(value));

    [Theory]
    [InlineData("https://user:pass@hooks.example.com/saas")]
    [InlineData("https://token@hooks.example.com/saas")]
    public void WebhookUrl_rejects_embedded_credentials(string value) =>
        DomainAssert.Throws(WebhookEndpointErrors.UrlHasCredentials, () => WebhookUrl.Create(value));

    [Theory]
    [InlineData("https://@hooks.example.com/saas")]
    [InlineData("https://hooks.example.com@evil.example.org/saas")]
    [InlineData("https://hooks.example.com:443@10.0.0.1/saas")]
    [InlineData("https://hooks.example.com%40evil.example.org@hooks.example.com/")]
    public void WebhookUrl_rejects_any_at_sign_in_the_authority(string value) =>
        DomainAssert.Throws(WebhookEndpointErrors.UrlHasCredentials, () => WebhookUrl.Create(value));

    [Fact]
    public void WebhookUrl_accepts_an_at_sign_after_the_authority() =>
        Assert.Equal(
            "https://hooks.example.com/users/@me?to=a@b.example",
            WebhookUrl.Create("https://hooks.example.com/users/@me?to=a@b.example").Value);

    [Theory]
    [InlineData("https://localhost/hook")]
    [InlineData("https://LOCALHOST:8443/hook")]
    [InlineData("https://localhost./hook")]
    [InlineData("https://api.localhost/hook")]
    [InlineData("https://127.0.0.1/hook")]
    [InlineData("https://127.8.9.10/hook")]
    [InlineData("https://[::1]/hook")]
    public void WebhookUrl_rejects_a_loopback_host(string value) =>
        DomainAssert.Throws(WebhookEndpointErrors.UrlHostNotPublic, () => WebhookUrl.Create(value));

    [Theory]
    [InlineData("https://10.0.0.1/hook")]
    [InlineData("https://10.255.255.255/hook")]
    [InlineData("https://172.16.0.1/hook")]
    [InlineData("https://172.31.255.254/hook")]
    [InlineData("https://192.168.1.10/hook")]
    [InlineData("https://169.254.169.254/latest/meta-data/")]
    [InlineData("https://0.0.0.0/hook")]
    [InlineData("https://[::]/hook")]
    [InlineData("https://[fc00::1]/hook")]
    [InlineData("https://[fd12:3456:789a::1]/hook")]
    [InlineData("https://[fe80::1]/hook")]
    [InlineData("https://[febf::1]/hook")]
    [InlineData("https://[::ffff:10.0.0.1]/hook")]
    [InlineData("https://[::ffff:169.254.169.254]/hook")]
    [InlineData("https://[::10.0.0.1]/hook")]
    public void WebhookUrl_rejects_a_private_link_local_unique_local_or_unspecified_address(string value) =>
        DomainAssert.Throws(WebhookEndpointErrors.UrlHostNotPublic, () => WebhookUrl.Create(value));

    [Theory]
    [InlineData("https://2130706433/hook")] // 127.0.0.1 sous forme d'un seul nombre décimal
    [InlineData("https://0x7f000001/hook")] // 127.0.0.1 en hexadécimal
    [InlineData("https://0x7f.0.0.1/hook")]
    [InlineData("https://0177.0.0.1/hook")] // octal
    [InlineData("https://167772161/hook")] // 10.0.0.1
    [InlineData("https://0xA9FEA9FE/hook")] // 169.254.169.254
    [InlineData("https://192.168.257/hook")] // 192.168.1.1 en trois parties
    [InlineData("https://10.1/hook")] // 10.0.0.1 en deux parties
    public void WebhookUrl_rejects_numeric_spellings_of_a_forbidden_address(string value) =>
        DomainAssert.Throws(WebhookEndpointErrors.UrlHostNotPublic, () => WebhookUrl.Create(value));

    [Theory]
    [InlineData("https://intranet/hook")]
    [InlineData("https://kubernetes:6443/hook")]
    [InlineData("https://intranet./hook")]
    public void WebhookUrl_rejects_a_host_name_without_a_dot(string value) =>
        DomainAssert.Throws(WebhookEndpointErrors.UrlHostNotPublic, () => WebhookUrl.Create(value));

    [Fact]
    public void WebhookUrl_rejects_port_zero() =>
        DomainAssert.Throws(WebhookEndpointErrors.UrlPortInvalid, () => WebhookUrl.Create("https://hooks.example.com:0/hook"));

    [Theory]
    [InlineData("https://hooks.example.com:8443/hook", "https://hooks.example.com:8443/hook")]
    [InlineData("https://203.0.113.10:9443/hook", "https://203.0.113.10:9443/hook")]
    [InlineData("https://172.32.0.1/hook", "https://172.32.0.1/hook")]
    [InlineData("https://172.15.0.1/hook", "https://172.15.0.1/hook")]
    [InlineData("https://169.253.1.1/hook", "https://169.253.1.1/hook")]
    [InlineData("https://[2001:db8::1]:8443/hook", "https://[2001:db8::1]:8443/hook")]
    public void WebhookUrl_accepts_a_public_host_with_a_custom_port(string input, string expected) =>
        Assert.Equal(expected, WebhookUrl.Create(input).Value);

    [Fact]
    public void WebhookUrl_length_is_bounded() =>
        DomainAssert.Throws(
            WebhookEndpointErrors.UrlInvalid,
            () => WebhookUrl.Create("https://hooks.example.com/" + new string('a', WebhookUrl.MaxLength)));

    [Theory]
    [InlineData("orders.order_placed", "orders.order_placed")]
    [InlineData(" payments.payment.captured ", "payments.payment.captured")]
    [InlineData("kds2.ticket_bumped", "kds2.ticket_bumped")]
    public void EventTypeCode_is_trimmed_and_otherwise_kept_as_is(string input, string expected) =>
        Assert.Equal(expected, EventTypeCode.Create(input).Value);

    [Theory]
    [InlineData("Orders.order_placed")]
    [InlineData("orders.Order_Placed")]
    [InlineData("ORDERS.ORDER_PLACED")]
    public void EventTypeCode_rejects_uppercase_instead_of_lowering_it(string value) =>
        DomainAssert.Throws(WebhookEndpointErrors.EventTypeInvalid, () => EventTypeCode.Create(value));

    [Theory]
    [InlineData("orders.read")]
    [InlineData("orders.order_placed")]
    [InlineData("a1.b_2.c3")]
    public void EventTypeCode_and_ApiScope_share_one_grammar(string value)
    {
        Assert.Equal(ApiScope.Create(value).Value, EventTypeCode.Create(value).Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("orders")]
    [InlineData("orders.")]
    [InlineData("orders..placed")]
    [InlineData("orders.order placed")]
    [InlineData("*")]
    [InlineData("orders.*")]
    [InlineData("orders.1placed")]
    [InlineData("orders.order-placed")]
    public void EventTypeCode_rejects_an_invalid_code(string? value) =>
        DomainAssert.Throws(WebhookEndpointErrors.EventTypeInvalid, () => EventTypeCode.Create(value));

    [Fact]
    public void EventTypeCode_length_is_bounded() =>
        DomainAssert.Throws(
            WebhookEndpointErrors.EventTypeInvalid,
            () => EventTypeCode.Create("orders." + new string('a', EventTypeCode.MaxLength)));

    [Fact]
    public void SecretReference_is_kept_as_is()
    {
        Assert.Equal("vault://webhooks/ep-1/v2", SecretReference.Create("vault://webhooks/ep-1/v2").Value);
        Assert.Equal(SecretReference.Create("ref-1"), SecretReference.Create("ref-1"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ref 1")]
    public void SecretReference_rejects_an_empty_or_whitespace_value(string? value) =>
        DomainAssert.Throws(WebhookEndpointErrors.SecretReferenceInvalid, () => SecretReference.Create(value));

    [Fact]
    public void SecretReference_length_is_bounded() =>
        DomainAssert.Throws(
            WebhookEndpointErrors.SecretReferenceInvalid,
            () => SecretReference.Create(new string('r', SecretReference.MaxLength + 1)));

    [Fact]
    public void Default_retry_policy_is_bounded()
    {
        var policy = WebhookRetryPolicy.Default;

        Assert.Equal(6, policy.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.InitialDelay);
        Assert.Equal(TimeSpan.FromHours(1), policy.MaxDelay);
    }

    [Theory]
    [InlineData(0, 30, 60)]
    [InlineData(-1, 30, 60)]
    [InlineData(51, 30, 60)]
    [InlineData(3, 0, 60)]
    [InlineData(3, -5, 60)]
    [InlineData(3, 30, 29)]
    [InlineData(3, 30, 7 * 24 * 3600 + 1)]
    public void Retry_policy_rejects_invalid_settings(int maxAttempts, int initialSeconds, int maxSeconds) =>
        DomainAssert.Throws(
            WebhookDeliveryErrors.RetryPolicyInvalid,
            () => WebhookRetryPolicy.Create(maxAttempts, TimeSpan.FromSeconds(initialSeconds), TimeSpan.FromSeconds(maxSeconds)));

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(4, 80)]
    [InlineData(5, 100)]
    [InlineData(6, 100)]
    [InlineData(49, 100)]
    public void Retry_policy_backoff_doubles_up_to_the_cap(int failedAttempt, int expectedSeconds)
    {
        var policy = WebhookRetryPolicy.Create(50, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(100));

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), policy.DelayAfter(failedAttempt));
    }

    [Fact]
    public void Retry_policy_max_delay_is_capped_so_the_backoff_cannot_overflow()
    {
        Assert.Equal(TimeSpan.FromDays(7), WebhookRetryPolicy.MaxAllowedDelay);
        DomainAssert.Throws(
            WebhookDeliveryErrors.RetryPolicyInvalid,
            () => WebhookRetryPolicy.Create(3, TimeSpan.FromSeconds(1), TimeSpan.MaxValue));
        DomainAssert.Throws(
            WebhookDeliveryErrors.RetryPolicyInvalid,
            () => WebhookRetryPolicy.Create(3, TimeSpan.MaxValue, TimeSpan.MaxValue));

        var widest = WebhookRetryPolicy.Create(
            WebhookRetryPolicy.MaxAllowedAttempts, WebhookRetryPolicy.MaxAllowedDelay, WebhookRetryPolicy.MaxAllowedDelay);
        var steepest = WebhookRetryPolicy.Create(
            WebhookRetryPolicy.MaxAllowedAttempts, TimeSpan.FromTicks(1), WebhookRetryPolicy.MaxAllowedDelay);

        Assert.Equal(WebhookRetryPolicy.MaxAllowedDelay, widest.DelayAfter(WebhookRetryPolicy.MaxAllowedAttempts));
        Assert.Equal(WebhookRetryPolicy.MaxAllowedDelay, steepest.DelayAfter(int.MaxValue));
    }

    [Fact]
    public void Retry_policy_backoff_rejects_a_non_positive_attempt_number() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WebhookRetryPolicy.Default.DelayAfter(0));

    [Fact]
    public void Retry_policy_knows_when_attempts_are_exhausted()
    {
        var policy = WebhookRetryPolicy.Create(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        Assert.True(policy.AllowsAttemptAfter(2));
        Assert.False(policy.AllowsAttemptAfter(3));
        Assert.Equal(WebhookRetryPolicy.Create(3, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)), policy);
    }

    [Fact]
    public void Payload_snapshot_keeps_type_version_and_body()
    {
        var eventType = EventTypeCode.Create("orders.order_placed");

        var snapshot = WebhookPayloadSnapshot.Create(eventType, 2, "{\"orderId\":\"1\"}");

        Assert.Equal(eventType, snapshot.EventType);
        Assert.Equal(2, snapshot.SchemaVersion);
        Assert.Equal("{\"orderId\":\"1\"}", snapshot.Payload);
        Assert.Equal(WebhookPayloadSnapshot.Create(eventType, 2, "{\"orderId\":\"1\"}"), snapshot);
        Assert.NotEqual(WebhookPayloadSnapshot.Create(eventType, 3, "{\"orderId\":\"1\"}"), snapshot);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Payload_snapshot_rejects_a_schema_version_below_one(int version) =>
        DomainAssert.Throws(
            WebhookDeliveryErrors.PayloadSchemaVersionInvalid,
            () => WebhookPayloadSnapshot.Create(EventTypeCode.Create("orders.order_placed"), version, "{}"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Payload_snapshot_rejects_an_empty_body(string? payload) =>
        DomainAssert.Throws(
            WebhookDeliveryErrors.PayloadEmpty,
            () => WebhookPayloadSnapshot.Create(EventTypeCode.Create("orders.order_placed"), 1, payload));

    [Fact]
    public void Payload_snapshot_size_is_bounded()
    {
        var eventType = EventTypeCode.Create("orders.order_placed");

        Assert.NotNull(WebhookPayloadSnapshot.Create(eventType, 1, new string('x', WebhookPayloadSnapshot.MaxPayloadLength)));
        DomainAssert.Throws(
            WebhookDeliveryErrors.PayloadTooLarge,
            () => WebhookPayloadSnapshot.Create(eventType, 1, new string('x', WebhookPayloadSnapshot.MaxPayloadLength + 1)));
    }

    [Fact]
    public void ApiKeyHash_is_opaque_and_never_printed()
    {
        var value = new string('a', 64);

        var hash = ApiKeyHash.Create(value);

        Assert.Equal(value, hash.Value);
        Assert.Equal(ApiKeyHash.Create(value), hash);
        Assert.DoesNotContain(value, hash.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void ApiKeyHash_rejects_an_invalid_value(string? value) =>
        DomainAssert.Throws(ApiCredentialErrors.KeyHashInvalid, () => ApiKeyHash.Create(value));

    [Fact]
    public void ApiKeyHash_length_is_bounded()
    {
        Assert.NotNull(ApiKeyHash.Create(new string('a', ApiKeyHash.MinLength)));
        Assert.NotNull(ApiKeyHash.Create(new string('a', ApiKeyHash.MaxLength)));
        DomainAssert.Throws(ApiCredentialErrors.KeyHashInvalid, () => ApiKeyHash.Create(new string('a', ApiKeyHash.MinLength - 1)));
        DomainAssert.Throws(ApiCredentialErrors.KeyHashInvalid, () => ApiKeyHash.Create(new string('a', ApiKeyHash.MaxLength + 1)));
    }

    [Theory]
    [InlineData("orders.read", "orders.read")]
    [InlineData(" catalog.products.write ", "catalog.products.write")]
    [InlineData("orders_v2.read_all", "orders_v2.read_all")]
    public void ApiScope_follows_the_permission_code_grammar_and_is_trimmed(string input, string expected) =>
        Assert.Equal(expected, ApiScope.Create(input).Value);

    [Theory]
    [InlineData("Orders.read")]
    [InlineData("orders.Read")]
    [InlineData("ORDERS.READ")]
    public void ApiScope_rejects_uppercase_instead_of_lowering_it(string value) =>
        DomainAssert.Throws(ApiCredentialErrors.ScopeInvalid, () => ApiScope.Create(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("orders")]
    [InlineData("  ")]
    [InlineData("orders.")]
    [InlineData(".read")]
    [InlineData("orders..read")]
    [InlineData("orders.*")]
    [InlineData("orders read")]
    [InlineData("orders:read")]
    [InlineData("1orders.read")]
    [InlineData("orders.1read")]
    [InlineData("orders-v2.read")]
    public void ApiScope_rejects_an_invalid_code(string? value) =>
        DomainAssert.Throws(ApiCredentialErrors.ScopeInvalid, () => ApiScope.Create(value));

    [Fact]
    public void ApiScope_length_is_bounded()
    {
        Assert.Equal(100, ApiScope.MaxLength);
        Assert.NotNull(ApiScope.Create("orders." + new string('a', ApiScope.MaxLength - 7)));
        DomainAssert.Throws(ApiCredentialErrors.ScopeInvalid, () => ApiScope.Create("orders." + new string('a', ApiScope.MaxLength - 6)));
    }

    [Fact]
    public void ApiScopeCatalog_knows_exactly_its_scopes()
    {
        var catalog = ApiScopeCatalog.Create([ApiScope.Create("orders.read"), ApiScope.Create("orders.write"), ApiScope.Create("orders.read")]);

        Assert.Equal(2, catalog.Scopes.Count);
        Assert.True(catalog.Contains(ApiScope.Create("orders.write")));
        Assert.False(catalog.Contains(ApiScope.Create("orders.cancel")));
        Assert.Equal(ApiScopeCatalog.Of("orders.write", "orders.read"), catalog);
        Assert.NotEqual(ApiScopeCatalog.Of("orders.read"), catalog);
    }

    [Fact]
    public void ApiScopeCatalog_rejects_an_empty_list()
    {
        DomainAssert.Throws(ApiCredentialErrors.ScopeCatalogEmpty, () => ApiScopeCatalog.Create([]));
        DomainAssert.Throws(ApiCredentialErrors.ScopeCatalogEmpty, () => ApiScopeCatalog.Of());
    }

    [Fact]
    public void ApiScopeCatalog_rejects_a_malformed_scope() =>
        DomainAssert.Throws(ApiCredentialErrors.ScopeInvalid, () => ApiScopeCatalog.Of("orders.read", "Orders.Write"));

    [Fact]
    public void Ids_reject_an_empty_guid()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => WebhookEndpointId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => DeliveryId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => EventId.From(Guid.Empty));
        DomainAssert.Throws(CommonErrors.EmptyId, () => CredentialId.From(Guid.Empty));
    }
}
