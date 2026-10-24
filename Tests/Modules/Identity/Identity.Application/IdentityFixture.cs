using System.Text;
using Identity.Application.Abstractions;
using Identity.Application.Commands;
using Identity.Domain.Users;

namespace Identity.Application;

/// <summary>
/// Ports en mémoire du module Identity. Les hashes sont des faux lisibles : salés comme les vrais (deux hashes d'un
/// même mot de passe diffèrent), mais vérifiables trivialement.
/// </summary>
public sealed class IdentityFixture :
    IUserRepository, IIdentityUnitOfWork, IUserEmailUniquenessChecker, IPasswordHasher, IRefreshTokens,
    IAccessTokenIssuer, IUserTokens, IIdentityNotifier, IPasswordAttempts, ITwoFactorAttempts, ICompromisedPasswords, IUserTraces, ITwoFactorSecrets, ITwoFactorChallenges,
    ITenantMemberships, IUserAvatars
{
    public const string Password = "correct horse battery";

    private readonly Dictionary<UserId, User> _users = [];
    private readonly Dictionary<string, (UserId UserId, UserTokenPurpose Purpose, DateTimeOffset ExpiresAtUtc)> _tokens = [];
    private int _sequence;
    private string? _consuming;

    private readonly Dictionary<string, (int Failures, DateTimeOffset StartedAtUtc)> _attempts = [];

    public FakeClock Clock { get; } = new();

    public IdentitySettings Settings { get; set; } = new();

    public int SaveCount { get; private set; }

    public int HashCount { get; private set; }

    /// <summary>Les hashes dont le « sel » commence par ce préfixe sont signalés comme obsolètes.</summary>
    public const string OutdatedHashPrefix = "old";

    public HashSet<string> CompromisedPasswords { get; } = [];

    /// <summary>Paires (utilisateur, tenant) autorisées à agir pour le tenant.</summary>
    public HashSet<(UserId UserId, TenantId TenantId)> Memberships { get; } = [];

    public HashSet<(UserId UserId, TenantId TenantId)> SoleOwnerships { get; } = [];

    public List<(UserId UserId, string Email)> Forgotten { get; } = [];

    public List<(string Kind, string Email, string Token)> Sent { get; } = [];

    public User Single => Assert.Single(_users.Values);

    public string LastTokenSentTo(string email) => Sent.Last(sent => sent.Email == email).Token;

    // --- Handlers, branchés sur les faux.

    public PasswordVerifier Verifier => new(this, this, Settings, Clock);

    public RegisterUserHandler Register => new(this, this, this, this, this, this, this, Settings, Clock);

    public InviteUserHandler Invite => new(this, this, this, this, this, Settings, Clock);

    public EnsureInvitedUserHandler EnsureInvited => new(this, this, this, Clock);

    public SendUserInvitationHandler SendInvitation => new(this, this, this, this, Settings, Clock);

    public AcceptInvitationHandler AcceptInvitation => new(this, this, this, this, this, Clock);

    public VerifyEmailHandler VerifyEmail => new(this, this, Verifier, this, Clock);

    public ResendEmailVerificationHandler ResendEmailVerification => new(this, this, this, this, Settings, Clock);

    public SessionOpener Sessions => new(this, this, Settings, Clock);

    public LoginHandler Login => new(this, Verifier, Sessions, this, this, Settings, Clock);

    public TwoFactorVerifier TwoFactorCodes => new(this, this, Settings, Clock);

    public StartTwoFactorSetupHandler StartTwoFactor => new(this, Verifier, this, this, Settings);

    public ConfirmTwoFactorHandler ConfirmTwoFactor => new(this, this, this, Clock);

    public DisableTwoFactorHandler DisableTwoFactor => new(this, Verifier, TwoFactorCodes, this, Clock);

    public CompleteTwoFactorLoginHandler CompleteTwoFactorLogin => new(this, this, TwoFactorCodes, Sessions, this, Clock);

    public RefreshSessionHandler Refresh => new(this, this, this, this, this, Settings, Clock);

    public SelectTenantHandler SelectTenant => new(this, this, this);

    public LogoutHandler Logout => new(this, this, this, Clock);

    public ChangePasswordHandler ChangePassword => new(this, this, this, Verifier, this, this, Clock);

    public RequestPasswordResetHandler RequestPasswordReset => new(this, this, this, this, Settings, Clock);

    public ResetPasswordHandler ResetPassword => new(this, this, this, this, this, this, Clock);

    public EraseUserHandler Erase => new(this, Verifier, this, this, this, this, Clock);

    public UpdateMyProfileHandler UpdateProfile => new(this, this, Clock);

    public ChangeMyPreferencesHandler ChangePreferences => new(this, this, Clock);

    public SetMyAvatarHandler SetAvatar => new(this, this, this, Clock);

    public RemoveMyAvatarHandler RemoveAvatar => new(this, this, this, Clock);

    public Dictionary<UserId, AvatarImage> Avatars { get; } = [];

    public ChangeEmailHandler ChangeEmail => new(this, this, Verifier, this, this, this, Settings, Clock);

    // --- Préconditions (Given).

    /// <summary>Un utilisateur enregistré, vérifié et actif ; événements effacés, rien encore envoyé.</summary>
    public async Task<User> GivenActiveUserAsync(string email = "chef@example.com")
    {
        await Register.HandleAsync(new RegisterUserCommand(email, Password));
        await VerifyEmail.HandleAsync(new VerifyEmailCommand(LastTokenSentTo(email), Password));
        var user = _users.Values.Single(candidate => candidate.Email.Value == email);
        user.ClearDomainEvents();
        Sent.Clear();
        SaveCount = 0;
        return user;
    }

    public async Task<AuthTokensDto> GivenSessionAsync(string email = "chef@example.com") =>
        (await Login.HandleAsync(new LoginCommand(email, Password))).Tokens!;

    // --- Ports.

    // Faux chiffrement (réversible, préfixé) : le vrai AES-GCM est testé avec l'infrastructure.
    bool ITwoFactorSecrets.IsConfigured => true;

    string ITwoFactorSecrets.Protect(byte[] secret) => "test." + Convert.ToBase64String(secret);

    byte[] ITwoFactorSecrets.Unprotect(string protectedSecret) => Convert.FromBase64String(protectedSecret["test.".Length..]);

    string ITwoFactorChallenges.Issue(UserId userId, DateTimeOffset expiresAtUtc) => $"{userId.Value}|{expiresAtUtc.ToUnixTimeSeconds()}";

    UserId? ITwoFactorChallenges.Read(string? token, DateTimeOffset now)
    {
        var parts = (token ?? string.Empty).Split('|');
        return parts.Length == 2 && Guid.TryParse(parts[0], out var id) && long.TryParse(parts[1], out var expires) && now.ToUnixTimeSeconds() < expires
            ? UserId.From(id)
            : null;
    }

    Task<AvatarImage?> IUserAvatars.FindAsync(UserId userId, CancellationToken cancellationToken) =>
        Task.FromResult(Avatars.GetValueOrDefault(userId));

    Task IUserAvatars.SaveAsync(UserId userId, AvatarImage image, CancellationToken cancellationToken)
    {
        Avatars[userId] = image;
        return Task.CompletedTask;
    }

    Task IUserAvatars.RemoveAsync(UserId userId, CancellationToken cancellationToken)
    {
        Avatars.Remove(userId);
        return Task.CompletedTask;
    }

    public Task<User?> FindAsync(UserId id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.GetValueOrDefault(id));

    public Task<User?> FindByEmailAsync(Email email, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Values.SingleOrDefault(user => user.Email == email));

    public Task<User?> FindBySessionTokenAsync(TokenHash tokenHash, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Values.SingleOrDefault(user => user.Sessions.Any(session => session.Hash == tokenHash)));

    public void Add(User user) => _users.Add(user.Id, user);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Comme avec les vrais ports, un lien n'est consommé qu'une fois la requête enregistrée.
        if (_consuming is not null)
        {
            _tokens.Remove(_consuming);
            _consuming = null;
        }

        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<bool> IsUniqueAsync(Email email, UserId? excludedUserId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Values.All(user => user.Email != email || user.Id == excludedUserId));

    public PasswordHash Hash(string password)
    {
        HashCount++;
        return PasswordHash.Create($"salt{++_sequence}.{Hex(password)}");
    }

    public bool NeedsRehash(PasswordHash hash) => hash.Value.StartsWith(OutdatedHashPrefix, StringComparison.Ordinal);

    public Task<bool> IsCompromisedAsync(string password, CancellationToken cancellationToken = default) =>
        Task.FromResult(CompromisedPasswords.Contains(password));

    public Task ForgetAsync(UserId userId, Email previousEmail, CancellationToken cancellationToken = default)
    {
        Forgotten.Add((userId, previousEmail.Value));
        return Task.CompletedTask;
    }

    public static PasswordHash OutdatedHashOf(string password) => PasswordHash.Create($"{OutdatedHashPrefix}.{Hex(password)}");

    public bool Verify(string password, PasswordHash hash) => hash.Value.EndsWith($".{Hex(password)}", StringComparison.Ordinal);

    public RefreshToken Generate()
    {
        var value = $"refresh-{++_sequence}";
        return new RefreshToken(value, ((IRefreshTokens)this).Hash(value));
    }

    TokenHash IRefreshTokens.Hash(string token) => TokenHash.Create($"sha.{Hex(token)}");

    /// <summary>Faux lisible : <c>access.{user}.{session}</c> (+ <c>.tenant.{tenant}</c>, + <c>.till.{caisse}.{établissement}</c>).</summary>
    public AccessToken Issue(User user, SessionId sessionId, TenantId? tenantId = null, TillAccess? till = null)
    {
        var suffix = tenantId.HasValue ? ".tenant." + tenantId.Value : string.Empty;
        if (till is not null)
        {
            suffix += $".till.{till.TillId}.{till.LocationId}";
        }

        return new AccessToken($"access.{user.Id}.{sessionId}{suffix}", till?.ExpiresAtUtc ?? Clock.UtcNow.AddMinutes(15));
    }

    public Task<bool> CanActForTenantAsync(UserId userId, TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Memberships.Contains((userId, tenantId)));

    public Task<IReadOnlyList<TenantId>> SoleOwnerTenantsAsync(UserId userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TenantId>>(SoleOwnerships.Where(entry => entry.UserId == userId).Select(entry => entry.TenantId).ToList());

    public Task<string> IssueAsync(
        UserId userId, UserTokenPurpose purpose, DateTimeOffset expiresAtUtc, CancellationToken cancellationToken = default)
    {
        foreach (var previous in _tokens.Where(token => token.Value.UserId == userId && token.Value.Purpose == purpose).ToList())
        {
            _tokens.Remove(previous.Key);
        }

        var value = $"{purpose}-{++_sequence}";
        _tokens.Add(value, (userId, purpose, expiresAtUtc));
        return Task.FromResult(value);
    }

    public Task RevokeAsync(UserId userId, UserTokenPurpose purpose, CancellationToken cancellationToken = default)
    {
        foreach (var token in _tokens.Where(token => token.Value.UserId == userId && token.Value.Purpose == purpose).ToList())
        {
            _tokens.Remove(token.Key);
        }

        return Task.CompletedTask;
    }

    public Task<UserId?> ConsumeAsync(
        string? token, UserTokenPurpose purpose, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        _consuming = null;
        if (token is null
            || !_tokens.TryGetValue(token, out var entry)
            || entry.Purpose != purpose
            || nowUtc >= entry.ExpiresAtUtc)
        {
            return Task.FromResult<UserId?>(null);
        }

        _consuming = token;
        return Task.FromResult<UserId?>(entry.UserId);
    }

    public Task<DateTimeOffset?> BlockedUntilAsync(
        Email account, DateTimeOffset nowUtc, int maxFailures, TimeSpan window, CancellationToken cancellationToken = default) =>
        Task.FromResult<DateTimeOffset?>(
            _attempts.TryGetValue(account.Value, out var entry) && entry.StartedAtUtc > nowUtc - window && entry.Failures >= maxFailures
                ? entry.StartedAtUtc + window
                : null);

    public Task RecordFailureAsync(Email account, DateTimeOffset nowUtc, TimeSpan window, CancellationToken cancellationToken = default)
    {
        _attempts[account.Value] = _attempts.TryGetValue(account.Value, out var entry) && entry.StartedAtUtc > nowUtc - window
            ? (entry.Failures + 1, entry.StartedAtUtc)
            : (1, nowUtc);
        return Task.CompletedTask;
    }

    public Task ResetAsync(Email account, CancellationToken cancellationToken = default)
    {
        _attempts.Remove(account.Value);
        return Task.CompletedTask;
    }

    public int FailuresOf(string email) => _attempts.TryGetValue(email, out var entry) ? entry.Failures : 0;

    // Compteur 2FA à part : même logique, autre clé.
    Task<DateTimeOffset?> ITwoFactorAttempts.BlockedUntilAsync(
        Email account, DateTimeOffset nowUtc, int maxFailures, TimeSpan window, CancellationToken cancellationToken) =>
        BlockedUntilAsync(TwoFactorKey(account), nowUtc, maxFailures, window, cancellationToken);

    Task ITwoFactorAttempts.RecordFailureAsync(Email account, DateTimeOffset nowUtc, TimeSpan window, CancellationToken cancellationToken) =>
        RecordFailureAsync(TwoFactorKey(account), nowUtc, window, cancellationToken);

    Task ITwoFactorAttempts.ResetAsync(Email account, CancellationToken cancellationToken) => ResetAsync(TwoFactorKey(account), cancellationToken);

    public int TwoFactorFailuresOf(string email) => FailuresOf("2fa." + email);

    private static Email TwoFactorKey(Email account) => Email.Create("2fa." + account.Value);

    public Task SendEmailVerificationAsync(Email email, string token, CancellationToken cancellationToken = default) =>
        Record("verification", email, token);

    public Task SendInvitationAsync(Email email, string token, CancellationToken cancellationToken = default) =>
        Record("invitation", email, token);

    public Task SendPasswordResetAsync(Email email, string token, CancellationToken cancellationToken = default) =>
        Record("reset", email, token);

    private Task Record(string kind, Email email, string token)
    {
        Sent.Add((kind, email.Value, token));
        return Task.CompletedTask;
    }

    private static string Hex(string value) => Convert.ToHexString(Encoding.UTF8.GetBytes(value));
}
