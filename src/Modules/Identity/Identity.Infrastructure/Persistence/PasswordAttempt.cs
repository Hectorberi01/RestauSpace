namespace Identity.Infrastructure.Persistence;

/// <summary>Mots de passe erronés pour une adresse (le compte peut ne pas exister) dans la fenêtre en cours.</summary>
public sealed class PasswordAttempt
{
    private PasswordAttempt()
    {
        Email = null!;
    }

    public string Email { get; private set; }

    public int Failures { get; private set; }

    public DateTimeOffset WindowStartedAtUtc { get; private set; }
}
