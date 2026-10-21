namespace Identity.Domain.Users;

/// <summary>Compte humain global (hors tenant) : identifiants, cycle de vie et sessions de refresh.</summary>
public sealed class User : AggregateRoot<UserId>
{
    public const int NameMaxLength = 60;
    public const int CityMaxLength = 80;
    public const int TimeZoneMaxLength = TimeZoneId.MaxLength;
    public const string DefaultLanguage = "fr";
    public const string DefaultTimeZone = "Europe/Paris";
    public static readonly IReadOnlyList<string> Languages = ["fr", "en"];

    public const int MaxProtectedSecretLength = 512;
    public const int RecoveryCodeCount = 10;

    private readonly List<RefreshSession> _sessions = [];

    // Empreintes des codes de secours restants (jamais les codes) ; pas readonly : l'ORM le pose au chargement.
    private List<string> _recoveryCodeHashes = [];
    public Email Email { get; private set; }
    public PasswordHash? PasswordHash { get; private set; }
    public UserStatus Status { get; private set; }
    public UserStatus? StatusBeforeLock { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? EmailVerifiedAtUtc { get; private set; }
    public DateTimeOffset? PasswordChangedAtUtc { get; private set; }
    public DateTimeOffset? ErasedAtUtc { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public PhoneNumber? Phone { get; private set; }
    public string? City { get; private set; }
    public string Language { get; private set; } = DefaultLanguage;
    public string TimeZone { get; private set; } = DefaultTimeZone;
    public bool ProductNewsEmails { get; private set; }
    public DateTimeOffset? AvatarUpdatedAtUtc { get; private set; }

    /// <summary>Secret de l'application d'authentification, CHIFFRÉ par l'infrastructure ; null sans validation en deux étapes.</summary>
    public string? TwoFactorSecret { get; private set; }

    /// <summary>Secret en attente de confirmation par un premier code, chiffré lui aussi.</summary>
    public string? PendingTwoFactorSecret { get; private set; }

    public DateTimeOffset? TwoFactorEnabledAtUtc { get; private set; }

    /// <summary>Dernier pas de temps accepté : un code déjà utilisé ne sert pas deux fois.</summary>
    public long? LastTwoFactorStep { get; private set; }

    public bool IsTwoFactorEnabled => TwoFactorSecret is not null;

    public int RecoveryCodesLeft => _recoveryCodeHashes.Count;
    public IReadOnlyCollection<RefreshSession> Sessions => _sessions.AsReadOnly();
    public bool IsEmailVerified => EmailVerifiedAtUtc is not null;
    public bool HasPassword => PasswordHash is not null;
    public bool IsErased => ErasedAtUtc is not null;
    public bool CanStartSession => Status == UserStatus.Active && HasPassword;
    public bool HasAvatar => AvatarUpdatedAtUtc is not null;

    private User(UserId id, Email email, PasswordHash? passwordHash, DateTimeOffset createdAtUtc): base(id)
    {
        Email = email;
        PasswordHash = passwordHash;
        Status = UserStatus.Pending;
        CreatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private User()
    {
        Email = null!;
    }

    public static User Register(Email email, PasswordHash passwordHash, bool isEmailUnique, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(passwordHash);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!isEmailUnique, UserErrors.EmailAlreadyUsed);

        var now = clock.UtcNow;
        var user = new User(UserId.New(), email, passwordHash, now);
        user.Raise(new UserRegistered(user.Id, email, now));
        return user;
    }

    public static User RegisterInvited(Email email, bool isEmailUnique, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!isEmailUnique, UserErrors.EmailAlreadyUsed);

        var now = clock.UtcNow;
        var user = new User(UserId.New(), email, passwordHash: null, now);
        user.Raise(new UserInvited(user.Id, email, now));
        return user;
    }

    public IReadOnlyCollection<RefreshSession> ActiveSessionsAt(DateTimeOffset instant) =>
        _sessions.Where(session => session.IsUsableAt(instant)).ToList().AsReadOnly();

    public void VerifyEmail(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotDisabled();
        EnsureHasPassword();
        DomainException.ThrowIf(IsEmailVerified, UserErrors.EmailAlreadyVerified);

        MarkEmailVerified(clock.UtcNow);
    }

    public void AcceptInvitation(PasswordHash passwordHash, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(passwordHash);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotDisabled();
        DomainException.ThrowIf(HasPassword, UserErrors.NoPendingInvitation);

        var now = clock.UtcNow;
        PasswordHash = passwordHash;
        MarkEmailVerified(now);
        Raise(new UserInvitationAccepted(Id, Email, now));
    }

    public void ChangeEmail(Email newEmail, bool isEmailUnique, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newEmail);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotDisabled();
        DomainException.ThrowIf(Status == UserStatus.Locked, UserErrors.Locked);
        if (newEmail == Email)
        {
            return;
        }

        DomainException.ThrowIf(!isEmailUnique, UserErrors.EmailAlreadyUsed);

        var previous = Email;
        Email = newEmail;
        EmailVerifiedAtUtc = null;
        Raise(new UserEmailChanged(Id, previous, newEmail, clock.UtcNow));
    }

    public void ChangePassword(PasswordHash newPasswordHash, IClock clock, SessionId? sessionToKeep = null)
    {
        ArgumentNullException.ThrowIfNull(newPasswordHash);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotDisabled();
        EnsureHasPassword();
        if (newPasswordHash == PasswordHash)
        {
            return;
        }

        DomainException.ThrowIf(
            sessionToKeep is not null && _sessions.All(session => session.Id != sessionToKeep),
            UserErrors.SessionNotFound);

        var now = clock.UtcNow;
        PasswordHash = newPasswordHash;
        PasswordChangedAtUtc = now;
        Raise(new PasswordChanged(Id, now));
        RevokeSessions(now, SessionRevocationReason.PasswordChanged, sessionToKeep);
    }

    /// <summary>Prénom, nom, téléphone et ville : facultatifs, saisis par l'utilisateur lui-même.</summary>
    public void UpdateProfile(string? firstName, string? lastName, PhoneNumber? phone, string? city, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotDisabled();
        var first = Optional(firstName, NameMaxLength, UserErrors.NameTooLong);
        var last = Optional(lastName, NameMaxLength, UserErrors.NameTooLong);
        var town = Optional(city, CityMaxLength, UserErrors.CityTooLong);
        if (first == FirstName && last == LastName && phone == Phone && town == City)
        {
            return;
        }

        FirstName = first;
        LastName = last;
        Phone = phone;
        City = town;
        Raise(new UserProfileChanged(Id, clock.UtcNow));
    }

    /// <summary>Langue de l'interface (fr, en), fuseau horaire IANA et emails d'actualités de la plateforme.</summary>
    public void ChangePreferences(string? language, TimeZoneId timeZone, bool productNewsEmails, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotDisabled();
        var code = language?.Trim().ToLowerInvariant() ?? string.Empty;
        DomainException.ThrowIf(!Languages.Contains(code), UserErrors.LanguageUnsupported);
        var zone = timeZone.Value;
        if (code == Language && zone == TimeZone && productNewsEmails == ProductNewsEmails)
        {
            return;
        }

        Language = code;
        TimeZone = zone;
        ProductNewsEmails = productNewsEmails;
        Raise(new UserPreferencesChanged(Id, code, productNewsEmails, clock.UtcNow));
    }

    /// <summary>La photo est stockée à part (Infrastructure) ; le compte n'en garde que la date, qui sert de version.</summary>
    public void ChangeAvatar(bool hasAvatar, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotDisabled();
        if (!hasAvatar && !HasAvatar)
        {
            return;
        }

        var now = clock.UtcNow;
        AvatarUpdatedAtUtc = hasAvatar ? now : null;
        Raise(new UserAvatarChanged(Id, hasAvatar, now));
    }

    // Même mot de passe re-haché plus fort : pas un changement, donc ni événement ni session révoquée.
    public void UpgradePasswordHash(PasswordHash upgradedHash)
    {
        ArgumentNullException.ThrowIfNull(upgradedHash);
        EnsureNotDisabled();
        EnsureHasPassword();

        PasswordHash = upgradedHash;
    }

    // Irréversible, permis dans tous les statuts : le compte garde son id (d'autres modules le référencent) et perd
    // tout ce qui est personnel. L'adresse de substitution libère la vraie adresse pour une future inscription.
    public void Erase(Email placeholderEmail, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(placeholderEmail);
        ArgumentNullException.ThrowIfNull(clock);
        if (IsErased)
        {
            return;
        }

        var now = clock.UtcNow;
        Email = placeholderEmail;
        PasswordHash = null;
        EmailVerifiedAtUtc = null;
        PasswordChangedAtUtc = null;
        Status = UserStatus.Disabled;
        StatusBeforeLock = null;
        ErasedAtUtc = now;
        FirstName = null;
        LastName = null;
        Phone = null;
        City = null;
        Language = DefaultLanguage;
        TimeZone = DefaultTimeZone;
        ProductNewsEmails = false;
        AvatarUpdatedAtUtc = null;
        _sessions.Clear();
        Raise(new UserErased(Id, now));
    }

    public void Lock(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotDisabled();
        DomainException.ThrowIf(Status == UserStatus.Locked, UserErrors.AlreadyLocked);

        var now = clock.UtcNow;
        StatusBeforeLock = Status;
        Status = UserStatus.Locked;
        Raise(new UserLocked(Id, now));
        RevokeSessions(now, SessionRevocationReason.UserLocked);
    }

    public void Unlock(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotDisabled();
        DomainException.ThrowIf(Status != UserStatus.Locked, UserErrors.NotLocked);

        // La valeur de repli ne sert qu'à un compte verrouillé avant que le statut précédent soit enregistré.
        Status = StatusBeforeLock ?? (IsEmailVerified ? UserStatus.Active : UserStatus.Pending);
        StatusBeforeLock = null;
        Raise(new UserUnlocked(Id, Status, clock.UtcNow));
    }

    public void Disable(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotDisabled();

        var now = clock.UtcNow;
        Status = UserStatus.Disabled;
        StatusBeforeLock = null;
        Raise(new UserDisabled(Id, now));
        RevokeSessions(now, SessionRevocationReason.UserDisabled);
    }

    /// <summary>Prépare la validation en deux étapes : un nouveau secret, actif seulement après un premier code juste.</summary>
    public void BeginTwoFactorSetup(string protectedSecret)
    {
        DomainException.ThrowIf(IsTwoFactorEnabled, UserErrors.TwoFactorAlreadyEnabled);
        PendingTwoFactorSecret = ProtectedSecret(protectedSecret);
    }

    /// <summary>
    /// Le premier code de l'application est juste : le secret en attente devient actif, avec les empreintes des codes
    /// de secours (montrés une seule fois à l'utilisateur).
    /// </summary>
    public void EnableTwoFactor(long step, IEnumerable<string> recoveryCodeHashes, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(recoveryCodeHashes);
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(IsTwoFactorEnabled, UserErrors.TwoFactorAlreadyEnabled);
        DomainException.ThrowIf(PendingTwoFactorSecret is null, UserErrors.TwoFactorSetupMissing);

        var now = clock.UtcNow;
        TwoFactorSecret = PendingTwoFactorSecret;
        PendingTwoFactorSecret = null;
        TwoFactorEnabledAtUtc = now;
        LastTwoFactorStep = step;
        _recoveryCodeHashes = recoveryCodeHashes.Distinct(StringComparer.Ordinal).ToList();
        Raise(new TwoFactorEnabled(Id, now));
    }

    /// <summary>Retire la validation en deux étapes ; les autres sessions sont fermées.</summary>
    public void DisableTwoFactor(IClock clock, SessionId? sessionToKeep = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsTwoFactorEnabled, UserErrors.TwoFactorNotEnabled);

        var now = clock.UtcNow;
        TwoFactorSecret = null;
        PendingTwoFactorSecret = null;
        TwoFactorEnabledAtUtc = null;
        LastTwoFactorStep = null;
        _recoveryCodeHashes = [];
        Raise(new TwoFactorDisabled(Id, now));
        RevokeSessions(now, SessionRevocationReason.TwoFactorDisabled, sessionToKeep);
    }

    /// <summary>Un code de l'application vient d'être accepté pour ce pas de temps : il ne sert plus.</summary>
    public void UseTwoFactorStep(long step)
    {
        DomainException.ThrowIf(!IsTwoFactorEnabled, UserErrors.TwoFactorNotEnabled);
        DomainException.ThrowIf(LastTwoFactorStep is { } last && step <= last, UserErrors.TwoFactorCodeReused);
        LastTwoFactorStep = step;
    }

    /// <summary>Un code de secours (son empreinte) remplace le code de l'application, une seule fois.</summary>
    public void UseRecoveryCode(string codeHash, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsTwoFactorEnabled, UserErrors.TwoFactorNotEnabled);
        DomainException.ThrowIf(!_recoveryCodeHashes.Contains(codeHash, StringComparer.Ordinal), UserErrors.RecoveryCodeInvalid);
        _recoveryCodeHashes = _recoveryCodeHashes.Where(hash => hash != codeHash).ToList();
        Raise(new RecoveryCodeUsed(Id, _recoveryCodeHashes.Count, clock.UtcNow));
    }

    private static string ProtectedSecret(string? value)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(value) || value.Length > MaxProtectedSecretLength, UserErrors.TwoFactorSecretInvalid);
        return value!;
    }

    /// <param name="deviceLabel">Appareil et navigateur (« Chrome · macOS ») ; tronqué, jamais l'en-tête brut.</param>
    public RefreshSession StartSession(TokenHash tokenHash, SessionLifetime lifetime, IClock clock, string? deviceLabel = null)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureCanStartSession();
        EnsureTokenHashIsUnused(tokenHash);

        var now = clock.UtcNow;
        var session = RefreshSession.Create(tokenHash, now, lifetime, deviceLabel);
        _sessions.Add(session);
        Raise(new SessionStarted(Id, session.Id, session.ExpiresAtUtc, now));
        return session;
    }

    public SessionRotationResult RotateSession(
        TokenHash presentedTokenHash,
        TokenHash newTokenHash,
        SessionLifetime lifetime,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(presentedTokenHash);
        ArgumentNullException.ThrowIfNull(newTokenHash);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureCanStartSession();

        var current = _sessions.SingleOrDefault(session => session.Hash == presentedTokenHash);
        DomainException.ThrowIf(current is null, UserErrors.SessionNotFound);

        var now = clock.UtcNow;
        if (current!.WasRotated)
        {
            var revokedCount = RevokeSessions(now, SessionRevocationReason.ReplayDetected);
            Raise(new RefreshTokenReplayDetected(Id, current.Id, revokedCount, now));
            return SessionRotationResult.Replay();
        }

        DomainException.ThrowIf(current.IsRevoked, UserErrors.SessionRevoked);
        DomainException.ThrowIf(current.IsExpiredAt(now), UserErrors.SessionExpired);
        EnsureTokenHashIsUnused(newTokenHash);

        var next = RefreshSession.Create(newTokenHash, now, lifetime, current.DeviceLabel);
        current.Rotate(next.Id, now);
        _sessions.Add(next);
        DropSupersededRotations();
        Raise(new RefreshTokenRotated(Id, current.Id, next.Id, next.ExpiresAtUtc, now));
        return SessionRotationResult.Rotated(next);
    }

    public void RevokeSession(SessionId sessionId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var session = _sessions.SingleOrDefault(candidate => candidate.Id == sessionId);
        DomainException.ThrowIf(session is null, UserErrors.SessionNotFound);

        DomainException.ThrowIf(session!.IsRevoked, UserErrors.SessionAlreadyRevoked);

        var now = clock.UtcNow;
        if (session.IsExpiredAt(now))
        {
            return;
        }

        session.Revoke(now, SessionRevocationReason.UserRequested);
        Raise(new SessionRevoked(Id, session.Id, SessionRevocationReason.UserRequested, now));
    }

    public void RevokeAllSessions(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        RevokeSessions(clock.UtcNow, SessionRevocationReason.UserRequested);
    }

    public int PurgeExpiredSessions(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.UtcNow;
        var usableIds = _sessions.Where(session => session.IsUsableAt(now)).Select(session => session.Id).ToHashSet();
        return _sessions.RemoveAll(session =>
            session.IsExpiredAt(now) && !(session.ReplacedBy is { } successor && usableIds.Contains(successor)));
    }

    private void DropSupersededRotations()
    {
        var rotatedIds = _sessions.Where(session => session.WasRotated).Select(session => session.Id).ToHashSet();
        _sessions.RemoveAll(session => session.ReplacedBy is { } successor && rotatedIds.Contains(successor));
    }

    private void MarkEmailVerified(DateTimeOffset now)
    {
        EmailVerifiedAtUtc = now;
        if (Status == UserStatus.Pending)
        {
            Status = UserStatus.Active;
        }
        else if (Status == UserStatus.Locked && StatusBeforeLock == UserStatus.Pending)
        {
            StatusBeforeLock = UserStatus.Active;
        }

        Raise(new EmailVerified(Id, Email, now));
    }

    private int RevokeSessions(DateTimeOffset now, SessionRevocationReason reason, SessionId? except = null)
    {
        var revoked = 0;
        foreach (var session in _sessions.Where(session => !session.IsRevoked && session.Id != except))
        {
            session.Revoke(now, reason);
            Raise(new SessionRevoked(Id, session.Id, reason, now));
            revoked++;
        }

        return revoked;
    }

    private void EnsureNotDisabled() => DomainException.ThrowIf(Status == UserStatus.Disabled, UserErrors.Disabled);

    /// <summary>Le compte peut-il ouvrir une session (actif, avec un mot de passe) ? Lève l'erreur sinon.</summary>
    public void EnsureCanStartSession()
    {
        EnsureNotDisabled();
        DomainException.ThrowIf(Status == UserStatus.Locked, UserErrors.Locked);
        EnsureHasPassword();
        DomainException.ThrowIf(Status == UserStatus.Pending, UserErrors.EmailNotVerified);
    }

    private static string? Optional(string? value, int maxLength, DomainError tooLong)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        DomainException.ThrowIf(trimmed.Length > maxLength, tooLong);
        return trimmed;
    }

    private void EnsureHasPassword() => DomainException.ThrowIf(!HasPassword, UserErrors.PasswordNotSet);

    private void EnsureTokenHashIsUnused(TokenHash tokenHash) =>
        DomainException.ThrowIf(_sessions.Any(session => session.Hash == tokenHash), UserErrors.TokenHashAlreadyUsed);
}
