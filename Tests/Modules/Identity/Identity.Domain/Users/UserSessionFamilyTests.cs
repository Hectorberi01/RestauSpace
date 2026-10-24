namespace Identity.Domain.Users;

/// <summary>Familles de sessions : l'ancre (premier jeton) reste connue, et la famille a une durée maximale.</summary>
public sealed class UserSessionFamilyTests
{
    private static readonly SessionLifetime Family = SessionLifetime.Create(TimeSpan.FromDays(90));
    private readonly FakeClock _clock = new();

    private User ActiveUser()
    {
        var user = User.Register(Email.Create("chef@example.com"), PasswordHash.Create("hash-1"), isEmailUnique: true, _clock);
        user.VerifyEmail(_clock);
        user.ClearDomainEvents();
        return user;
    }

    private static TokenHash T(string value) => TokenHash.Create(value);

    private SessionRotationResult Rotate(User user, string from, string to) =>
        user.RotateSession(T(from), T(to), SessionLifetime.Default, _clock, T("t1"), Family);

    [Fact]
    public void Rotation_keeps_the_anchor_until_the_end_of_the_family_and_drops_the_middle_generations()
    {
        var user = ActiveUser();
        var anchor = user.StartSession(T("t1"), SessionLifetime.Default, _clock);
        var second = Rotate(user, "t1", "t2").NewSession!;
        var third = Rotate(user, "t2", "t3").NewSession!;

        var fourth = Rotate(user, "t3", "t4").NewSession!;

        Assert.Equal(new[] { anchor.Id, third.Id, fourth.Id }, user.Sessions.Select(session => session.Id).ToArray());
        Assert.DoesNotContain(user.Sessions, session => session.Id == second.Id);
        Assert.Equal(anchor.CreatedAtUtc.Add(Family.Duration), anchor.ExpiresAtUtc);
        Assert.False(anchor.IsUsableAt(_clock.UtcNow));
    }

    [Fact]
    public void Reuse_of_a_dropped_generation_is_recognised_by_its_anchor_and_revokes_every_session()
    {
        var user = ActiveUser();
        var anchor = user.StartSession(T("t1"), SessionLifetime.Default, _clock);
        var otherDevice = user.StartSession(T("other"), SessionLifetime.Default, _clock);
        Rotate(user, "t1", "t2");
        Rotate(user, "t2", "t3");
        var current = Rotate(user, "t3", "t4").NewSession!;
        user.ClearDomainEvents();

        Assert.True(user.RevokeAfterFamilyTokenReuse(T("t1"), _clock));

        Assert.Empty(user.ActiveSessionsAt(_clock.UtcNow));
        Assert.Equal(SessionRevocationReason.ReplayDetected, current.RevocationReason);
        Assert.Equal(SessionRevocationReason.ReplayDetected, otherDevice.RevocationReason);
        var detected = DomainAssert.Raised<RefreshTokenReplayDetected>(user);
        Assert.Equal(anchor.Id, detected.ReplayedSessionId);
        Assert.Equal(2, detected.RevokedSessionCount);
    }

    [Fact]
    public void Presenting_the_anchor_itself_after_several_rotations_is_a_replay()
    {
        var user = ActiveUser();
        user.StartSession(T("t1"), SessionLifetime.Default, _clock);
        for (var i = 1; i < 10; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(15));
            Rotate(user, $"t{i}", $"t{i + 1}");
        }

        var replay = Rotate(user, "t1", "stolen");

        Assert.True(replay.ReplayDetected);
        Assert.Empty(user.ActiveSessionsAt(_clock.UtcNow));
    }

    [Fact]
    public void An_unknown_anchor_or_a_finished_family_revokes_nothing()
    {
        var user = ActiveUser();
        user.StartSession(T("t1"), SessionLifetime.Default, _clock);
        var live = user.StartSession(T("live"), SessionLifetime.Create(TimeSpan.FromDays(365)), _clock);
        Rotate(user, "t1", "t2");

        Assert.False(user.RevokeAfterFamilyTokenReuse(T("unknown"), _clock));
        Assert.False(user.RevokeAfterFamilyTokenReuse(T("live"), _clock));
        _clock.Advance(Family.Duration);
        Assert.False(user.RevokeAfterFamilyTokenReuse(T("t1"), _clock));
        Assert.True(live.IsUsableAt(_clock.UtcNow));
    }

    [Fact]
    public void Each_rotation_is_capped_at_the_end_of_the_family()
    {
        var user = ActiveUser();
        var anchor = user.StartSession(T("t1"), SessionLifetime.Default, _clock);
        var end = anchor.CreatedAtUtc.Add(Family.Duration);

        _clock.Advance(TimeSpan.FromDays(29));
        Assert.Equal(_clock.UtcNow.AddDays(30), Rotate(user, "t1", "t2").NewSession!.ExpiresAtUtc);
        _clock.Advance(TimeSpan.FromDays(29));
        Rotate(user, "t2", "t3");
        _clock.Advance(TimeSpan.FromDays(29));

        Assert.Equal(end, Rotate(user, "t3", "t4").NewSession!.ExpiresAtUtc);
    }

    [Fact]
    public void A_session_that_outlives_its_family_cannot_be_renewed()
    {
        var user = ActiveUser();
        user.StartSession(T("t1"), SessionLifetime.Default, _clock);
        _clock.Advance(TimeSpan.FromDays(2));

        DomainAssert.Throws(
            UserErrors.SessionFamilyExpired,
            () => user.RotateSession(T("t1"), T("t2"), SessionLifetime.Default, _clock, T("t1"), SessionLifetime.Create(TimeSpan.FromDays(1))));
    }

    [Fact]
    public void Purge_keeps_the_anchor_until_the_end_of_the_family()
    {
        var user = ActiveUser();
        var anchor = user.StartSession(T("t1"), SessionLifetime.Create(TimeSpan.FromHours(1)), _clock);
        Rotate(user, "t1", "t2");
        Rotate(user, "t2", "t3");
        user.RevokeAllSessions(_clock);

        _clock.Advance(TimeSpan.FromDays(60));
        user.PurgeExpiredSessions(_clock);
        Assert.Contains(anchor, user.Sessions);

        _clock.Advance(TimeSpan.FromDays(30));
        user.PurgeExpiredSessions(_clock);
        Assert.DoesNotContain(anchor, user.Sessions);
    }
}
