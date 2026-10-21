namespace Reviews.Application;

/// <summary>Codes de permission déclarés par le module Reviews (accordés via les rôles Staff, ou en scope d'une clé d'API).</summary>
public static class ReviewsPermissions
{
    /// <summary>Lire les avis (commentaires compris : parole du client) et leurs statistiques, et les questionnaires.</summary>
    public const string View = "reviews.view";

    /// <summary>Enregistrer l'avis d'un client (tablette en salle, application du restaurant par clé d'API).</summary>
    public const string Submit = "reviews.submit";

    /// <summary>Traiter les avis (examiné, archivé) et gérer les questionnaires.</summary>
    public const string Manage = "reviews.manage";

    public static IReadOnlyList<string> All { get; } = [View, Submit, Manage];
}
