using Identity.Application.Abstractions;

namespace Identity.Application;

/// <summary>Règles sur le mot de passe en clair, vérifiées avant hachage. Le domaine ne voit jamais que le hash.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    // Borne haute : le hachage est volontairement lent, une entrée non bornée est un déni de service.
    public const int MaxLength = 128;

    public static string Check(string? password)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(password), IdentityApplicationErrors.PasswordEmpty);
        DomainException.ThrowIf(password!.Length < MinLength, IdentityApplicationErrors.PasswordTooShort);
        DomainException.ThrowIf(password.Length > MaxLength, IdentityApplicationErrors.PasswordTooLong);
        return password;
    }

    /// <summary>Les règles ci-dessus, puis la liste des mots de passe connus par des fuites de données.</summary>
    public static async Task<string> CheckAsync(
        string? password, ICompromisedPasswords compromised, CancellationToken cancellationToken = default)
    {
        var checkedPassword = Check(password);
        DomainException.ThrowIf(
            await compromised.IsCompromisedAsync(checkedPassword, cancellationToken), IdentityApplicationErrors.PasswordCompromised);
        return checkedPassword;
    }
}
