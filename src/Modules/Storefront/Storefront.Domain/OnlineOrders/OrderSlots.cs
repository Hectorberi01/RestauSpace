namespace Storefront.Domain;

/// <summary>
/// Les créneaux de commande en ligne : des tranches de <see cref="Minutes"/> minutes, alignées sur l'heure. Une commande
/// occupe le créneau de l'heure où elle doit être prête (heure demandée, ou dès que possible).
/// </summary>
public static class OrderSlots
{
    public const int Minutes = 15;

    /// <summary>Jours proposés à l'avance, aujourd'hui compris.</summary>
    public const int Days = 7;

    /// <summary>Le début du créneau qui contient <paramref name="at"/>, en UTC.</summary>
    public static DateTimeOffset StartOf(DateTimeOffset at)
    {
        var utc = at.ToUniversalTime();
        var ticks = utc.Ticks - (utc.Ticks % TimeSpan.FromMinutes(Minutes).Ticks);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    /// <summary>Le premier créneau qui commence à <paramref name="at"/> ou après.</summary>
    public static DateTimeOffset NextFrom(DateTimeOffset at)
    {
        var start = StartOf(at);
        return start < at.ToUniversalTime() ? start.AddMinutes(Minutes) : start;
    }
}
