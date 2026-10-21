namespace Promotions.Infrastructure;

/// <summary>Réglages techniques du module (section <c>Promotions</c> de la configuration de l'hôte).</summary>
public sealed class PromotionsOptions
{
    /// <summary>Faux : le job d'expiration ne tourne pas (tests, instance sans tâche de fond).</summary>
    public bool RunWorkers { get; set; } = true;

    /// <summary>Intervalle entre deux passages du job d'expiration.</summary>
    public TimeSpan ExpiryInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Promotions expirées par restaurant et par lot (une transaction par lot).</summary>
    public int ExpiryBatchSize { get; set; } = 200;
}
