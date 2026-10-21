using Identity.Application.Abstractions;
using Identity.Domain.Users;

namespace Identity.Application.Commands;

// Validation en deux étapes (TOTP) de l'utilisateur connecté : préparation avec le mot de passe, activation par un
// premier code juste (codes de secours remis une fois), retrait avec le mot de passe et un code.

/// <summary>Le secret à saisir ou scanner dans l'application d'authentification : montré à l'utilisateur seul, une fois.</summary>
public sealed record TwoFactorSetupDto(string Secret, string OtpAuthUri);

public sealed record TwoFactorRecoveryCodesDto(IReadOnlyList<string> Codes);

public sealed record StartTwoFactorSetupCommand(Guid UserId, string? Password) : ICommand<TwoFactorSetupDto>
{
    public override string ToString() => $"StartTwoFactorSetupCommand ({UserId}, ***)";
}

public sealed class StartTwoFactorSetupHandler(
    IUserRepository users, PasswordVerifier passwords, ITwoFactorSecrets secrets, IIdentityUnitOfWork unitOfWork, IdentitySettings settings)
    : ICommandHandler<StartTwoFactorSetupCommand, TwoFactorSetupDto>
{
    public async Task<TwoFactorSetupDto> HandleAsync(StartTwoFactorSetupCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        DomainException.ThrowIf(!secrets.IsConfigured, IdentityApplicationErrors.TwoFactorUnavailable);
        var user = await users.FindAsync(UserId.From(command.UserId), cancellationToken)
            ?? throw new NotFoundException(IdentityApplicationErrors.NotFound);
        await passwords.EnsureAsync(user.Email, user.PasswordHash, command.Password, cancellationToken);

        var secret = TwoFactorCodes.NewSecret();
        user.BeginTwoFactorSetup(secrets.Protect(secret));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var base32 = TwoFactorCodes.Base32(secret);
        return new TwoFactorSetupDto(base32, TwoFactorCodes.OtpAuthUri(settings.TwoFactorIssuer, user.Email.Value, base32));
    }
}

public sealed record ConfirmTwoFactorCommand(Guid UserId, string? Code) : ICommand<TwoFactorRecoveryCodesDto>
{
    public override string ToString() => $"ConfirmTwoFactorCommand ({UserId}, ***)";
}

public sealed class ConfirmTwoFactorHandler(IUserRepository users, ITwoFactorSecrets secrets, IIdentityUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<ConfirmTwoFactorCommand, TwoFactorRecoveryCodesDto>
{
    public async Task<TwoFactorRecoveryCodesDto> HandleAsync(ConfirmTwoFactorCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = await users.FindAsync(UserId.From(command.UserId), cancellationToken)
            ?? throw new NotFoundException(IdentityApplicationErrors.NotFound);
        DomainException.ThrowIf(user.IsTwoFactorEnabled, UserErrors.TwoFactorAlreadyEnabled);
        var pending = user.PendingTwoFactorSecret ?? throw new DomainException(UserErrors.TwoFactorSetupMissing);
        var step = TwoFactorCodes.Verify(secrets.Unprotect(pending), command.Code, clock.UtcNow)
            ?? throw new DomainException(IdentityApplicationErrors.TwoFactorCodeInvalid);

        var codes = TwoFactorCodes.NewRecoveryCodes(User.RecoveryCodeCount);
        user.EnableTwoFactor(step, codes.Select(TwoFactorCodes.HashRecoveryCode), clock);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new TwoFactorRecoveryCodesDto(codes);
    }
}

/// <summary>Retire la validation en deux étapes : mot de passe ET code (de l'application ou de secours).</summary>
public sealed record DisableTwoFactorCommand(Guid UserId, string? Password, string? Code, Guid? CurrentSessionId = null) : ICommand
{
    public override string ToString() => $"DisableTwoFactorCommand ({UserId}, ***)";
}

public sealed class DisableTwoFactorHandler(
    IUserRepository users, PasswordVerifier passwords, TwoFactorVerifier codes, IIdentityUnitOfWork unitOfWork, IClock clock)
    : ICommandHandler<DisableTwoFactorCommand>
{
    public async Task HandleAsync(DisableTwoFactorCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var user = await users.FindAsync(UserId.From(command.UserId), cancellationToken)
            ?? throw new NotFoundException(IdentityApplicationErrors.NotFound);
        await passwords.EnsureAsync(user.Email, user.PasswordHash, command.Password, cancellationToken);
        await codes.EnsureAsync(user, command.Code, cancellationToken);

        var keep = user.Sessions.SingleOrDefault(session => session.Id.Value == command.CurrentSessionId && session.IsUsableAt(clock.UtcNow))?.Id;
        user.DisableTwoFactor(clock, keep);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Vérifie un code de l'application (une seule fois par pas de temps) ou un code de secours (une seule fois). Les
/// échecs comptent avec ceux du mot de passe du même compte : au-delà, le compte est bloqué un moment.
/// </summary>
public sealed class TwoFactorVerifier(ITwoFactorSecrets secrets, IPasswordAttempts attempts, IdentitySettings settings, IClock clock)
{
    public async Task EnsureAsync(User user, string? code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        DomainException.ThrowIf(!user.IsTwoFactorEnabled, UserErrors.TwoFactorNotEnabled);
        var now = clock.UtcNow;
        var blockedUntil = await attempts.BlockedUntilAsync(
            user.Email, now, settings.MaxFailedPasswordAttempts, settings.FailedPasswordAttemptWindow, cancellationToken);
        if (blockedUntil is { } until)
        {
            throw new TooManyRequestsException(IdentityApplicationErrors.TooManyAttempts, until - now);
        }

        var value = (code ?? string.Empty).Trim();
        if (value.Any(char.IsAsciiLetter))
        {
            var hash = TwoFactorCodes.HashRecoveryCode(value);
            try
            {
                user.UseRecoveryCode(hash, clock);
            }
            catch (DomainException)
            {
                await attempts.RecordFailureAsync(user.Email, now, settings.FailedPasswordAttemptWindow, cancellationToken);
                throw new UnauthenticatedException(IdentityApplicationErrors.TwoFactorCodeInvalid);
            }

            return;
        }

        if (TwoFactorCodes.Verify(secrets.Unprotect(user.TwoFactorSecret!), value, now) is not { } step)
        {
            await attempts.RecordFailureAsync(user.Email, now, settings.FailedPasswordAttemptWindow, cancellationToken);
            throw new UnauthenticatedException(IdentityApplicationErrors.TwoFactorCodeInvalid);
        }

        user.UseTwoFactorStep(step);
        await attempts.ResetAsync(user.Email, cancellationToken);
    }
}

/// <summary>Seconde étape de la connexion : le jeton de la première étape et le code ouvrent la session.</summary>
public sealed record CompleteTwoFactorLoginCommand(string? Challenge, string? Code, string? Device = null) : ICommand<AuthTokensDto>
{
    public override string ToString() => "CompleteTwoFactorLoginCommand (***)";
}

public sealed class CompleteTwoFactorLoginHandler(
    IUserRepository users,
    ITwoFactorChallenges challenges,
    TwoFactorVerifier codes,
    SessionOpener sessions,
    IIdentityUnitOfWork unitOfWork,
    IClock clock)
    : ICommandHandler<CompleteTwoFactorLoginCommand, AuthTokensDto>
{
    public async Task<AuthTokensDto> HandleAsync(CompleteTwoFactorLoginCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var userId = challenges.Read(command.Challenge, clock.UtcNow)
            ?? throw new UnauthenticatedException(IdentityApplicationErrors.TwoFactorChallengeInvalid);
        var user = await users.FindAsync(userId, cancellationToken)
            ?? throw new UnauthenticatedException(IdentityApplicationErrors.TwoFactorChallengeInvalid);
        DomainException.ThrowIf(!user.IsTwoFactorEnabled, IdentityApplicationErrors.TwoFactorChallengeInvalid);

        await codes.EnsureAsync(user, command.Code, cancellationToken);
        var tokens = sessions.Open(user, command.Device, upgradedHash: null);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return tokens;
    }
}
