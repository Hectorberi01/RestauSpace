namespace Reporting.Infrastructure;

/// <summary>Réglages techniques du module (section <c>Reporting</c> de l'hôte).</summary>
public sealed class ReportingOptions
{
    /// <summary>Faux : le job (envois planifiés, exports, expiration) ne tourne pas seul (tests, instance sans job).</summary>
    public bool RunWorkers { get; set; } = true;

    /// <summary>Intervalle entre deux passages du job.</summary>
    public TimeSpan WorkerInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Nombre maximal d'éléments traités par file et par passage.</summary>
    public int BatchSize { get; set; } = 50;
}
