using Identity.Domain.Users;

namespace Identity.Domain.Users;

public sealed class CredentialValueObjectsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PasswordHash_rejects_an_empty_value(string? value) =>
        DomainAssert.Throws(UserErrors.PasswordHashEmpty, () => PasswordHash.Create(value));

    [Fact]
    public void PasswordHash_rejects_whitespace_inside_the_value() =>
        DomainAssert.Throws(UserErrors.PasswordHashInvalid, () => PasswordHash.Create("abc def"));

    [Fact]
    public void PasswordHash_accepts_the_maximum_length_and_rejects_one_more()
    {
        Assert.Equal(PasswordHash.MaxLength, PasswordHash.Create(new string('a', PasswordHash.MaxLength)).Value.Length);
        DomainAssert.Throws(
            UserErrors.PasswordHashInvalid,
            () => PasswordHash.Create(new string('a', PasswordHash.MaxLength + 1)));
    }

    [Fact]
    public void PasswordHash_is_compared_by_value_and_never_printed()
    {
        var hash = PasswordHash.Create("$argon2id$v=19$m=65536$salt$hash");

        Assert.Equal(PasswordHash.Create("$argon2id$v=19$m=65536$salt$hash"), hash);
        Assert.NotEqual(PasswordHash.Create("$argon2id$v=19$m=65536$salt$other"), hash);
        Assert.DoesNotContain("argon2id", hash.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void TokenHash_rejects_an_empty_value(string? value) =>
        DomainAssert.Throws(UserErrors.TokenHashEmpty, () => TokenHash.Create(value));

    [Fact]
    public void TokenHash_rejects_whitespace_inside_the_value() =>
        DomainAssert.Throws(UserErrors.TokenHashInvalid, () => TokenHash.Create("abc\tdef"));

    [Fact]
    public void TokenHash_accepts_the_maximum_length_and_rejects_one_more()
    {
        Assert.Equal(TokenHash.MaxLength, TokenHash.Create(new string('a', TokenHash.MaxLength)).Value.Length);
        DomainAssert.Throws(
            UserErrors.TokenHashInvalid,
            () => TokenHash.Create(new string('a', TokenHash.MaxLength + 1)));
    }

    [Fact]
    public void TokenHash_is_compared_by_value_and_never_printed()
    {
        var hash = TokenHash.Create("9f86d081884c7d65");

        Assert.Equal(TokenHash.Create("9f86d081884c7d65"), hash);
        Assert.NotEqual(TokenHash.Create("0000000000000000"), hash);
        Assert.DoesNotContain("9f86", hash.ToString());
    }

    [Fact]
    public void SessionLifetime_default_is_thirty_days() =>
        Assert.Equal(TimeSpan.FromDays(30), SessionLifetime.Default.Duration);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SessionLifetime_rejects_a_non_positive_duration(int seconds) =>
        DomainAssert.Throws(UserErrors.SessionLifetimeInvalid, () => SessionLifetime.Create(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void SessionLifetime_accepts_the_maximum_and_rejects_beyond()
    {
        Assert.Equal(SessionLifetime.Maximum, SessionLifetime.Create(SessionLifetime.Maximum).Duration);
        DomainAssert.Throws(
            UserErrors.SessionLifetimeInvalid,
            () => SessionLifetime.Create(SessionLifetime.Maximum + TimeSpan.FromTicks(1)));
    }

    [Fact]
    public void SessionLifetime_is_compared_by_value() =>
        Assert.Equal(SessionLifetime.Create(TimeSpan.FromHours(8)), SessionLifetime.Create(TimeSpan.FromHours(8)));

    [Fact]
    public void SessionId_rejects_an_empty_guid() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => SessionId.From(Guid.Empty));

    [Fact]
    public void SessionId_round_trips_a_guid()
    {
        var id = SessionId.New();

        Assert.Equal(id, SessionId.From(id.Value));
        Assert.NotEqual(id, SessionId.New());
    }
}
