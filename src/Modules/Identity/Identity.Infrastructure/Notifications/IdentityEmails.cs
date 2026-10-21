namespace Identity.Infrastructure.Notifications;

/// <summary>Textes des emails, en français, en texte brut.</summary>
internal static class IdentityEmails
{
    public static (string Subject, string Body) Verification(IdentityEmailOptions options, string link) => (
        $"{options.ProductName} : confirmez votre adresse email",
        $"""
        Bonjour,

        Pour confirmer cette adresse email et activer votre compte {options.ProductName}, ouvrez ce lien puis saisissez votre mot de passe :

        {link}

        Ce lien est valable 24 heures.

        Si vous n'avez pas créé de compte, ignorez ce message : sans votre mot de passe, personne ne peut activer ce compte. Si vous souhaitez utiliser cette adresse, choisissez « Mot de passe oublié » sur la page de connexion.
        """);

    public static (string Subject, string Body) Invitation(IdentityEmailOptions options, string link) => (
        $"Vous êtes invité(e) sur {options.ProductName}",
        $"""
        Bonjour,

        Vous avez été invité(e) à rejoindre {options.ProductName}. Pour accepter et choisir votre mot de passe, ouvrez ce lien :

        {link}

        Ce lien est valable 7 jours.
        """);

    public static (string Subject, string Body) PasswordReset(IdentityEmailOptions options, string link) => (
        $"{options.ProductName} : réinitialisation de votre mot de passe",
        $"""
        Bonjour,

        Pour choisir un nouveau mot de passe, ouvrez ce lien :

        {link}

        Ce lien est valable 1 heure et ne peut servir qu'une fois.

        Si vous n'êtes pas à l'origine de cette demande, ignorez ce message : votre mot de passe reste inchangé.
        """);

    public static (string Subject, string Body) PasswordChanged(string productName) => (
        $"{productName} : votre mot de passe a été modifié",
        $"""
        Bonjour,

        Le mot de passe de votre compte {productName} vient d'être modifié.

        Si ce n'est pas vous, choisissez « Mot de passe oublié » sur la page de connexion pour reprendre la main, puis contactez le support.
        """);

    public static (string Subject, string Body) EmailChanged(string productName, string newEmail) => (
        $"{productName} : l'adresse email de votre compte a changé",
        $"""
        Bonjour,

        L'adresse email de votre compte {productName} a été remplacée par {Mask(newEmail)}. Cette adresse-ci n'y est plus associée.

        Si ce n'est pas vous, contactez le support sans attendre.
        """);

    // "chef@example.com" -> "c***@example.com" : assez pour reconnaître son adresse, pas pour découvrir celle d'autrui.
    private static string Mask(string email)
    {
        var at = email.IndexOf('@');
        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }
}
