using Audit.Domain;

namespace Audit.Application;

/// <summary>
/// L'importance d'une écriture, déduite de son action : <see cref="High"/> pour ce qui touche aux accès, à l'argent, aux
/// secrets ou à l'existence du compte ; <see cref="Low"/> pour la routine (connexions, préférences, exports) ;
/// <see cref="Medium"/> pour le reste. Une décision du produit, pas une donnée enregistrée : la changer reclasse tout
/// l'historique.
/// </summary>
public static class AuditImportance
{
    public const string High = "High";
    public const string Medium = "Medium";
    public const string Low = "Low";

    public static IReadOnlyList<string> All { get; } = [High, Medium, Low];

    public static IReadOnlySet<string> HighActions { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "tenants.tenant.suspended", "tenants.tenant.closed", "tenants.tenant.currency_changed", "tenants.tenant.price_tax_mode_changed",
        "staff.member.suspended", "staff.member.left", "staff.member.owner_created",
        "staff.role.permission_granted", "staff.role.permission_revoked",
        "identity.session.replay_detected", "identity.user.locked", "identity.user.disabled", "identity.user.erased",
        "identity.user.email_changed", "identity.user.password_changed",
        "payments.configuration.enabled", "payments.configuration.disabled", "payments.configuration.mode_changed",
        "payments.refund.requested", "payments.refund.registered", "payments.refund.succeeded", "payments.payment.captured_after_cancellation",
        "customers.customer.anonymized",
        "integrations.api_key.issued", "integrations.api_key.rotated", "integrations.api_key.revoked",
        "integrations.webhook_endpoint.auto_disabled", "integrations.webhook_secret.rotated",
        "billing.subscription.past_due", "billing.subscription.suspended", "billing.subscription.cancelled",
        "billing.subscription.cancellation_scheduled", "billing.subscription.plan_changed",
        "backups.backup.download_link_created", "backups.backup.deletion_requested", "backups.restore.requested", "backups.restore.cancelled",
    };

    public static IReadOnlySet<string> LowActions { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "identity.session.started", "identity.session.revoked", "identity.user.profile_changed", "identity.user.preferences_changed",
        "identity.user.avatar_changed", "identity.user.email_verified",
        "reporting.export.requested", "reviews.feedback.reviewed", "reviews.feedback.archived",
    };

    public static string Of(string action) =>
        HighActions.Contains(action) ? High : LowActions.Contains(action) ? Low : Medium;

    /// <summary>
    /// Le filtre du dépôt pour une importance : les actions à garder (Haute, Basse) ou à écarter (Moyenne : tout sauf
    /// les deux listes). Une valeur inconnue est refusée.
    /// </summary>
    internal static (IReadOnlyCollection<AuditAction>? Only, IReadOnlyCollection<AuditAction>? Except) ToFilter(string? importance)
    {
        if (string.IsNullOrWhiteSpace(importance))
        {
            return (null, null);
        }

        static IReadOnlyCollection<AuditAction> Codes(IEnumerable<string> actions) => actions.Select(AuditAction.Create).ToList();
        return importance.Trim() switch
        {
            var value when string.Equals(value, High, StringComparison.OrdinalIgnoreCase) => (Codes(HighActions), null),
            var value when string.Equals(value, Low, StringComparison.OrdinalIgnoreCase) => (Codes(LowActions), null),
            var value when string.Equals(value, Medium, StringComparison.OrdinalIgnoreCase) => (null, Codes(HighActions.Concat(LowActions))),
            _ => throw new DomainException(AuditApplicationErrors.ImportanceInvalid),
        };
    }
}
