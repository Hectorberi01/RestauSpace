using Audit.Domain;

namespace Audit.Domain;

public class AuditValueObjectsTests
{
    [Fact]
    public void AuditId_from_rejects_an_empty_guid() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => AuditId.From(Guid.Empty));

    [Fact]
    public void AuditId_new_generates_distinct_ids() => Assert.NotEqual(AuditId.New(), AuditId.New());

    [Fact]
    public void ActorId_for_user_keeps_the_user_identifier()
    {
        var userId = UserId.New();

        var actor = ActorId.ForUser(userId);

        Assert.Equal(ActorType.User, actor.Type);
        Assert.Equal(userId.Value.ToString(), actor.Value);
    }

    [Fact]
    public void ActorId_for_user_rejects_a_default_user_id() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => ActorId.ForUser(default));

    [Fact]
    public void ActorId_for_system_allows_a_non_human_actor()
    {
        var actor = ActorId.ForSystem("  billing-renewal-job ");

        Assert.Equal(ActorType.System, actor.Type);
        Assert.Equal("billing-renewal-job", actor.Value);
        Assert.Equal("System:billing-renewal-job", actor.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ActorId_rejects_a_missing_value(string? value) =>
        DomainAssert.Throws(AuditErrors.ActorValueInvalid, () => ActorId.Create(ActorType.ApiCredential, value));

    [Fact]
    public void ActorId_rejects_a_value_over_the_limit()
    {
        Assert.Equal(ActorId.MaxValueLength, ActorId.Create(ActorType.System, new string('a', ActorId.MaxValueLength)).Value.Length);
        DomainAssert.Throws(
            AuditErrors.ActorValueInvalid,
            () => ActorId.Create(ActorType.System, new string('a', ActorId.MaxValueLength + 1)));
    }

    [Fact]
    public void ActorId_rejects_an_unknown_type() =>
        DomainAssert.Throws(AuditErrors.ActorTypeInvalid, () => ActorId.Create((ActorType)99, "x"));

    [Fact]
    public void ActorId_equality_is_by_type_and_value()
    {
        Assert.Equal(ActorId.Create(ActorType.System, "job"), ActorId.Create(ActorType.System, "job"));
        Assert.NotEqual(ActorId.Create(ActorType.System, "job"), ActorId.Create(ActorType.ApiCredential, "job"));
    }

    [Theory]
    [InlineData("orders.order.cancelled", "orders.order.cancelled")]
    [InlineData("  Staff.Role_Assigned ", "staff.role_assigned")]
    public void AuditAction_is_normalized(string input, string expected) =>
        Assert.Equal(expected, AuditAction.Create(input).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cancelled")]
    [InlineData("orders..cancelled")]
    [InlineData("orders.order cancelled")]
    [InlineData("1orders.cancelled")]
    [InlineData(".orders.cancelled")]
    public void AuditAction_rejects_an_invalid_code(string? value) =>
        DomainAssert.Throws(AuditErrors.ActionInvalid, () => AuditAction.Create(value));

    [Fact]
    public void AuditAction_rejects_a_code_over_the_limit() =>
        DomainAssert.Throws(AuditErrors.ActionInvalid, () => AuditAction.Create("a." + new string('b', AuditAction.MaxLength)));

    [Fact]
    public void AuditTarget_keeps_type_and_id()
    {
        var target = AuditTarget.Create(" Order ", " 42 ");

        Assert.Equal("order", target.Type);
        Assert.Equal("42", target.Id);
        Assert.Equal(AuditTarget.Create("Order", "42"), target);
    }

    [Theory]
    [InlineData("Order")]
    [InlineData("ORDER")]
    [InlineData(" order ")]
    public void AuditTarget_type_is_lower_cased_so_that_casing_variants_are_one_target(string type) =>
        Assert.Equal(AuditTarget.Create("order", "42"), AuditTarget.Create(type, "42"));

    [Fact]
    public void AuditTarget_id_keeps_its_casing()
    {
        Assert.Equal("AbC-42", AuditTarget.Create("order", "AbC-42").Id);
        Assert.NotEqual(AuditTarget.Create("order", "abc"), AuditTarget.Create("order", "ABC"));
    }

    [Theory]
    [InlineData("42\n43")]
    [InlineData("42\r\nactor=admin")]
    [InlineData("42\t43")]
    [InlineData("42\0")]
    [InlineData("42\u001B[31m")]
    [InlineData("42\u0085x")]
    [InlineData("42\u2028x")]
    [InlineData("42\u2029x")]
    public void AuditTarget_rejects_control_characters_in_its_id(string id) =>
        DomainAssert.Throws(AuditErrors.TargetIdInvalid, () => AuditTarget.Create("order", id));

    [Theory]
    [InlineData("or\nder")]
    [InlineData("order\u0000x")]
    [InlineData("order\u2028x")]
    public void AuditTarget_rejects_control_characters_in_its_type(string type) =>
        DomainAssert.Throws(AuditErrors.TargetTypeInvalid, () => AuditTarget.Create(type, "42"));

    [Theory]
    [InlineData("nightly\njob")]
    [InlineData("job\r\nactor=admin")]
    [InlineData("job\tx")]
    [InlineData("job\0x")]
    [InlineData("job\u2028x")]
    public void ActorId_rejects_control_characters(string value)
    {
        DomainAssert.Throws(AuditErrors.ActorValueInvalid, () => ActorId.Create(ActorType.System, value));
        DomainAssert.Throws(AuditErrors.ActorValueInvalid, () => ActorId.ForSystem(value));
        DomainAssert.Throws(AuditErrors.ActorValueInvalid, () => ActorId.Create(ActorType.ApiCredential, value));
    }

    [Fact]
    public void ActorId_created_for_a_user_is_the_same_as_ForUser()
    {
        var userId = UserId.New();

        Assert.Equal(ActorId.ForUser(userId), ActorId.Create(ActorType.User, userId.Value.ToString()));
        Assert.Equal(ActorId.ForUser(userId), ActorId.Create(ActorType.User, $" {userId.Value.ToString("N").ToUpperInvariant()} "));
    }

    [Theory]
    [InlineData("alice")]
    [InlineData("42")]
    [InlineData("not-a-guid")]
    public void ActorId_created_for_a_user_rejects_anything_but_a_guid(string value) =>
        DomainAssert.Throws(AuditErrors.ActorValueInvalid, () => ActorId.Create(ActorType.User, value));

    [Fact]
    public void ActorId_created_for_a_user_rejects_an_empty_guid() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => ActorId.Create(ActorType.User, Guid.Empty.ToString()));

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("Order Item")]
    [InlineData("1Order")]
    [InlineData("Order/Item")]
    public void AuditTarget_rejects_an_invalid_type(string? type) =>
        DomainAssert.Throws(AuditErrors.TargetTypeInvalid, () => AuditTarget.Create(type, "42"));

    [Fact]
    public void AuditTarget_rejects_a_type_over_the_limit() =>
        DomainAssert.Throws(
            AuditErrors.TargetTypeInvalid,
            () => AuditTarget.Create(new string('A', AuditTarget.MaxTypeLength + 1), "42"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AuditTarget_rejects_a_missing_id(string? id) =>
        DomainAssert.Throws(AuditErrors.TargetIdInvalid, () => AuditTarget.Create("Order", id));

    [Fact]
    public void AuditTarget_rejects_an_id_over_the_limit() =>
        DomainAssert.Throws(
            AuditErrors.TargetIdInvalid,
            () => AuditTarget.Create("Order", new string('1', AuditTarget.MaxIdLength + 1)));

    [Fact]
    public void CorrelationId_is_trimmed() => Assert.Equal("req-1", CorrelationId.Create(" req-1 ").Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("req 1")]
    [InlineData("req\n1")]
    public void CorrelationId_rejects_an_invalid_value(string? value) =>
        DomainAssert.Throws(AuditErrors.CorrelationIdInvalid, () => CorrelationId.Create(value));

    [Fact]
    public void CorrelationId_rejects_a_value_over_the_limit() =>
        DomainAssert.Throws(
            AuditErrors.CorrelationIdInvalid,
            () => CorrelationId.Create(new string('c', CorrelationId.MaxLength + 1)));
}
