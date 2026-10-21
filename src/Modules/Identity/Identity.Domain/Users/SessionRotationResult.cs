using System.Diagnostics.CodeAnalysis;

namespace Identity.Domain.Users;

public sealed class SessionRotationResult
{
    private SessionRotationResult(RefreshSession? newSession) => NewSession = newSession;

    public RefreshSession? NewSession { get; }

    [MemberNotNullWhen(false, nameof(NewSession))]
    [MemberNotNullWhen(true, nameof(Error))]
    public bool ReplayDetected => NewSession is null;

    public DomainError? Error => NewSession is null ? UserErrors.RefreshTokenReplayed : null;

    internal static SessionRotationResult Rotated(RefreshSession newSession) => new(newSession);

    internal static SessionRotationResult Replay() => new(null);
}
