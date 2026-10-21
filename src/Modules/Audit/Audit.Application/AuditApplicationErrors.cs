namespace Audit.Application;

/// <summary>Erreurs des cas d'usage d'Audit (celles des règles métier vivent dans Audit.Domain).</summary>
public static class AuditApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("audit.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    /// <summary>Même origine, même action, même cible : l'événement a déjà été journalisé (rejeu de l'outbox).</summary>
    public static readonly DomainError AlreadyRecorded = new("audit.entry.already_recorded", "This action is already in the audit log.");

    /// <summary>Aussi la réponse pour une écriture d'un autre restaurant : on ne révèle jamais qu'elle existe.</summary>
    public static readonly DomainError EntryNotFound = new("audit.entry.not_found", "The audit entry does not exist.");

    public static readonly DomainError PageInvalid = new("audit.page_invalid", "Skip must be zero or more and take between 1 and 100.");
    public static readonly DomainError PeriodInvalid = new("audit.period_invalid", "The start of the period must be before its end.");
    public static readonly DomainError ImportanceInvalid = new("audit.importance_invalid", "Importance is High, Medium or Low.");
    public static readonly DomainError SummaryPeriodInvalid =
        new("audit.summary_period_invalid", "A summary needs a start and an end, at most 366 days apart.");
    public static readonly DomainError ActorFilterInvalid =
        new("audit.actor_filter_invalid", "Filtering by actor needs both the actor type and the actor identifier.");
}

/// <summary>Codes de permission déclarés par le module Audit pour les restaurants (accordés via les rôles Staff).</summary>
public static class AuditPermissions
{
    /// <summary>Consulter le journal d'audit du restaurant.</summary>
    public const string View = "audit.view";

    public static IReadOnlyList<string> All { get; } = [View];
}
