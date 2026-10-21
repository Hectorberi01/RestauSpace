using Reservations.Domain;

namespace Reservations.Infrastructure.Persistence;

/// <summary>
/// Forme stockée de la <see cref="ReservationPolicy"/> d'un établissement (value object du domaine, sans identité) :
/// une ligne par (tenant, établissement). Relue via <see cref="ReservationPolicy.Create"/> : une valeur stockée hors des
/// bornes du domaine échoue bruyamment au lieu de se charger.
/// </summary>
internal sealed class ReservationPolicyRow : ITenantOwned
{
    public TenantId TenantId { get; set; }

    public LocationId LocationId { get; set; }

    public TimeSpan MinLeadTime { get; set; }

    public TimeSpan MaxAdvance { get; set; }

    public TimeSpan DefaultDuration { get; set; }

    public TimeSpan CancellationCutoff { get; set; }

    public TimeSpan MinDuration { get; set; }

    public TimeSpan MaxDuration { get; set; }

    public int MaxPartySize { get; set; }

    public bool AcceptsOnlineBookings { get; set; }

    public DepositMode DepositMode { get; set; }

    public DepositBasis DepositBasis { get; set; } = DepositBasis.PerGuest;

    public decimal? DepositAmount { get; set; }

    public string? DepositCurrency { get; set; }

    public int DepositMinPartySize { get; set; } = 1;

    public TimeSpan DepositPaymentWindow { get; set; } = DepositRule.DefaultPaymentWindow;

    public bool WaitlistAcceptsOnline { get; set; }

    public TimeSpan WaitlistOfferWindow { get; set; } = WaitlistSettings.DefaultOfferWindow;

    public DepositRule ToDepositRule() => DepositMode == DepositMode.Off
        ? DepositRule.Off
        : DepositRule.Create(
            DepositMode, DepositBasis, DepositAmount is { } amount ? Money.Of(amount, Currency.FromCode(DepositCurrency!)) : null,
            DepositMinPartySize, DepositPaymentWindow);

    public void CopyFrom(DepositRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        DepositMode = rule.Mode;
        DepositBasis = rule.Basis;
        DepositAmount = rule.Amount?.Amount;
        DepositCurrency = rule.Amount?.Currency.Code;
        DepositMinPartySize = rule.MinPartySize;
        DepositPaymentWindow = rule.PaymentWindow;
    }

    public WaitlistSettings ToWaitlistSettings() => WaitlistSettings.Create(WaitlistAcceptsOnline, WaitlistOfferWindow);

    public void CopyFrom(WaitlistSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        WaitlistAcceptsOnline = settings.AcceptsOnline;
        WaitlistOfferWindow = settings.OfferWindow;
    }

    public ReservationPolicy ToPolicy() => ReservationPolicy.Create(
        MinLeadTime, MaxAdvance, DefaultDuration, CancellationCutoff, MinDuration, MaxDuration, MaxPartySize, AcceptsOnlineBookings);

    public void CopyFrom(ReservationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        MinLeadTime = policy.MinLeadTime;
        MaxAdvance = policy.MaxAdvance;
        DefaultDuration = policy.DefaultDuration;
        CancellationCutoff = policy.CancellationCutoff;
        MinDuration = policy.MinDuration;
        MaxDuration = policy.MaxDuration;
        MaxPartySize = policy.MaxPartySize;
        AcceptsOnlineBookings = policy.AcceptsOnlineBookings;
    }
}
