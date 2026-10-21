using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Identity.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure;

public sealed class SecurityTests(IdentityDatabase database) : IClassFixture<IdentityDatabase>
{
    [Fact]
    public void A_password_hash_verifies_its_password_only_and_is_salted()
    {
        var hasher = database.Get<IPasswordHasher>();

        var hash = hasher.Hash("correct horse battery");
        var again = hasher.Hash("correct horse battery");

        Assert.True(hasher.Verify("correct horse battery", hash));
        Assert.False(hasher.Verify("correct horse battery ", hash));
        Assert.False(hasher.Verify("", hash));
        Assert.NotEqual(hash, again);
        Assert.DoesNotContain("correct", hash.Value);
        Assert.False(hasher.Verify("anything", PasswordHash.Create("not-one-of-our-hashes")));
    }

    [Theory]
    [InlineData("pässwörd with spaces")]
    [InlineData("密码密码密码密码")]
    [InlineData("a\0b\tc\nd-----")]
    public void Any_password_gives_a_hash_the_domain_accepts(string password)
    {
        var hasher = database.Get<IPasswordHasher>();

        Assert.True(hasher.Verify(password, hasher.Hash(password)));
        Assert.True(hasher.Verify(new string('x', 128), hasher.Hash(new string('x', 128))));
    }

    [Fact]
    public void Refresh_tokens_are_unguessable_and_their_hash_is_deterministic()
    {
        var tokens = database.Get<IRefreshTokens>();

        var first = tokens.Generate();
        var second = tokens.Generate();

        Assert.NotEqual(first.Value, second.Value);
        Assert.True(first.Value.Length >= 43);
        Assert.Equal(first.Hash, tokens.Hash(first.Value));
        Assert.NotEqual(first.Value, first.Hash.Value);
        Assert.NotEqual(first.Hash, tokens.Hash(first.Value + " "));
    }

    [Theory]
    [InlineData("with spaces and \t tabs")]
    [InlineData("\0")]
    [InlineData("é")]
    public void Whatever_a_client_presents_can_be_hashed(string presented)
    {
        var tokens = database.Get<IRefreshTokens>();

        Assert.Equal(64, tokens.Hash(presented).Value.Length);
        Assert.Equal(64, tokens.Hash(new string('a', 100_000)).Value.Length);
    }

    [Fact]
    public async Task An_access_token_is_a_signed_jwt_naming_the_user_and_the_session()
    {
        var user = User.Register(Email.Create("jwt@example.com"), PasswordHash.Create("hash"), isEmailUnique: true, database.Clock);
        var sessionId = SessionId.New();

        var token = database.Get<IAccessTokenIssuer>().Issue(user, sessionId);

        Assert.Equal(database.Clock.UtcNow.AddMinutes(15), token.ExpiresAtUtc);
        var result = await Validate(token.Value, IdentityDatabase.SigningKey);
        Assert.True(result.IsValid, result.Exception?.Message);
        var jwt = (JsonWebToken)result.SecurityToken;
        Assert.Equal(user.Id.Value.ToString(), jwt.Subject);
        Assert.Equal(sessionId.Value.ToString(), jwt.GetClaim("sid").Value);
        Assert.Equal("jwt@example.com", jwt.GetClaim("email").Value);
        Assert.Equal(token.ExpiresAtUtc.UtcDateTime, jwt.ValidTo);
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type is "role" or "roles");

        var otherKey = Convert.ToBase64String(new byte[32]);
        Assert.False((await Validate(token.Value, otherKey)).IsValid);
    }

    [Theory]
    [InlineData("issuer", "audience", "not base64 !")]
    [InlineData("issuer", "audience", "c2hvcnQ=")]
    [InlineData("", "audience", null)]
    [InlineData("issuer", "", null)]
    public void A_weak_or_incomplete_access_token_configuration_is_refused_at_registration(
        string issuer, string audience, string? signingKey)
    {
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddIdentityInfrastructure(
            "Host=localhost", options =>
            {
                options.Issuer = issuer;
                options.Audience = audience;
                options.SigningKey = signingKey ?? IdentityDatabase.SigningKey;
            }));
    }

    // L'horloge factice vit dans le passé : la durée de vie est donc vérifiée sur les claims.
    private static Task<TokenValidationResult> Validate(string token, string signingKey) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = IdentityDatabase.Issuer,
            ValidAudience = IdentityDatabase.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(signingKey)),
            ValidateLifetime = false,
        });

    [Fact]
    public void An_authenticator_secret_is_stored_encrypted_and_a_challenge_cannot_be_forged()
    {
        var secrets = database.Get<ITwoFactorSecrets>();
        var challenges = database.Get<ITwoFactorChallenges>();
        var secret = "12345678901234567890"u8.ToArray();
        var user = UserId.New();

        var stored = secrets.Protect(secret);
        var again = secrets.Protect(secret);
        var token = challenges.Issue(user, database.Clock.UtcNow.AddMinutes(5));
        var tampered = token[..^2] + (token[^2] == 'A' ? "BA" : "AA");

        Assert.Equal(secret, secrets.Unprotect(stored));
        Assert.NotEqual(stored, again);
        Assert.DoesNotContain("MTIzNDU2Nzg5", stored, StringComparison.Ordinal);
        Assert.Throws<DomainException>(() => secrets.Unprotect(stored[..^4] + "AAAA"));
        Assert.Equal(user, challenges.Read(token, database.Clock.UtcNow));
        Assert.Null(challenges.Read(tampered, database.Clock.UtcNow));
        Assert.Null(challenges.Read(token, database.Clock.UtcNow.AddMinutes(6)));
    }
}
