namespace Identity.Domain.Users;

/// <summary>Policy : durée d'utilisation d'une session de refresh après émission (glissante : chaque rotation la renouvelle).</summary>
public sealed class SessionLifetime : ValueObject
{
    public static readonly TimeSpan Maximum = TimeSpan.FromDays(365);

    private SessionLifetime(TimeSpan duration) => Duration = duration;

    public TimeSpan Duration { get; }

    public static SessionLifetime Default { get; } = new(TimeSpan.FromDays(30));

    public static SessionLifetime Create(TimeSpan duration)
    {
        DomainException.ThrowIf(duration <= TimeSpan.Zero || duration > Maximum, UserErrors.SessionLifetimeInvalid);
        return new SessionLifetime(duration);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Duration;
    }

    public override string ToString() => Duration.ToString();
}
