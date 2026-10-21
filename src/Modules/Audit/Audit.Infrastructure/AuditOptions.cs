namespace Audit.Infrastructure;

/// <summary>Réglages techniques du module (section <c>Audit</c> de l'hôte).</summary>
public sealed class AuditOptions
{
    /// <summary>Faux : le job de rétention ne tourne pas seul (tests).</summary>
    public bool RunWorkers { get; set; } = true;

    /// <summary>
    /// Durée de conservation du journal, à fixer selon vos obligations légales. Au-delà, les écritures sont purgées par le
    /// job (seule suppression autorisée en base). <see cref="TimeSpan.Zero"/> : conservation illimitée.
    /// </summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(3 * 365);

    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(6);

    public int PurgeBatchSize { get; set; } = 5000;
}
