using Integrations.Domain;

namespace Integrations.Domain;

public class ApiCredentialTests
{
    private static readonly ApiScope OrdersRead = ApiScope.Create("orders.read");
    private static readonly ApiScope OrdersWrite = ApiScope.Create("orders.write");
    private static readonly ApiScopeCatalog Catalog = ApiScopeCatalog.Of("orders.read", "orders.write", "orders.read.all");
    private static readonly ApiKeyHash Hash = ApiKeyHash.Create(new string('a', 64));
    private static readonly ApiKeyHash OtherHash = ApiKeyHash.Create(new string('b', 64));

    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();

    private ApiCredential Issue(
        string? name = "POS connector",
        IEnumerable<ApiScope>? scopes = null,
        TimeSpan? expiresIn = null,
        ApiScopeCatalog? catalog = null)
    {
        var credential = ApiCredential.Issue(
            _tenantId, name, Hash, scopes ?? [OrdersRead], catalog ?? Catalog, expiresIn is { } delay ? _clock.UtcNow + delay : null, _clock);
        credential.ClearDomainEvents();
        return credential;
    }

    private ApiCredential Revoked()
    {
        var credential = Issue();
        credential.Revoke(_clock);
        credential.ClearDomainEvents();
        return credential;
    }

    [Fact]
    public void Issue_creates_an_active_credential_holding_only_the_hash()
    {
        var expiresAt = _clock.UtcNow.AddDays(90);

        var credential = ApiCredential.Issue(_tenantId, "  POS connector ", Hash, [OrdersRead, OrdersWrite, OrdersRead], Catalog, expiresAt, _clock);

        Assert.NotEqual(default, credential.Id);
        Assert.Equal(_tenantId, credential.TenantId);
        Assert.Equal("POS connector", credential.Name);
        Assert.Equal(Hash, credential.KeyHash);
        Assert.Equal(2, credential.Scopes.Count);
        Assert.Equal(ApiCredentialStatus.Active, credential.Status);
        Assert.Equal(expiresAt, credential.ExpiresAtUtc);
        Assert.Equal(_clock.UtcNow, credential.IssuedAtUtc);
        var issued = DomainAssert.Raised<ApiCredentialIssued>(credential);
        Assert.Equal(_tenantId, issued.TenantId);
        Assert.Equal(credential.Id, issued.CredentialId);
        Assert.Equal("POS connector", issued.Name);
        Assert.Equal(expiresAt, issued.ExpiresAtUtc);
        Assert.DoesNotContain(Hash.Value, issued.ToString());
    }

    [Fact]
    public void Issue_without_expiry_never_expires()
    {
        var credential = Issue();

        Assert.Null(credential.ExpiresAtUtc);
        Assert.True(credential.IsUsableAt(_clock.UtcNow.AddYears(50)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Issue_rejects_an_empty_name(string? name) =>
        DomainAssert.Throws(ApiCredentialErrors.NameEmpty, () => Issue(name));

    [Fact]
    public void Issue_rejects_a_name_over_the_limit() =>
        DomainAssert.Throws(ApiCredentialErrors.NameTooLong, () => Issue(new string('n', ApiCredential.MaxNameLength + 1)));

    [Fact]
    public void Issue_requires_at_least_one_scope() =>
        DomainAssert.Throws(ApiCredentialErrors.ScopesRequired, () => Issue(scopes: []));

    [Fact]
    public void Issue_rejects_more_scopes_than_the_limit()
    {
        var scopes = Enumerable.Range(0, ApiCredential.MaxScopes + 1).Select(i => ApiScope.Create($"orders.s{i}")).ToList();

        DomainAssert.Throws(ApiCredentialErrors.TooManyScopes, () => Issue(scopes: scopes, catalog: ApiScopeCatalog.Create(scopes)));
    }

    [Fact]
    public void Issue_accepts_only_scopes_of_the_catalogue()
    {
        var catalog = ApiScopeCatalog.Of("orders.read", "catalog.products.write");

        var credential = Issue(scopes: [OrdersRead, ApiScope.Create("catalog.products.write")], catalog: catalog);

        Assert.Equal(2, credential.Scopes.Count);
    }

    [Fact]
    public void Issue_rejects_a_scope_that_is_not_in_the_catalogue()
    {
        var catalog = ApiScopeCatalog.Of("orders.read");

        DomainAssert.Throws(ApiCredentialErrors.ScopeUnknown, () => Issue(scopes: [OrdersRead, OrdersWrite], catalog: catalog));
        DomainAssert.Throws(ApiCredentialErrors.ScopeUnknown, () => Issue(scopes: [ApiScope.Create("payments.refund")], catalog: catalog));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void Issue_rejects_an_expiry_that_is_not_in_the_future(int secondsFromNow) =>
        DomainAssert.Throws(ApiCredentialErrors.ExpiryNotInFuture, () => Issue(expiresIn: TimeSpan.FromSeconds(secondsFromNow)));

    [Fact]
    public void Issue_rejects_a_default_tenant() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => ApiCredential.Issue(default, "x", Hash, [OrdersRead], Catalog, null, _clock));

    [Fact]
    public void EnsureCanAccess_allows_a_granted_scope()
    {
        var credential = Issue(scopes: [OrdersRead, OrdersWrite]);

        credential.EnsureCanAccess(OrdersRead, _clock);
        credential.EnsureCanAccess(ApiScope.Create(" orders.write "), _clock);

        Assert.True(credential.HasScope(OrdersWrite, _clock.UtcNow));
    }

    [Fact]
    public void EnsureCanAccess_rejects_a_scope_that_was_not_granted()
    {
        var credential = Issue(scopes: [OrdersRead]);

        DomainAssert.Throws(ApiCredentialErrors.ScopeNotGranted, () => credential.EnsureCanAccess(OrdersWrite, _clock));
        Assert.False(credential.HasScope(OrdersWrite, _clock.UtcNow));
    }

    [Fact]
    public void A_scope_does_not_imply_a_broader_or_narrower_one()
    {
        var credential = Issue(scopes: [ApiScope.Create("orders.read")]);

        DomainAssert.Throws(
            ApiCredentialErrors.ScopeNotGranted,
            () => credential.EnsureCanAccess(ApiScope.Create("orders.read.all"), _clock));
    }

    [Fact]
    public void Credential_is_usable_until_its_expiry_instant_excluded()
    {
        var credential = Issue(expiresIn: TimeSpan.FromHours(1));
        var expiresAt = credential.ExpiresAtUtc!.Value;

        Assert.True(credential.IsUsableAt(expiresAt.AddTicks(-1)));
        Assert.False(credential.IsUsableAt(expiresAt));
        Assert.False(credential.IsUsableAt(expiresAt.AddDays(1)));
        Assert.True(credential.IsExpiredAt(expiresAt));
    }

    [Fact]
    public void EnsureCanAccess_rejects_an_expired_credential()
    {
        var credential = Issue(expiresIn: TimeSpan.FromHours(1));
        _clock.Advance(TimeSpan.FromHours(1));

        DomainAssert.Throws(ApiCredentialErrors.Expired, () => credential.EnsureCanAccess(OrdersRead, _clock));
    }

    [Fact]
    public void HasScope_is_false_once_the_credential_is_expired()
    {
        var credential = Issue(expiresIn: TimeSpan.FromHours(1));
        var expiresAt = credential.ExpiresAtUtc!.Value;

        Assert.True(credential.HasScope(OrdersRead, expiresAt.AddTicks(-1)));
        Assert.False(credential.HasScope(OrdersRead, expiresAt));
        Assert.False(credential.HasScope(OrdersRead, expiresAt.AddDays(1)));
    }

    [Fact]
    public void Revoke_makes_the_credential_unusable_and_raises_ApiCredentialRevoked()
    {
        var credential = Issue();
        _clock.Advance(TimeSpan.FromDays(1));

        credential.Revoke(_clock);

        Assert.Equal(ApiCredentialStatus.Revoked, credential.Status);
        Assert.Equal(_clock.UtcNow, credential.RevokedAtUtc);
        Assert.False(credential.IsUsableAt(_clock.UtcNow));
        var revoked = DomainAssert.Raised<ApiCredentialRevoked>(credential);
        Assert.Equal(_tenantId, revoked.TenantId);
        Assert.Equal(credential.Id, revoked.CredentialId);
    }

    [Fact]
    public void A_revoked_credential_is_rejected_even_for_a_granted_scope()
    {
        var credential = Revoked();

        DomainAssert.Throws(ApiCredentialErrors.Revoked, () => credential.EnsureCanAccess(OrdersRead, _clock));
        Assert.False(credential.HasScope(OrdersRead, _clock.UtcNow));
        Assert.Contains(OrdersRead, credential.Scopes);
        Assert.False(credential.IsUsableAt(_clock.UtcNow));
    }

    [Fact]
    public void Revoked_is_terminal()
    {
        var credential = Revoked();

        DomainAssert.Throws(ApiCredentialErrors.Revoked, () => credential.Revoke(_clock));
        DomainAssert.Throws(ApiCredentialErrors.Revoked, () => credential.Rotate(OtherHash, _clock));
        Assert.Equal(Hash, credential.KeyHash);
        Assert.Equal(ApiCredentialStatus.Revoked, credential.Status);
        DomainAssert.NoEvents(credential);
    }

    [Fact]
    public void Rotate_replaces_the_hash_and_raises_ApiCredentialRotated_without_it()
    {
        var credential = Issue();
        _clock.Advance(TimeSpan.FromDays(30));

        credential.Rotate(OtherHash, _clock);

        Assert.Equal(OtherHash, credential.KeyHash);
        Assert.Equal(_clock.UtcNow, credential.RotatedAtUtc);
        Assert.Equal(ApiCredentialStatus.Active, credential.Status);
        var rotated = DomainAssert.Raised<ApiCredentialRotated>(credential);
        Assert.Equal(credential.Id, rotated.CredentialId);
        Assert.DoesNotContain(OtherHash.Value, rotated.ToString());
    }

    [Fact]
    public void Rotate_with_the_same_hash_is_a_silent_no_op()
    {
        var credential = Issue();
        _clock.Advance(TimeSpan.FromDays(1));

        credential.Rotate(ApiKeyHash.Create(new string('a', 64)), _clock);

        Assert.Equal(Hash, credential.KeyHash);
        Assert.Null(credential.RotatedAtUtc);
        DomainAssert.NoEvents(credential);
    }

    [Fact]
    public void Rotate_with_the_same_hash_is_still_rejected_when_revoked_or_expired()
    {
        DomainAssert.Throws(ApiCredentialErrors.Revoked, () => Revoked().Rotate(Hash, _clock));

        var expired = Issue(expiresIn: TimeSpan.FromHours(1));
        _clock.Advance(TimeSpan.FromHours(2));
        DomainAssert.Throws(ApiCredentialErrors.Expired, () => expired.Rotate(Hash, _clock));
    }

    [Fact]
    public void Rotate_rejects_an_expired_credential()
    {
        var credential = Issue(expiresIn: TimeSpan.FromHours(1));
        _clock.Advance(TimeSpan.FromHours(2));

        DomainAssert.Throws(ApiCredentialErrors.Expired, () => credential.Rotate(OtherHash, _clock));
        Assert.Equal(Hash, credential.KeyHash);
    }

    [Fact]
    public void An_expired_credential_can_still_be_revoked()
    {
        var credential = Issue(expiresIn: TimeSpan.FromHours(1));
        _clock.Advance(TimeSpan.FromHours(2));

        credential.Revoke(_clock);

        Assert.Equal(ApiCredentialStatus.Revoked, credential.Status);
    }

    [Fact]
    public void Scopes_cannot_be_mutated_from_outside()
    {
        var credential = Issue();

        var exposed = Assert.IsAssignableFrom<ICollection<ApiScope>>(credential.Scopes);

        Assert.Throws<NotSupportedException>(() => exposed.Add(OrdersWrite));
        Assert.False(credential.HasScope(OrdersWrite, _clock.UtcNow));
    }

    [Fact]
    public void Aggregates_expose_no_public_setter() =>
        Assert.All(
            new[] { typeof(ApiCredential), typeof(WebhookEndpoint), typeof(WebhookDelivery) }.SelectMany(t => t.GetProperties()),
            property => Assert.True(property.SetMethod is null || !property.SetMethod.IsPublic, property.Name));

    [Fact]
    public void Each_operation_reads_the_clock_once_so_state_and_event_carry_the_same_instant()
    {
        var clock = new TickingClock(_clock.UtcNow);

        var credential = ApiCredential.Issue(_tenantId, "POS", Hash, [OrdersRead], Catalog, null, clock);
        Assert.Equal(credential.IssuedAtUtc, DomainAssert.Raised<ApiCredentialIssued>(credential).OccurredAtUtc);

        credential.Rotate(OtherHash, clock);
        Assert.Equal(credential.RotatedAtUtc, DomainAssert.Raised<ApiCredentialRotated>(credential).OccurredAtUtc);

        credential.Revoke(clock);
        Assert.Equal(credential.RevokedAtUtc, DomainAssert.Raised<ApiCredentialRevoked>(credential).OccurredAtUtc);
    }

    // --- Ajouts : clé dormante, auteur, scopes publiés

    [Fact]
    public void A_never_used_key_becomes_dormant_after_the_delay_counted_from_its_issue()
    {
        var credential = Issue();

        Assert.False(credential.IsDormantAt(_clock.UtcNow.AddDays(89), TimeSpan.FromDays(90)));
        Assert.True(credential.IsDormantAt(_clock.UtcNow.AddDays(90), TimeSpan.FromDays(90)));
    }

    [Fact]
    public void A_revoked_key_is_never_reported_as_dormant()
    {
        Assert.False(Revoked().IsDormantAt(_clock.UtcNow.AddDays(365), TimeSpan.FromDays(90)));
    }

    [Fact]
    public void Issuing_publishes_the_scopes_and_the_author_but_never_the_hash()
    {
        var actor = ChangeActor.ForUser(Guid.NewGuid());

        var credential = ApiCredential.Issue(_tenantId, "POS", Hash, [OrdersWrite, OrdersRead], Catalog, null, _clock, actor);

        var issued = DomainAssert.Raised<ApiCredentialIssued>(credential);
        Assert.Equal(actor, issued.Actor);
        Assert.Equal(["orders.read", "orders.write"], issued.Scopes!);
        Assert.DoesNotContain(Hash.Value, issued.ToString(), StringComparison.Ordinal);
    }
}
