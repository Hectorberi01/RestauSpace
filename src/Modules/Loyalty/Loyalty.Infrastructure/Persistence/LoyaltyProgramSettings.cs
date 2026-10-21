using Loyalty.Domain;

namespace Loyalty.Infrastructure.Persistence;

/// <summary>
/// Le barème d'un restaurant (une ligne par tenant, absente tant que rien n'est réglé : <see cref="LoyaltyProgram.Default"/>).
/// Relu par <see cref="LoyaltyProgram.Create"/> : une ligne altérée à la main hors des bornes est refusée.
/// </summary>
internal sealed class LoyaltyProgramSettings : ITenantOwned
{
    public TenantId TenantId { get; set; }

    public bool IsEnabled { get; set; }

    public decimal PointsPerCurrencyUnit { get; set; }

    public bool AutoEnroll { get; set; }

    public int? InactivityExpiryDays { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public LoyaltyProgram ToProgram() => LoyaltyProgram.Create(IsEnabled, PointsPerCurrencyUnit, AutoEnroll, InactivityExpiryDays);

    /// <summary>Vrai si la ligne a changé.</summary>
    public bool Apply(LoyaltyProgram program, DateTimeOffset updatedAtUtc)
    {
        if (program == ToProgram())
        {
            return false;
        }

        IsEnabled = program.IsEnabled;
        PointsPerCurrencyUnit = program.PointsPerCurrencyUnit;
        AutoEnroll = program.AutoEnroll;
        InactivityExpiryDays = program.InactivityExpiryDays;
        UpdatedAtUtc = updatedAtUtc;
        return true;
    }
}
