namespace Identity.Application.Abstractions;

public interface IPasswordAttempts
{
    Task<DateTimeOffset?> BlockedUntilAsync(
        Email account, DateTimeOffset nowUtc, int maxFailures, TimeSpan window, CancellationToken cancellationToken = default);
    Task RecordFailureAsync(Email account, DateTimeOffset nowUtc, TimeSpan window, CancellationToken cancellationToken = default);
    Task ResetAsync(Email account, CancellationToken cancellationToken = default);
}
