using System.Collections.Frozen;

namespace Notifications.Domain;

/// <summary>
/// Un type de notification que la plateforme sait produire : sa clé (celle des templates), sa finalité (transactionnelle
/// ou marketing : une autorisation transactionnelle n'implique JAMAIS une autorisation marketing), sa priorité par défaut
/// et les SEULES variables qu'un template de ce type peut utiliser. Défini dans <see cref="NotificationTypes"/>.
/// </summary>
public sealed class NotificationTypeDefinition
{
    internal NotificationTypeDefinition(
        TemplateKey key, NotificationPurpose purpose, NotificationPriority priority, IEnumerable<string> variables, bool isEssential = false)
    {
        Key = key;
        IsEssential = isEssential;
        Purpose = purpose;
        Priority = priority;
        OrderedVariables = variables.Distinct(StringComparer.Ordinal).ToList().AsReadOnly();
        Variables = OrderedVariables.ToFrozenSet(StringComparer.Ordinal);
    }

    public TemplateKey Key { get; }

    public NotificationPurpose Purpose { get; }

    public NotificationPriority Priority { get; }

    /// <summary>Variables autorisées (<c>customer.firstName</c>...), sensibles à la casse.</summary>
    public FrozenSet<string> Variables { get; }

    /// <summary>Les mêmes variables, dans l'ordre du catalogue (affichage).</summary>
    public IReadOnlyList<string> OrderedVariables { get; }

    /// <summary>
    /// Destinataire : <c>customer</c> (modèles personnalisables par le restaurant), <c>account</c> (comptes et
    /// sécurité) ou <c>staff</c> (équipe du restaurant) ; ces deux derniers restent des modèles de la plateforme.
    /// </summary>
    public string Audience => Key.Value.StartsWith("staff.", StringComparison.Ordinal) ? "staff"
        : Key.Value.StartsWith("account.", StringComparison.Ordinal) || Key.Value.StartsWith("security.", StringComparison.Ordinal) ? "account"
        : "customer";

    public bool IsMarketing => Purpose == NotificationPurpose.Marketing;

    /// <summary>
    /// Message de sécurité du compte (vérification, réinitialisation, OTP) : envoyé même à une personne désinscrite du
    /// canal, faute de quoi elle perdrait l'accès à son compte. Toujours transactionnel.
    /// </summary>
    public bool IsEssential { get; }

    public bool Allows(string variable) => Variables.Contains(variable);

    public override string ToString() => Key.Value;
}
