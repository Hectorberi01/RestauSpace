namespace SaasRestaurant.Host;

internal sealed class SystemClock : IClock
{
    // UtcNow, jamais Now : PostgreSQL ne stocke que des instants avec un décalage nul.
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
