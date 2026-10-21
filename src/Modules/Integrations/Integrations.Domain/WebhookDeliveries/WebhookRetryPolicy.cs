namespace Integrations.Domain;

/// <summary>
/// Nouvelles tentatives bornées : au plus <see cref="MaxAttempts"/> tentatives au total (la première comprise),
/// espacées d'un backoff exponentiel (le délai double après chaque échec) plafonné à <see cref="MaxDelay"/>, lui-même
/// borné par <see cref="MaxAllowedDelay"/> pour que le calcul du backoff ne puisse jamais déborder.
/// </summary>
public sealed class WebhookRetryPolicy : ValueObject
{
    public const int MaxAllowedAttempts = 50;

    public static readonly TimeSpan MaxAllowedDelay = TimeSpan.FromDays(7);

    private WebhookRetryPolicy(int maxAttempts, TimeSpan initialDelay, TimeSpan maxDelay)
    {
        MaxAttempts = maxAttempts;
        InitialDelay = initialDelay;
        MaxDelay = maxDelay;
    }

    /// <summary>6 tentatives ; 30 s, 1 min, 2 min, 4 min, 8 min entre elles (plafond : 1 h).</summary>
    public static WebhookRetryPolicy Default { get; } = Create(6, TimeSpan.FromSeconds(30), TimeSpan.FromHours(1));

    public int MaxAttempts { get; }

    public TimeSpan InitialDelay { get; }

    public TimeSpan MaxDelay { get; }

    public static WebhookRetryPolicy Create(int maxAttempts, TimeSpan initialDelay, TimeSpan maxDelay)
    {
        DomainException.ThrowIf(
            maxAttempts is < 1 or > MaxAllowedAttempts
            || initialDelay <= TimeSpan.Zero
            || maxDelay < initialDelay
            || maxDelay > MaxAllowedDelay,
            WebhookDeliveryErrors.RetryPolicyInvalid);

        return new WebhookRetryPolicy(maxAttempts, initialDelay, maxDelay);
    }

    public bool AllowsAttemptAfter(int attemptCount) => attemptCount < MaxAttempts;

    /// <summary>Délai d'attente après l'échec de la tentative n° <paramref name="failedAttemptNumber"/> (base 1).</summary>
    public TimeSpan DelayAfter(int failedAttemptNumber)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttemptNumber, 1);

        var delay = InitialDelay;
        for (var attempt = 1; attempt < failedAttemptNumber && delay < MaxDelay; attempt++)
        {
            delay += delay;
        }

        return delay < MaxDelay ? delay : MaxDelay;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MaxAttempts;
        yield return InitialDelay;
        yield return MaxDelay;
    }
}
