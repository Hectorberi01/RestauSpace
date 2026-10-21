using Identity.Domain.Users;

namespace Identity.Domain.Users;

public sealed class UserTests
{
    private readonly FakeClock _clock = new();

    private static PasswordHash Password(string value = "hash-1") => PasswordHash.Create(value);

    private User PendingUser() =>
        User.Register(Email.Create("chef@example.com"), Password(), isEmailUnique: true, _clock);

    private User ActiveUser()
    {
        var user = PendingUser();
        user.VerifyEmail(_clock);
        user.ClearDomainEvents();
        return user;
    }

    private User InvitedUser() =>
        User.RegisterInvited(Email.Create("waiter@example.com"), isEmailUnique: true, _clock);

    private User LockedUser()
    {
        var user = ActiveUser();
        user.Lock(_clock);
        user.ClearDomainEvents();
        return user;
    }

    private User DisabledUser()
    {
        var user = ActiveUser();
        user.Disable(_clock);
        user.ClearDomainEvents();
        return user;
    }

    // ---- Register ----

    [Fact]
    public void Register_creates_a_pending_user_with_a_normalized_email()
    {
        var user = User.Register(Email.Create("  Chef@Example.COM "), Password(), isEmailUnique: true, _clock);

        Assert.Equal("chef@example.com", user.Email.Value);
        Assert.Equal(Password(), user.PasswordHash);
        Assert.Equal(UserStatus.Pending, user.Status);
        Assert.Equal(_clock.UtcNow, user.CreatedAtUtc);
        Assert.False(user.IsEmailVerified);
        Assert.Empty(user.Sessions);
        Assert.NotEqual(Guid.Empty, user.Id.Value);
    }

    [Fact]
    public void Register_raises_UserRegistered()
    {
        var user = PendingUser();

        var registered = DomainAssert.Raised<UserRegistered>(user);
        Assert.Equal(user.Id, registered.UserId);
        Assert.Equal(user.Email, registered.Email);
        Assert.Equal(_clock.UtcNow, registered.OccurredAtUtc);
    }

    [Fact]
    public void Register_rejects_an_email_already_used() =>
        DomainAssert.Throws(
            UserErrors.EmailAlreadyUsed,
            () => User.Register(Email.Create("chef@example.com"), Password(), isEmailUnique: false, _clock));

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("chef@")]
    public void Register_cannot_be_reached_with_an_invalid_email(string email)
    {
        var exception = Assert.Throws<DomainException>(
            () => User.Register(Email.Create(email), Password(), isEmailUnique: true, _clock));

        Assert.StartsWith("common.email.", exception.Code);
    }

    [Fact]
    public void Register_cannot_be_reached_with_an_empty_password_hash() =>
        DomainAssert.Throws(
            UserErrors.PasswordHashEmpty,
            () => User.Register(Email.Create("chef@example.com"), PasswordHash.Create(""), isEmailUnique: true, _clock));

    [Fact]
    public void Register_rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => User.Register(null!, Password(), true, _clock));
        Assert.Throws<ArgumentNullException>(() => User.Register(Email.Create("a@b.co"), null!, true, _clock));
        Assert.Throws<ArgumentNullException>(() => User.Register(Email.Create("a@b.co"), Password(), true, null!));
    }

    // ---- RegisterInvited / AcceptInvitation ----

    [Fact]
    public void RegisterInvited_creates_a_pending_user_without_password()
    {
        var user = User.RegisterInvited(Email.Create(" Waiter@Example.com "), isEmailUnique: true, _clock);

        Assert.Equal("waiter@example.com", user.Email.Value);
        Assert.Null(user.PasswordHash);
        Assert.False(user.HasPassword);
        Assert.Equal(UserStatus.Pending, user.Status);
        Assert.Equal(_clock.UtcNow, user.CreatedAtUtc);
        Assert.False(user.IsEmailVerified);
        Assert.False(user.CanStartSession);
        Assert.Empty(user.Sessions);
    }

    [Fact]
    public void RegisterInvited_raises_UserInvited_only()
    {
        var user = InvitedUser();

        var invited = DomainAssert.Raised<UserInvited>(user);
        Assert.Equal(user.Id, invited.UserId);
        Assert.Equal(user.Email, invited.Email);
        Assert.Equal(_clock.UtcNow, invited.OccurredAtUtc);
        Assert.Single(user.DomainEvents);
    }

    [Fact]
    public void RegisterInvited_rejects_an_email_already_used() =>
        DomainAssert.Throws(
            UserErrors.EmailAlreadyUsed,
            () => User.RegisterInvited(Email.Create("waiter@example.com"), isEmailUnique: false, _clock));

    [Fact]
    public void RegisterInvited_rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => User.RegisterInvited(null!, true, _clock));
        Assert.Throws<ArgumentNullException>(() => User.RegisterInvited(Email.Create("a@b.co"), true, null!));
    }

    [Fact]
    public void Register_gives_the_user_a_password()
    {
        var user = PendingUser();

        Assert.True(user.HasPassword);
        DomainAssert.NotRaised<UserInvited>(user);
    }

    [Fact]
    public void AcceptInvitation_sets_the_password_verifies_the_email_and_activates_the_user()
    {
        var user = InvitedUser();
        user.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromDays(2));

        user.AcceptInvitation(Password(), _clock);

        Assert.Equal(Password(), user.PasswordHash);
        Assert.True(user.HasPassword);
        Assert.Equal(_clock.UtcNow, user.EmailVerifiedAtUtc);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(user.CanStartSession);
        var accepted = DomainAssert.Raised<UserInvitationAccepted>(user);
        Assert.Equal(user.Id, accepted.UserId);
        Assert.Equal(user.Email, accepted.Email);
        Assert.Equal(_clock.UtcNow, accepted.OccurredAtUtc);
        Assert.Equal(user.Email, DomainAssert.Raised<EmailVerified>(user).Email);
        DomainAssert.NotRaised<PasswordChanged>(user);
        Assert.Equal(2, user.DomainEvents.Count);
    }

    [Fact]
    public void AcceptInvitation_lets_the_user_start_a_session()
    {
        var user = InvitedUser();
        user.AcceptInvitation(Password(), _clock);

        var session = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);

        Assert.True(session.IsUsableAt(_clock.UtcNow));
    }

    [Fact]
    public void AcceptInvitation_rejects_a_user_that_already_has_a_password()
    {
        foreach (var user in new[] { PendingUser(), ActiveUser() })
        {
            user.ClearDomainEvents();

            DomainAssert.Throws(UserErrors.NoPendingInvitation, () => user.AcceptInvitation(Password("hash-2"), _clock));
            Assert.Equal(Password(), user.PasswordHash);
            DomainAssert.NoEvents(user);
        }
    }

    [Fact]
    public void AcceptInvitation_cannot_be_done_twice()
    {
        var user = InvitedUser();
        user.AcceptInvitation(Password(), _clock);
        user.ClearDomainEvents();

        DomainAssert.Throws(UserErrors.NoPendingInvitation, () => user.AcceptInvitation(Password("hash-2"), _clock));
        Assert.Equal(Password(), user.PasswordHash);
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void AcceptInvitation_rejects_a_disabled_user()
    {
        var user = InvitedUser();
        user.Disable(_clock);
        user.ClearDomainEvents();

        DomainAssert.Throws(UserErrors.Disabled, () => user.AcceptInvitation(Password(), _clock));
        Assert.False(user.HasPassword);
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void AcceptInvitation_on_a_locked_user_keeps_it_locked_until_unlocked()
    {
        var user = InvitedUser();
        user.Lock(_clock);

        user.AcceptInvitation(Password(), _clock);

        Assert.Equal(UserStatus.Locked, user.Status);
        Assert.True(user.HasPassword);
        Assert.True(user.IsEmailVerified);
        Assert.False(user.CanStartSession);

        user.Unlock(_clock);

        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void AcceptInvitation_rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => InvitedUser().AcceptInvitation(null!, _clock));
        Assert.Throws<ArgumentNullException>(() => InvitedUser().AcceptInvitation(Password(), null!));
    }

    [Fact]
    public void Invited_user_cannot_start_a_session_before_accepting()
    {
        var user = InvitedUser();
        user.ClearDomainEvents();

        DomainAssert.Throws(
            UserErrors.PasswordNotSet,
            () => user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock));
        Assert.Empty(user.Sessions);
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void Invited_user_cannot_verify_its_email_without_setting_a_password()
    {
        var user = InvitedUser();
        user.ClearDomainEvents();

        DomainAssert.Throws(UserErrors.PasswordNotSet, () => user.VerifyEmail(_clock));
        Assert.Equal(UserStatus.Pending, user.Status);
        Assert.False(user.IsEmailVerified);
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void Invited_user_cannot_change_a_password_it_does_not_have()
    {
        var user = InvitedUser();
        user.ClearDomainEvents();

        DomainAssert.Throws(UserErrors.PasswordNotSet, () => user.ChangePassword(Password(), _clock));
        Assert.Null(user.PasswordHash);
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void Invited_user_can_have_its_email_corrected_before_accepting()
    {
        var user = InvitedUser();

        user.ChangeEmail(Email.Create("waiter-fixed@example.com"), isEmailUnique: true, _clock);
        user.AcceptInvitation(Password(), _clock);

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal("waiter-fixed@example.com", DomainAssert.Raised<EmailVerified>(user).Email.Value);
    }

    [Fact]
    public void Invited_user_unlocked_before_accepting_is_pending_again()
    {
        var user = InvitedUser();
        user.Lock(_clock);

        user.Unlock(_clock);

        Assert.Equal(UserStatus.Pending, user.Status);
        Assert.False(user.HasPassword);
    }

    // ---- VerifyEmail ----

    [Fact]
    public void VerifyEmail_activates_a_pending_user_and_raises_EmailVerified()
    {
        var user = PendingUser();
        _clock.Advance(TimeSpan.FromMinutes(5));

        user.VerifyEmail(_clock);

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(_clock.UtcNow, user.EmailVerifiedAtUtc);
        var verified = DomainAssert.Raised<EmailVerified>(user);
        Assert.Equal(user.Id, verified.UserId);
        Assert.Equal(user.Email, verified.Email);
    }

    [Fact]
    public void VerifyEmail_rejects_an_already_verified_email()
    {
        var user = ActiveUser();

        DomainAssert.Throws(UserErrors.EmailAlreadyVerified, () => user.VerifyEmail(_clock));
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void VerifyEmail_on_a_locked_unverified_user_keeps_it_locked()
    {
        var user = PendingUser();
        user.Lock(_clock);

        user.VerifyEmail(_clock);

        Assert.Equal(UserStatus.Locked, user.Status);
        Assert.True(user.IsEmailVerified);
    }

    // ---- ChangeEmail ----

    [Fact]
    public void ChangeEmail_replaces_the_email_requires_a_new_verification_and_keeps_the_user_active()
    {
        var user = ActiveUser();
        var previous = user.Email;

        user.ChangeEmail(Email.Create("new@example.com"), isEmailUnique: true, _clock);

        Assert.Equal("new@example.com", user.Email.Value);
        Assert.False(user.IsEmailVerified);
        Assert.Equal(UserStatus.Active, user.Status);
        var changed = DomainAssert.Raised<UserEmailChanged>(user);
        Assert.Equal(previous, changed.PreviousEmail);
        Assert.Equal(user.Email, changed.NewEmail);
    }

    [Fact]
    public void ChangeEmail_then_VerifyEmail_verifies_the_new_address()
    {
        var user = ActiveUser();
        user.ChangeEmail(Email.Create("new@example.com"), isEmailUnique: true, _clock);

        user.VerifyEmail(_clock);

        Assert.True(user.IsEmailVerified);
        Assert.Equal("new@example.com", DomainAssert.Raised<EmailVerified>(user).Email.Value);
    }

    [Fact]
    public void ChangeEmail_is_allowed_for_a_pending_user()
    {
        var user = PendingUser();

        user.ChangeEmail(Email.Create("typo-fixed@example.com"), isEmailUnique: true, _clock);

        Assert.Equal(UserStatus.Pending, user.Status);
        Assert.Equal("typo-fixed@example.com", user.Email.Value);
    }

    [Fact]
    public void ChangeEmail_rejects_an_email_already_used()
    {
        var user = ActiveUser();

        DomainAssert.Throws(
            UserErrors.EmailAlreadyUsed,
            () => user.ChangeEmail(Email.Create("taken@example.com"), isEmailUnique: false, _clock));
        Assert.Equal("chef@example.com", user.Email.Value);
        Assert.True(user.IsEmailVerified);
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void ChangeEmail_with_the_same_email_is_a_silent_no_op()
    {
        var user = ActiveUser();
        var verifiedAt = user.EmailVerifiedAtUtc;
        _clock.Advance(TimeSpan.FromMinutes(1));

        user.ChangeEmail(Email.Create("CHEF@example.com"), isEmailUnique: false, _clock);

        Assert.Equal("chef@example.com", user.Email.Value);
        Assert.Equal(verifiedAt, user.EmailVerifiedAtUtc);
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void ChangeEmail_rejects_a_locked_user() =>
        DomainAssert.Throws(
            UserErrors.Locked,
            () => LockedUser().ChangeEmail(Email.Create("new@example.com"), isEmailUnique: true, _clock));

    // ---- ChangePassword ----

    [Fact]
    public void ChangePassword_replaces_the_hash_and_raises_PasswordChanged()
    {
        var user = ActiveUser();
        _clock.Advance(TimeSpan.FromHours(1));

        user.ChangePassword(Password("hash-2"), _clock);

        Assert.Equal(Password("hash-2"), user.PasswordHash);
        Assert.Equal(_clock.UtcNow, user.PasswordChangedAtUtc);
        Assert.Equal(user.Id, DomainAssert.Raised<PasswordChanged>(user).UserId);
    }

    [Fact]
    public void ChangePassword_revokes_every_session_by_default()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        user.StartSession(TokenHash.Create("t2"), SessionLifetime.Default, _clock);
        user.ClearDomainEvents();

        user.ChangePassword(Password("hash-2"), _clock);

        Assert.Empty(user.ActiveSessionsAt(_clock.UtcNow));
        var revoked = user.DomainEvents.OfType<SessionRevoked>().ToList();
        Assert.Equal(2, revoked.Count);
        Assert.All(revoked, e => Assert.Equal(SessionRevocationReason.PasswordChanged, e.Reason));
    }

    [Fact]
    public void ChangePassword_keeps_the_session_it_was_requested_from()
    {
        var user = ActiveUser();
        var current = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        var other = user.StartSession(TokenHash.Create("t2"), SessionLifetime.Default, _clock);
        user.ClearDomainEvents();

        user.ChangePassword(Password("hash-2"), _clock, sessionToKeep: current.Id);

        Assert.True(current.IsUsableAt(_clock.UtcNow));
        Assert.False(other.IsUsableAt(_clock.UtcNow));
        Assert.Equal(other.Id, DomainAssert.Raised<SessionRevoked>(user).SessionId);
    }

    [Fact]
    public void ChangePassword_rejects_an_unknown_session_to_keep()
    {
        var user = ActiveUser();

        DomainAssert.Throws(
            UserErrors.SessionNotFound,
            () => user.ChangePassword(Password("hash-2"), _clock, sessionToKeep: SessionId.New()));
        Assert.Equal(Password(), user.PasswordHash);
    }

    [Fact]
    public void ChangePassword_with_the_same_hash_is_a_silent_no_op_and_keeps_the_sessions()
    {
        var user = ActiveUser();
        var session = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        user.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromMinutes(1));

        user.ChangePassword(Password(), _clock);

        Assert.Equal(Password(), user.PasswordHash);
        Assert.Null(user.PasswordChangedAtUtc);
        Assert.True(session.IsUsableAt(_clock.UtcNow));
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void ChangePassword_is_allowed_while_locked()
    {
        var user = LockedUser();

        user.ChangePassword(Password("hash-2"), _clock);

        Assert.Equal(Password("hash-2"), user.PasswordHash);
        Assert.Equal(UserStatus.Locked, user.Status);
    }

    // ---- Lock / Unlock ----

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lock_locks_a_pending_or_active_user_and_raises_UserLocked(bool verified)
    {
        var user = verified ? ActiveUser() : PendingUser();

        user.Lock(_clock);

        Assert.Equal(UserStatus.Locked, user.Status);
        Assert.Equal(user.Id, DomainAssert.Raised<UserLocked>(user).UserId);
    }

    [Fact]
    public void Lock_revokes_every_session()
    {
        var user = ActiveUser();
        var session = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);

        user.Lock(_clock);

        Assert.False(session.IsUsableAt(_clock.UtcNow));
        Assert.Equal(SessionRevocationReason.UserLocked, session.RevocationReason);
        Assert.Equal(SessionRevocationReason.UserLocked, DomainAssert.Raised<SessionRevoked>(user).Reason);
    }

    [Fact]
    public void Lock_rejects_an_already_locked_user()
    {
        var user = LockedUser();

        DomainAssert.Throws(UserErrors.AlreadyLocked, () => user.Lock(_clock));
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void Unlock_restores_an_active_user_when_the_email_is_verified()
    {
        var user = LockedUser();

        user.Unlock(_clock);

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal(UserStatus.Active, DomainAssert.Raised<UserUnlocked>(user).RestoredStatus);
    }

    [Fact]
    public void Unlock_restores_a_pending_user_when_the_email_is_not_verified()
    {
        var user = PendingUser();
        user.Lock(_clock);
        Assert.Equal(UserStatus.Pending, user.StatusBeforeLock);

        user.Unlock(_clock);

        Assert.Equal(UserStatus.Pending, user.Status);
        Assert.Null(user.StatusBeforeLock);
    }

    [Fact]
    public void ChangeEmail_then_Lock_then_Unlock_keeps_the_user_active()
    {
        var user = ActiveUser();
        user.ChangeEmail(Email.Create("new@example.com"), isEmailUnique: true, _clock);
        Assert.True(user.CanStartSession);

        user.Lock(_clock);
        user.ClearDomainEvents();
        user.Unlock(_clock);

        // Le statut d'avant le verrouillage est restauré, pas recalculé d'après la nouvelle adresse (non vérifiée).
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.False(user.IsEmailVerified);
        Assert.True(user.CanStartSession);
        Assert.Equal(UserStatus.Active, DomainAssert.Raised<UserUnlocked>(user).RestoredStatus);
        user.StartSession(TokenHash.Create("after-unlock"), SessionLifetime.Default, _clock);
    }

    [Fact]
    public void ChangeEmail_then_Lock_then_Unlock_still_lets_the_new_address_be_verified()
    {
        var user = ActiveUser();
        user.ChangeEmail(Email.Create("new@example.com"), isEmailUnique: true, _clock);
        user.Lock(_clock);
        user.Unlock(_clock);
        user.ClearDomainEvents();

        user.VerifyEmail(_clock);

        Assert.True(user.IsEmailVerified);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Equal("new@example.com", DomainAssert.Raised<EmailVerified>(user).Email.Value);
    }

    [Fact]
    public void ChangeEmail_keeps_the_existing_sessions_usable()
    {
        var user = ActiveUser();
        var session = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);

        user.ChangeEmail(Email.Create("new@example.com"), isEmailUnique: true, _clock);

        Assert.True(session.IsUsableAt(_clock.UtcNow));
        Assert.False(user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock).ReplayDetected);
    }

    [Fact]
    public void A_pending_user_verified_while_locked_is_active_once_unlocked()
    {
        var user = PendingUser();
        user.Lock(_clock);

        user.VerifyEmail(_clock);

        Assert.Equal(UserStatus.Locked, user.Status);
        user.Unlock(_clock);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(user.CanStartSession);
    }

    [Fact]
    public void Lock_can_be_repeated_after_an_unlock_and_each_unlock_restores_the_status_of_its_own_lock()
    {
        var user = PendingUser();
        user.Lock(_clock);
        user.Unlock(_clock);
        user.VerifyEmail(_clock);

        user.Lock(_clock);
        Assert.Equal(UserStatus.Active, user.StatusBeforeLock);
        user.Unlock(_clock);

        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Unlock_rejects_a_user_that_is_not_locked(bool verified)
    {
        var user = verified ? ActiveUser() : PendingUser();

        DomainAssert.Throws(UserErrors.NotLocked, () => user.Unlock(_clock));
        DomainAssert.NotRaised<UserUnlocked>(user);
    }

    [Fact]
    public void Unlock_does_not_bring_revoked_sessions_back()
    {
        var user = ActiveUser();
        var session = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        user.Lock(_clock);

        user.Unlock(_clock);

        Assert.False(session.IsUsableAt(_clock.UtcNow));
        DomainAssert.Throws(
            UserErrors.SessionRevoked,
            () => user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock));
    }

    // ---- Disable (état terminal) ----

    [Fact]
    public void Disable_can_be_reached_from_pending_active_and_locked()
    {
        foreach (var user in new[] { PendingUser(), ActiveUser(), LockedUser() })
        {
            user.Disable(_clock);

            Assert.Equal(UserStatus.Disabled, user.Status);
            Assert.Equal(user.Id, DomainAssert.Raised<UserDisabled>(user).UserId);
        }
    }

    [Fact]
    public void Disable_revokes_every_session()
    {
        var user = ActiveUser();
        var first = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        var second = user.StartSession(TokenHash.Create("t2"), SessionLifetime.Default, _clock);
        user.ClearDomainEvents();

        user.Disable(_clock);

        Assert.All(new[] { first, second }, s => Assert.Equal(SessionRevocationReason.UserDisabled, s.RevocationReason));
        Assert.Equal(2, user.DomainEvents.OfType<SessionRevoked>().Count());
        Assert.Empty(user.ActiveSessionsAt(_clock.UtcNow));
    }

    [Fact]
    public void Disabled_user_cannot_start_a_session()
    {
        var user = DisabledUser();

        Assert.False(user.CanStartSession);
        DomainAssert.Throws(
            UserErrors.Disabled,
            () => user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock));
        Assert.Empty(user.Sessions);
    }

    [Fact]
    public void Disabled_user_cannot_rotate_a_session()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        user.Disable(_clock);

        DomainAssert.Throws(
            UserErrors.Disabled,
            () => user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock));
    }

    [Fact]
    public void Disabled_is_terminal_and_rejects_every_mutation()
    {
        var user = DisabledUser();

        DomainAssert.Throws(UserErrors.Disabled, () => user.Disable(_clock));
        DomainAssert.Throws(UserErrors.Disabled, () => user.Lock(_clock));
        DomainAssert.Throws(UserErrors.Disabled, () => user.Unlock(_clock));
        DomainAssert.Throws(UserErrors.Disabled, () => user.VerifyEmail(_clock));
        DomainAssert.Throws(UserErrors.Disabled, () => user.ChangeEmail(Email.Create("new@example.com"), true, _clock));
        DomainAssert.Throws(UserErrors.Disabled, () => user.ChangePassword(Password("hash-2"), _clock));
        DomainAssert.Throws(UserErrors.Disabled, () => user.ChangeEmail(Email.Create("chef@example.com"), true, _clock));
        DomainAssert.Throws(UserErrors.Disabled, () => user.ChangePassword(Password(), _clock));
        DomainAssert.Throws(UserErrors.Disabled, () => user.AcceptInvitation(Password("hash-2"), _clock));
        Assert.Equal(UserStatus.Disabled, user.Status);
        DomainAssert.NoEvents(user);
    }

    // ---- StartSession ----

    [Fact]
    public void StartSession_opens_a_usable_session_expiring_after_the_given_lifetime()
    {
        var user = ActiveUser();
        var lifetime = SessionLifetime.Create(TimeSpan.FromHours(8));

        var session = user.StartSession(TokenHash.Create("t1"), lifetime, _clock);

        Assert.Same(session, Assert.Single(user.Sessions));
        Assert.Equal(TokenHash.Create("t1"), session.Hash);
        Assert.Equal(_clock.UtcNow, session.CreatedAtUtc);
        Assert.Equal(_clock.UtcNow.AddHours(8), session.ExpiresAtUtc);
        Assert.Null(session.RevokedAtUtc);
        Assert.Null(session.ReplacedBy);
        Assert.True(session.IsUsableAt(_clock.UtcNow));
        var started = DomainAssert.Raised<SessionStarted>(user);
        Assert.Equal(session.Id, started.SessionId);
        Assert.Equal(session.ExpiresAtUtc, started.ExpiresAtUtc);
    }

    [Fact]
    public void StartSession_rejects_a_pending_user()
    {
        var user = PendingUser();

        Assert.False(user.CanStartSession);
        DomainAssert.Throws(
            UserErrors.EmailNotVerified,
            () => user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock));
        DomainAssert.NotRaised<SessionStarted>(user);
    }

    [Fact]
    public void StartSession_rejects_a_locked_user()
    {
        var user = LockedUser();

        Assert.False(user.CanStartSession);
        DomainAssert.Throws(
            UserErrors.Locked,
            () => user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock));
    }

    [Fact]
    public void StartSession_rejects_a_token_hash_already_used_by_the_user()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);

        DomainAssert.Throws(
            UserErrors.TokenHashAlreadyUsed,
            () => user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock));
        Assert.Single(user.Sessions);
    }

    [Fact]
    public void Session_is_usable_until_its_expiry_instant_excluded()
    {
        var user = ActiveUser();
        var session = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Create(TimeSpan.FromHours(1)), _clock);

        Assert.True(session.IsUsableAt(session.ExpiresAtUtc.AddTicks(-1)));
        Assert.False(session.IsUsableAt(session.ExpiresAtUtc));
        Assert.True(session.IsExpiredAt(session.ExpiresAtUtc));
    }

    [Fact]
    public void ActiveSessionsAt_excludes_expired_and_revoked_sessions()
    {
        var user = ActiveUser();
        var shortLived = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Create(TimeSpan.FromHours(1)), _clock);
        var revoked = user.StartSession(TokenHash.Create("t2"), SessionLifetime.Default, _clock);
        var alive = user.StartSession(TokenHash.Create("t3"), SessionLifetime.Default, _clock);
        user.RevokeSession(revoked.Id, _clock);
        _clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(3, user.Sessions.Count);
        Assert.Same(alive, Assert.Single(user.ActiveSessionsAt(_clock.UtcNow)));
        Assert.False(shortLived.IsUsableAt(_clock.UtcNow));
    }

    // ---- RotateSession ----

    [Fact]
    public void RotateSession_revokes_the_presented_session_and_links_it_to_the_new_one()
    {
        var user = ActiveUser();
        var old = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        user.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromDays(1));

        var result = user.RotateSession(
            TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Create(TimeSpan.FromDays(7)), _clock);

        Assert.False(result.ReplayDetected);
        Assert.Null(result.Error);
        var next = result.NewSession!;
        Assert.Equal(_clock.UtcNow, old.RevokedAtUtc);
        Assert.Equal(next.Id, old.ReplacedBy);
        Assert.True(old.WasRotated);
        Assert.Null(old.RevocationReason);
        Assert.False(old.IsUsableAt(_clock.UtcNow));
        Assert.Equal(TokenHash.Create("t2"), next.Hash);
        Assert.Equal(_clock.UtcNow.AddDays(7), next.ExpiresAtUtc);
        Assert.True(next.IsUsableAt(_clock.UtcNow));
        Assert.Equal(2, user.Sessions.Count);
        Assert.Same(next, Assert.Single(user.ActiveSessionsAt(_clock.UtcNow)));
    }

    [Fact]
    public void RotateSession_raises_RefreshTokenRotated_only()
    {
        var user = ActiveUser();
        var old = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        user.ClearDomainEvents();

        var next = user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock).NewSession!;

        var rotated = DomainAssert.Raised<RefreshTokenRotated>(user);
        Assert.Equal(user.Id, rotated.UserId);
        Assert.Equal(old.Id, rotated.RotatedSessionId);
        Assert.Equal(next.Id, rotated.NewSessionId);
        Assert.Equal(next.ExpiresAtUtc, rotated.NewExpiresAtUtc);
        Assert.Single(user.DomainEvents);
    }

    [Fact]
    public void RotateSession_can_be_chained_with_each_new_token()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock);

        var result = user.RotateSession(TokenHash.Create("t2"), TokenHash.Create("t3"), SessionLifetime.Default, _clock);

        Assert.False(result.ReplayDetected);
        Assert.Equal(2, user.Sessions.Count);
        Assert.Same(result.NewSession, Assert.Single(user.ActiveSessionsAt(_clock.UtcNow)));
    }

    [Fact]
    public void RotateSession_drops_the_rotated_session_whose_successor_has_been_rotated()
    {
        var user = ActiveUser();
        var first = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        var otherDevice = user.StartSession(TokenHash.Create("other"), SessionLifetime.Default, _clock);
        var second = user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock).NewSession!;
        Assert.Contains(first, user.Sessions);
        user.ClearDomainEvents();

        var third = user.RotateSession(TokenHash.Create("t2"), TokenHash.Create("t3"), SessionLifetime.Default, _clock).NewSession!;

        Assert.Equal(new[] { otherDevice.Id, second.Id, third.Id }, user.Sessions.Select(s => s.Id).ToArray());
        Assert.Equal(third.Id, second.ReplacedBy);
        Assert.IsType<RefreshTokenRotated>(Assert.Single(user.DomainEvents));
    }

    [Fact]
    public void RotateSession_replay_of_the_latest_rotated_token_is_still_detected_in_a_long_chain()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("t0"), SessionLifetime.Default, _clock);
        for (var i = 0; i < 10; i++)
        {
            user.RotateSession(TokenHash.Create($"t{i}"), TokenHash.Create($"t{i + 1}"), SessionLifetime.Default, _clock);
        }

        var replay = user.RotateSession(TokenHash.Create("t9"), TokenHash.Create("stolen"), SessionLifetime.Default, _clock);

        Assert.True(replay.ReplayDetected);
        Assert.Empty(user.ActiveSessionsAt(_clock.UtcNow));
    }

    [Fact]
    public void RotateSession_token_older_than_the_kept_generation_is_unknown_and_revokes_nothing()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock);
        var current = user.RotateSession(TokenHash.Create("t2"), TokenHash.Create("t3"), SessionLifetime.Default, _clock).NewSession!;
        user.ClearDomainEvents();

        DomainAssert.Throws(
            UserErrors.SessionNotFound,
            () => user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t4"), SessionLifetime.Default, _clock));
        Assert.True(current.IsUsableAt(_clock.UtcNow));
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void RotateSession_every_15_minutes_for_30_days_keeps_the_session_collection_bounded()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("t0"), SessionLifetime.Default, _clock);
        const int rotations = 30 * 24 * 4;

        for (var i = 0; i < rotations; i++)
        {
            _clock.Advance(TimeSpan.FromMinutes(15));
            var result = user.RotateSession(TokenHash.Create($"t{i}"), TokenHash.Create($"t{i + 1}"), SessionLifetime.Default, _clock);

            Assert.False(result.ReplayDetected);
            Assert.Equal(2, user.Sessions.Count);
        }

        Assert.Single(user.ActiveSessionsAt(_clock.UtcNow));
        var replay = user.RotateSession(
            TokenHash.Create($"t{rotations - 1}"), TokenHash.Create("stolen"), SessionLifetime.Default, _clock);
        Assert.True(replay.ReplayDetected);
    }

    [Fact]
    public void RotateSession_keeps_two_sessions_per_device_chain()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("a0"), SessionLifetime.Default, _clock);
        user.StartSession(TokenHash.Create("b0"), SessionLifetime.Default, _clock);

        for (var i = 0; i < 20; i++)
        {
            user.RotateSession(TokenHash.Create($"a{i}"), TokenHash.Create($"a{i + 1}"), SessionLifetime.Default, _clock);
            user.RotateSession(TokenHash.Create($"b{i}"), TokenHash.Create($"b{i + 1}"), SessionLifetime.Default, _clock);
        }

        Assert.Equal(4, user.Sessions.Count);
        Assert.Equal(2, user.ActiveSessionsAt(_clock.UtcNow).Count);
    }

    [Fact]
    public void RotateSession_replay_of_a_rotated_token_is_rejected_and_revokes_every_session()
    {
        var user = ActiveUser();
        var stolen = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        var otherDevice = user.StartSession(TokenHash.Create("other"), SessionLifetime.Default, _clock);
        var legitimate = user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock).NewSession!;
        user.ClearDomainEvents();

        var replay = user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t3"), SessionLifetime.Default, _clock);

        Assert.True(replay.ReplayDetected);
        Assert.Null(replay.NewSession);
        Assert.Equal(UserErrors.RefreshTokenReplayed, replay.Error);
        Assert.Empty(user.ActiveSessionsAt(_clock.UtcNow));
        Assert.Equal(SessionRevocationReason.ReplayDetected, legitimate.RevocationReason);
        Assert.Equal(SessionRevocationReason.ReplayDetected, otherDevice.RevocationReason);
        Assert.Equal(3, user.Sessions.Count);
        Assert.DoesNotContain(user.Sessions, s => s.Hash == TokenHash.Create("t3"));

        var detected = DomainAssert.Raised<RefreshTokenReplayDetected>(user);
        Assert.Equal(stolen.Id, detected.ReplayedSessionId);
        Assert.Equal(2, detected.RevokedSessionCount);
        Assert.Equal(2, user.DomainEvents.OfType<SessionRevoked>().Count());
        DomainAssert.NotRaised<RefreshTokenRotated>(user);
    }

    [Fact]
    public void RotateSession_after_a_replay_the_legitimate_new_token_is_unusable_too()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock);
        user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t3"), SessionLifetime.Default, _clock);

        DomainAssert.Throws(
            UserErrors.SessionRevoked,
            () => user.RotateSession(TokenHash.Create("t2"), TokenHash.Create("t4"), SessionLifetime.Default, _clock));
    }

    [Fact]
    public void RotateSession_second_replay_is_still_reported_without_revoking_anything_more()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock);
        user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t3"), SessionLifetime.Default, _clock);
        user.ClearDomainEvents();

        var replay = user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t3"), SessionLifetime.Default, _clock);

        Assert.True(replay.ReplayDetected);
        Assert.Equal(0, DomainAssert.Raised<RefreshTokenReplayDetected>(user).RevokedSessionCount);
        DomainAssert.NotRaised<SessionRevoked>(user);
    }

    [Fact]
    public void RotateSession_rejects_a_session_revoked_by_the_user_without_touching_the_others()
    {
        var user = ActiveUser();
        var loggedOut = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        var other = user.StartSession(TokenHash.Create("other"), SessionLifetime.Default, _clock);
        user.RevokeSession(loggedOut.Id, _clock);
        user.ClearDomainEvents();

        DomainAssert.Throws(
            UserErrors.SessionRevoked,
            () => user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock));
        Assert.True(other.IsUsableAt(_clock.UtcNow));
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void RotateSession_rejects_an_expired_session()
    {
        var user = ActiveUser();
        var session = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Create(TimeSpan.FromHours(1)), _clock);
        user.ClearDomainEvents();
        _clock.Set(session.ExpiresAtUtc);

        DomainAssert.Throws(
            UserErrors.SessionExpired,
            () => user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock));
        Assert.Null(session.ReplacedBy);
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void RotateSession_rejects_an_unknown_token()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);

        DomainAssert.Throws(
            UserErrors.SessionNotFound,
            () => user.RotateSession(TokenHash.Create("nope"), TokenHash.Create("t2"), SessionLifetime.Default, _clock));
    }

    [Fact]
    public void RotateSession_rejects_a_new_token_hash_already_used()
    {
        var user = ActiveUser();
        var session = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);

        DomainAssert.Throws(
            UserErrors.TokenHashAlreadyUsed,
            () => user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t1"), SessionLifetime.Default, _clock));
        Assert.True(session.IsUsableAt(_clock.UtcNow));
    }

    [Fact]
    public void RotateSession_rejects_a_locked_user()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        user.Lock(_clock);

        DomainAssert.Throws(
            UserErrors.Locked,
            () => user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock));
    }

    // ---- RevokeSession / RevokeAllSessions ----

    [Fact]
    public void RevokeSession_makes_the_session_unusable_and_raises_SessionRevoked()
    {
        var user = ActiveUser();
        var session = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        user.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromMinutes(10));

        user.RevokeSession(session.Id, _clock);

        Assert.Equal(_clock.UtcNow, session.RevokedAtUtc);
        Assert.Equal(SessionRevocationReason.UserRequested, session.RevocationReason);
        Assert.Null(session.ReplacedBy);
        Assert.False(session.IsUsableAt(_clock.UtcNow));
        var revoked = DomainAssert.Raised<SessionRevoked>(user);
        Assert.Equal(user.Id, revoked.UserId);
        Assert.Equal(session.Id, revoked.SessionId);
        Assert.Equal(SessionRevocationReason.UserRequested, revoked.Reason);
    }

    [Fact]
    public void RevokeSession_rejects_an_already_revoked_session()
    {
        var user = ActiveUser();
        var session = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        user.RevokeSession(session.Id, _clock);
        user.ClearDomainEvents();

        DomainAssert.Throws(UserErrors.SessionAlreadyRevoked, () => user.RevokeSession(session.Id, _clock));
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void RevokeSession_on_an_expired_session_changes_nothing_and_raises_nothing()
    {
        var user = ActiveUser();
        var session = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Create(TimeSpan.FromHours(1)), _clock);
        user.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromHours(1));

        user.RevokeSession(session.Id, _clock);

        Assert.False(session.IsRevoked);
        Assert.Null(session.RevokedAtUtc);
        Assert.Null(session.RevocationReason);
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void RevokeSession_still_rejects_a_revoked_session_once_it_has_expired()
    {
        var user = ActiveUser();
        var session = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Create(TimeSpan.FromHours(1)), _clock);
        user.RevokeSession(session.Id, _clock);
        user.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromHours(2));

        DomainAssert.Throws(UserErrors.SessionAlreadyRevoked, () => user.RevokeSession(session.Id, _clock));
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void PurgeExpiredSessions_is_allowed_on_a_disabled_user_and_raises_nothing()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("t1"), SessionLifetime.Create(TimeSpan.FromHours(1)), _clock);
        user.Disable(_clock);
        user.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(1, user.PurgeExpiredSessions(_clock));

        Assert.Empty(user.Sessions);
        Assert.Equal(UserStatus.Disabled, user.Status);
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void RevokeSession_rejects_a_rotated_session()
    {
        var user = ActiveUser();
        var session = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock);

        DomainAssert.Throws(UserErrors.SessionAlreadyRevoked, () => user.RevokeSession(session.Id, _clock));
    }

    [Fact]
    public void RevokeSession_rejects_an_unknown_session() =>
        DomainAssert.Throws(UserErrors.SessionNotFound, () => ActiveUser().RevokeSession(SessionId.New(), _clock));

    [Fact]
    public void RevokeAllSessions_revokes_only_sessions_not_already_revoked()
    {
        var user = ActiveUser();
        var alreadyRevoked = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        var second = user.StartSession(TokenHash.Create("t2"), SessionLifetime.Default, _clock);
        var third = user.StartSession(TokenHash.Create("t3"), SessionLifetime.Default, _clock);
        user.RevokeSession(alreadyRevoked.Id, _clock);
        var firstRevocation = alreadyRevoked.RevokedAtUtc;
        user.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromMinutes(1));

        user.RevokeAllSessions(_clock);

        Assert.Empty(user.ActiveSessionsAt(_clock.UtcNow));
        Assert.Equal(firstRevocation, alreadyRevoked.RevokedAtUtc);
        Assert.Equal(
            new[] { second.Id, third.Id },
            user.DomainEvents.OfType<SessionRevoked>().Select(e => e.SessionId).ToArray());
    }

    [Fact]
    public void RevokeAllSessions_without_session_raises_nothing()
    {
        var user = ActiveUser();

        user.RevokeAllSessions(_clock);

        DomainAssert.NoEvents(user);
    }

    // ---- PurgeExpiredSessions ----

    [Fact]
    public void PurgeExpiredSessions_removes_only_expired_sessions()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("short"), SessionLifetime.Create(TimeSpan.FromHours(1)), _clock);
        var longLived = user.StartSession(TokenHash.Create("long"), SessionLifetime.Default, _clock);
        var revoked = user.StartSession(TokenHash.Create("revoked"), SessionLifetime.Default, _clock);
        user.RevokeSession(revoked.Id, _clock);
        user.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromHours(1));

        var removed = user.PurgeExpiredSessions(_clock);

        Assert.Equal(1, removed);
        Assert.Equal(new[] { longLived.Id, revoked.Id }, user.Sessions.Select(s => s.Id).ToArray());
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void PurgeExpiredSessions_keeps_an_expired_rotated_session_while_its_successor_is_usable()
    {
        var user = ActiveUser();
        var rotated = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Create(TimeSpan.FromHours(1)), _clock);
        var successor = user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock).NewSession!;
        _clock.Advance(TimeSpan.FromHours(2));
        user.ClearDomainEvents();

        var removed = user.PurgeExpiredSessions(_clock);

        Assert.Equal(0, removed);
        Assert.True(rotated.IsExpiredAt(_clock.UtcNow));
        Assert.Equal(new[] { rotated.Id, successor.Id }, user.Sessions.Select(s => s.Id).ToArray());

        var replay = user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t3"), SessionLifetime.Default, _clock);

        Assert.True(replay.ReplayDetected);
        Assert.Equal(SessionRevocationReason.ReplayDetected, successor.RevocationReason);
    }

    [Fact]
    public void PurgeExpiredSessions_removes_an_expired_rotated_session_once_its_successor_is_revoked()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("t1"), SessionLifetime.Create(TimeSpan.FromHours(1)), _clock);
        var successor = user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock).NewSession!;
        user.RevokeSession(successor.Id, _clock);
        _clock.Advance(TimeSpan.FromHours(2));

        var removed = user.PurgeExpiredSessions(_clock);

        Assert.Equal(1, removed);
        Assert.Same(successor, Assert.Single(user.Sessions));
    }

    [Fact]
    public void PurgeExpiredSessions_removes_a_rotated_session_together_with_its_expired_successor()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("t1"), SessionLifetime.Create(TimeSpan.FromHours(1)), _clock);
        var successor = user.RotateSession(
            TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Create(TimeSpan.FromHours(3)), _clock).NewSession!;
        _clock.Set(successor.ExpiresAtUtc);

        var removed = user.PurgeExpiredSessions(_clock);

        Assert.Equal(2, removed);
        Assert.Empty(user.Sessions);
    }

    [Fact]
    public void PurgeExpiredSessions_does_not_remove_a_rotated_session_that_has_not_expired()
    {
        var user = ActiveUser();
        var rotated = user.StartSession(TokenHash.Create("t1"), SessionLifetime.Default, _clock);
        var successor = user.RotateSession(TokenHash.Create("t1"), TokenHash.Create("t2"), SessionLifetime.Default, _clock).NewSession!;
        user.RevokeSession(successor.Id, _clock);

        Assert.Equal(0, user.PurgeExpiredSessions(_clock));
        Assert.Contains(rotated, user.Sessions);
    }

    [Fact]
    public void UpgradePasswordHash_replaces_the_hash_without_event_and_keeps_the_sessions()
    {
        var user = ActiveUser();
        var session = user.StartSession(TokenHash.Create("upgrade-token"), SessionLifetime.Default, _clock);
        user.ClearDomainEvents();

        user.UpgradePasswordHash(Password("stronger-hash"));

        Assert.Equal(Password("stronger-hash"), user.PasswordHash);
        Assert.Null(user.PasswordChangedAtUtc);
        Assert.True(session.IsUsableAt(_clock.UtcNow));
        DomainAssert.NoEvents(user);
    }

    [Fact]
    public void UpgradePasswordHash_is_refused_without_password_or_on_a_disabled_user()
    {
        DomainAssert.Throws(UserErrors.PasswordNotSet, () => InvitedUser().UpgradePasswordHash(Password()));
        DomainAssert.Throws(UserErrors.Disabled, () => DisabledUser().UpgradePasswordHash(Password()));
    }

    [Fact]
    public void Erase_removes_everything_personal_disables_the_account_and_raises_UserErased_without_the_email()
    {
        var user = ActiveUser();
        user.StartSession(TokenHash.Create("erase-token"), SessionLifetime.Default, _clock);
        user.ClearDomainEvents();
        var placeholder = Email.Create("erased-1@erased.invalid");

        user.Erase(placeholder, _clock);

        Assert.Equal(placeholder, user.Email);
        Assert.Null(user.PasswordHash);
        Assert.Null(user.EmailVerifiedAtUtc);
        Assert.Equal(UserStatus.Disabled, user.Status);
        Assert.Equal(_clock.UtcNow, user.ErasedAtUtc);
        Assert.True(user.IsErased);
        Assert.Empty(user.Sessions);
        var erased = Assert.IsType<UserErased>(Assert.Single(user.DomainEvents));
        Assert.Equal(user.Id, erased.UserId);
    }

    [Fact]
    public void Erase_is_allowed_in_every_status_and_does_nothing_the_second_time()
    {
        foreach (var user in new[] { PendingUser(), InvitedUser(), LockedUser(), DisabledUser() })
        {
            user.ClearDomainEvents();
            user.Erase(Email.Create("erased-2@erased.invalid"), _clock);
            Assert.True(user.IsErased);

            user.ClearDomainEvents();
            user.Erase(Email.Create("erased-3@erased.invalid"), _clock);
            Assert.Equal("erased-2@erased.invalid", user.Email.Value);
            DomainAssert.NoEvents(user);
        }
    }
}
