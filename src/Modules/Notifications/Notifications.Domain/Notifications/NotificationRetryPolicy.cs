namespace Notifications.Domain;

/// <summary>
/// Au plus <see cref="MaxAttempts"/> tentatives d'envoi au total (la première comprise) ; le délai d'attente double
/// après chaque échec, plafonné à <see cref="MaxBackoff"/>. Fixée sur une notification à sa création.
/// </summary>
public sealed class NotificationRetryPolicy : ValueObject
{
    public const int MaxAllowedAttempts = 20;

    /// <summary>Borne supérieure de <see cref="MaxBackoff"/> : tient le doublement très loin de tout dépassement.</summary>
    public static readonly TimeSpan MaxAllowedBackoff = TimeSpan.FromDays(7);

    private NotificationRetryPolicy(int maxAttempts, TimeSpan initialBackoff, TimeSpan maxBackoff)
    {
        MaxAttempts = maxAttempts;
        InitialBackoff = initialBackoff;
        MaxBackoff = maxBackoff;
    }

    /// <summary>3 tentatives ; 1 min puis 2 min entre elles (plafond : 30 min).</summary>
    public static NotificationRetryPolicy Default { get; } = Create(3, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30));

    public int MaxAttempts { get; }

    public TimeSpan InitialBackoff { get; }

    public TimeSpan MaxBackoff { get; }

    public static NotificationRetryPolicy Create(int maxAttempts, TimeSpan initialBackoff, TimeSpan maxBackoff)
    {
        DomainException.ThrowIf(
            maxAttempts is < 1 or > MaxAllowedAttempts
            || initialBackoff <= TimeSpan.Zero
            || maxBackoff < initialBackoff
            || maxBackoff > MaxAllowedBackoff,
            NotificationErrors.RetryPolicyInvalid);

        return new NotificationRetryPolicy(maxAttempts, initialBackoff, maxBackoff);
    }

    public bool AllowsAttemptAfter(int attemptCount) => attemptCount < MaxAttempts;

    /// <summary>Délai à attendre après l'échec de la tentative n° <paramref name="failedAttemptNumber"/> (base 1).</summary>
    public TimeSpan BackoffAfter(int failedAttemptNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttemptNumber, 1);

        var backoff = InitialBackoff;
        for (var attempt = 1; attempt < failedAttemptNumber && backoff < MaxBackoff; attempt++)
        {
            backoff += backoff;
        }

        return backoff < MaxBackoff ? backoff : MaxBackoff;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MaxAttempts;
        yield return InitialBackoff;
        yield return MaxBackoff;
    }
}
