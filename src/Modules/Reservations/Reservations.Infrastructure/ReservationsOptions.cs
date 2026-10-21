namespace Reservations.Infrastructure;

/// <summary>Réglages techniques du module (section <c>Reservations</c> de la configuration de l'hôte).</summary>
public sealed class ReservationsOptions
{
    /// <summary>Faux : le job des échéances ne tourne pas (tests, instance sans tâche de fond).</summary>
    public bool RunWorkers { get; set; } = true;

    /// <summary>Intervalle entre deux passages : acomptes non payés à temps, offres de la liste d'attente expirées.</summary>
    public TimeSpan DueInterval { get; set; } = TimeSpan.FromMinutes(1);

    public int DueBatchSize { get; set; } = 200;
}
