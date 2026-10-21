using System.Reflection;
using Audit.Domain;

namespace Audit.Domain;

public class AuditEntryTests
{
    private const BindingFlags DeclaredInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly FakeClock _clock = new();

    private static readonly AuditAction Action = AuditAction.Create("orders.order.cancelled");
    private static readonly AuditTarget Target = AuditTarget.Create("Order", "42");
    private static readonly CorrelationId Correlation = CorrelationId.Create("req-1");

    private AuditEntry Create(TenantId? tenantId = null, ActorId? actor = null, AuditMetadata? metadata = null) =>
        AuditEntry.Create(tenantId, actor, Action, Target, Correlation, metadata, _clock);

    [Fact]
    public void Create_records_every_fact()
    {
        var tenantId = TenantId.New();
        var actor = ActorId.ForUser(UserId.New());
        var metadata = AuditMetadata.Create([new("reason", "customer request")]);

        var entry = Create(tenantId, actor, metadata);

        Assert.NotEqual(default, entry.Id);
        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal(actor, entry.ActorId);
        Assert.Equal(Action, entry.Action);
        Assert.Equal(Target, entry.Target);
        Assert.Equal("order", entry.TargetType);
        Assert.Equal("42", entry.TargetId);
        Assert.Equal(Correlation, entry.CorrelationId);
        Assert.Same(metadata, entry.Metadata);
        Assert.Equal(_clock.UtcNow, entry.OccurredAtUtc);
    }

    [Fact]
    public void Create_allows_a_platform_action_without_tenant_nor_actor()
    {
        var entry = Create();

        Assert.Null(entry.TenantId);
        Assert.Null(entry.ActorId);
    }

    [Fact]
    public void Create_allows_a_system_actor()
    {
        var entry = Create(TenantId.New(), ActorId.ForSystem("nightly-job"));

        Assert.Equal(ActorType.System, entry.ActorId!.Type);
    }

    [Fact]
    public void Create_defaults_metadata_to_empty() => Assert.Same(AuditMetadata.Empty, Create().Metadata);

    [Fact]
    public void Create_rejects_a_default_tenant_id() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => Create(tenantId: default(TenantId)));

    [Fact]
    public void Create_requires_action_target_correlation_and_clock()
    {
        Assert.Throws<ArgumentNullException>(() => AuditEntry.Create(null, null, null!, Target, Correlation, null, _clock));
        Assert.Throws<ArgumentNullException>(() => AuditEntry.Create(null, null, Action, null!, Correlation, null, _clock));
        Assert.Throws<ArgumentNullException>(() => AuditEntry.Create(null, null, Action, Target, null!, null, _clock));
        Assert.Throws<ArgumentNullException>(() => AuditEntry.Create(null, null, Action, Target, Correlation, null, null!));
    }

    [Fact]
    public void Create_never_stores_a_secret_passed_in_metadata()
    {
        var metadata = AuditMetadata.Create([new("password", "hunter2"), new("Authorization", "Bearer abc")]);

        var entry = Create(metadata: metadata);

        Assert.All(entry.Metadata.Values.Values, value => Assert.Equal(AuditMetadata.RedactedMarker, value));
    }

    [Fact]
    public void Entry_exposes_no_property_setter()
    {
        var properties = typeof(AuditEntry).GetProperties(DeclaredInstance);

        Assert.NotEmpty(properties);
        Assert.All(properties, property => Assert.False(property.CanWrite, $"{property.Name} has a setter"));

        var id = typeof(AuditEntry).GetProperty(nameof(AuditEntry.Id))!;
        Assert.True(id.SetMethod is null || !id.SetMethod.IsPublic);
    }

    [Fact]
    public void Entry_has_only_readonly_fields() =>
        Assert.All(typeof(AuditEntry).GetFields(DeclaredInstance), field => Assert.True(field.IsInitOnly, field.Name));

    [Fact]
    public void Entry_declares_no_instance_method_besides_property_getters()
    {
        var methods = typeof(AuditEntry).GetMethods(DeclaredInstance).Where(m => !m.IsSpecialName);

        Assert.Empty(methods);
    }

    [Fact]
    public void Entry_can_only_be_built_through_its_factory()
    {
        Assert.Empty(typeof(AuditEntry).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.True(typeof(AuditEntry).IsSealed);
    }

    [Fact]
    public void Entry_is_not_an_event_raising_aggregate() =>
        Assert.False(typeof(IAggregateRoot).IsAssignableFrom(typeof(AuditEntry)));

    [Theory]
    [InlineData(typeof(ActorId))]
    [InlineData(typeof(AuditAction))]
    [InlineData(typeof(AuditTarget))]
    [InlineData(typeof(CorrelationId))]
    [InlineData(typeof(AuditMetadata))]
    public void Value_objects_of_the_entry_are_immutable(Type type)
    {
        Assert.All(type.GetProperties(DeclaredInstance), property => Assert.False(property.CanWrite, property.Name));
        Assert.All(type.GetFields(DeclaredInstance), field => Assert.True(field.IsInitOnly, field.Name));
        Assert.Empty(type.GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }

    [Fact]
    public void Entries_are_compared_by_identity()
    {
        var first = Create();
        var second = Create();

        Assert.NotEqual(first, second);
        Assert.Equal(first, first);
    }

    [Fact]
    public void Create_never_stores_a_secret_hidden_under_an_innocuous_key()
    {
        var metadata = AuditMetadata.Create(
        [
            new("header", "Bearer abcdef123456"),
            new("note", "mot de passe: 1234"),
            new("payload", "{\"password\":\"hunter2\"}"),
            new("pwd", "hunter2"),
        ]);

        var entry = Create(metadata: metadata);

        Assert.All(entry.Metadata.Values.Values, value => Assert.Equal(AuditMetadata.RedactedMarker, value));
    }

    // --- Instant de l'action et instant d'enregistrement

    [Fact]
    public void An_entry_written_after_the_fact_keeps_the_instant_of_the_action()
    {
        var happened = _clock.UtcNow.AddMinutes(-3);

        var entry = AuditEntry.Create(null, null, Action, Target, Correlation, null, _clock, happened);

        Assert.Equal(happened, entry.OccurredAtUtc);
        Assert.Equal(_clock.UtcNow, entry.RecordedAtUtc);
    }

    [Fact]
    public void An_action_dated_in_the_future_is_brought_back_to_the_recording_instant()
    {
        var entry = AuditEntry.Create(null, null, Action, Target, Correlation, null, _clock, _clock.UtcNow.AddHours(1));

        Assert.Equal(_clock.UtcNow, entry.OccurredAtUtc);
    }

    [Fact]
    public void Without_an_action_instant_both_instants_are_now()
    {
        var entry = Create();

        Assert.Equal((_clock.UtcNow, _clock.UtcNow), (entry.OccurredAtUtc, entry.RecordedAtUtc));
    }
}
