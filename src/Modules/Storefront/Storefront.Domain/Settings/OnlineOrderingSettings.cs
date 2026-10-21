namespace Storefront.Domain;

/// <summary>Ce que l'établissement propose en ligne : sur place, à emporter, en livraison.</summary>
public enum ServiceMode
{
    DineIn = 1,
    Takeaway = 2,
    Delivery = 3,
}

/// <summary>Une commande reçue part en cuisine d'elle-même, ou attend qu'un membre du personnel l'accepte.</summary>
public enum OrderAcceptance
{
    Automatic = 1,
    Manual = 2,
}

/// <summary>
/// La règle d'un canal (site, application, borne) : paiements acceptés (en ligne, au comptoir avec un plafond facultatif)
/// et acceptation des commandes. Au moins un moyen de paiement.
/// </summary>
public sealed class ChannelPolicy : ValueObject
{
    public const decimal MaxAmount = 100_000_000m;

    private ChannelPolicy(SalesAppKind channel, bool payOnline, bool payAtCounter, decimal? counterLimit, OrderAcceptance acceptance)
    {
        Channel = channel;
        PayOnline = payOnline;
        PayAtCounter = payAtCounter;
        CounterLimit = counterLimit;
        Acceptance = acceptance;
    }

    public SalesAppKind Channel { get; }

    public bool PayOnline { get; }

    public bool PayAtCounter { get; }

    /// <summary>Montant au-delà duquel le paiement au comptoir n'est plus proposé ; null = sans plafond.</summary>
    public decimal? CounterLimit { get; }

    public OrderAcceptance Acceptance { get; }

    public static ChannelPolicy Create(SalesAppKind channel, bool payOnline, bool payAtCounter, decimal? counterLimit, OrderAcceptance acceptance)
    {
        DomainException.ThrowIf(!Enum.IsDefined(channel), SalesAppErrors.KindInvalid);
        DomainException.ThrowIf(!Enum.IsDefined(acceptance), OnlineOrderingErrors.AcceptanceInvalid);
        DomainException.ThrowIf(!payOnline && !payAtCounter, OnlineOrderingErrors.PaymentRequired);
        DomainException.ThrowIf(counterLimit is not null && !payAtCounter, OnlineOrderingErrors.CounterLimitWithoutCounter);
        DomainException.ThrowIf(counterLimit is <= 0 or > MaxAmount, OnlineOrderingErrors.AmountInvalid);
        return new ChannelPolicy(channel, payOnline, payAtCounter, counterLimit, acceptance);
    }

    public static ChannelPolicy Default(SalesAppKind channel) => Create(channel, true, true, null, OrderAcceptance.Automatic);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Channel;
        yield return PayOnline;
        yield return PayAtCounter;
        yield return CounterLimit;
        yield return Acceptance;
    }
}

/// <summary>
/// Les réglages de vente en ligne d'UN établissement : pause (« on ne prend plus de commandes »), modes de service,
/// délai de préparation, minimum de commande, règles par canal, délai d'acceptation manuelle et d'expiration des
/// commandes impayées. Un par établissement ; tant qu'il n'existe pas, les valeurs par défaut s'appliquent.
/// </summary>
public sealed class OnlineOrderingSettings : AggregateRoot<OnlineOrderingSettingsId>, ITenantOwned
{
    public const int MinPreparationMinutes = 5;
    public const int MaxPreparationMinutes = 240;
    public const int DefaultPreparationMinutes = 20;
    public const int MinAcceptanceMinutes = 2;
    public const int MaxAcceptanceMinutes = 60;
    public const int DefaultAcceptanceMinutes = 10;
    public const int MinUnpaidExpiryMinutes = 5;
    public const int MaxUnpaidExpiryMinutes = 120;
    public const int DefaultUnpaidExpiryMinutes = 15;
    public const int MaxSlotCapacity = 500;

    private List<ServiceMode> _serviceModes = [];
    private List<ChannelPolicy> _policies = [];

    private OnlineOrderingSettings(OnlineOrderingSettingsId id, TenantId tenantId, LocationId locationId, DateTimeOffset now)
        : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        PreparationMinutes = DefaultPreparationMinutes;
        ManualAcceptanceMinutes = DefaultAcceptanceMinutes;
        UnpaidExpiryMinutes = DefaultUnpaidExpiryMinutes;
        _serviceModes = [ServiceMode.Takeaway];
        _policies = Enum.GetValues<SalesAppKind>().Select(ChannelPolicy.Default).ToList();
        UpdatedAtUtc = now;
    }

    // Réservé à l'ORM.
    private OnlineOrderingSettings()
    {
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    /// <summary>Vrai : l'établissement ne prend plus de commandes en ligne pour le moment (rush, rupture, fermeture).</summary>
    public bool IsPaused { get; private set; }

    public IReadOnlyList<ServiceMode> ServiceModes => _serviceModes.AsReadOnly();

    public int PreparationMinutes { get; private set; }

    /// <summary>Montant minimal d'une commande en ligne, dans la devise du restaurant ; null = aucun.</summary>
    public decimal? MinimumOrderAmount { get; private set; }

    public int ManualAcceptanceMinutes { get; private set; }

    public int UnpaidExpiryMinutes { get; private set; }

    public IReadOnlyList<ChannelPolicy> Policies => _policies.AsReadOnly();

    /// <summary>Nombre de commandes en ligne au plus par créneau de <see cref="OrderSlots.Minutes"/> minutes ; null = sans limite.</summary>
    public int? SlotCapacity { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Les réglages par défaut d'un établissement du restaurant (pas encore enregistrés).</summary>
    public static OnlineOrderingSettings Default(LocationReference location, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(clock);
        return new OnlineOrderingSettings(OnlineOrderingSettingsId.New(), location.TenantId, location.LocationId, clock.UtcNow);
    }

    public ChannelPolicy PolicyFor(SalesAppKind channel) =>
        _policies.Find(policy => policy.Channel == channel) ?? ChannelPolicy.Default(channel);

    public bool Offers(ServiceMode mode) => _serviceModes.Contains(mode);

    /// <summary>
    /// Remplace tous les réglages. Au moins un mode de service ; une règle par canal (les canaux omis gardent la leur) ;
    /// <paramref name="slotCapacity"/> null = pas de limite par créneau.
    /// </summary>
    public void Configure(
        bool isPaused,
        IEnumerable<ServiceMode> serviceModes,
        int preparationMinutes,
        decimal? minimumOrderAmount,
        int manualAcceptanceMinutes,
        int unpaidExpiryMinutes,
        IEnumerable<ChannelPolicy> policies,
        IClock clock,
        int? slotCapacity = null)
    {
        ArgumentNullException.ThrowIfNull(serviceModes);
        ArgumentNullException.ThrowIfNull(policies);
        ArgumentNullException.ThrowIfNull(clock);
        var modes = serviceModes.Distinct().Order().ToList();
        DomainException.ThrowIf(modes.Count == 0, OnlineOrderingErrors.ServiceModeRequired);
        DomainException.ThrowIf(modes.Any(mode => !Enum.IsDefined(mode)), OnlineOrderingErrors.ServiceModeInvalid);
        DomainException.ThrowIf(
            preparationMinutes is < MinPreparationMinutes or > MaxPreparationMinutes, OnlineOrderingErrors.PreparationInvalid);
        DomainException.ThrowIf(minimumOrderAmount is < 0 or > ChannelPolicy.MaxAmount, OnlineOrderingErrors.AmountInvalid);
        DomainException.ThrowIf(
            manualAcceptanceMinutes is < MinAcceptanceMinutes or > MaxAcceptanceMinutes, OnlineOrderingErrors.AcceptanceDelayInvalid);
        DomainException.ThrowIf(
            unpaidExpiryMinutes is < MinUnpaidExpiryMinutes or > MaxUnpaidExpiryMinutes, OnlineOrderingErrors.ExpiryInvalid);
        DomainException.ThrowIf(slotCapacity is < 1 or > MaxSlotCapacity, OnlineOrderingErrors.SlotCapacityInvalid);

        var given = policies.ToList();
        DomainException.ThrowIf(given.Any(policy => policy is null), OnlineOrderingErrors.PolicyInvalid);
        DomainException.ThrowIf(given.Select(policy => policy.Channel).Distinct().Count() != given.Count, OnlineOrderingErrors.PolicyDuplicated);
        var merged = Enum.GetValues<SalesAppKind>()
            .Select(channel => given.Find(policy => policy.Channel == channel) ?? PolicyFor(channel))
            .ToList();

        IsPaused = isPaused;
        _serviceModes = modes;
        PreparationMinutes = preparationMinutes;
        MinimumOrderAmount = minimumOrderAmount == 0 ? null : minimumOrderAmount;
        ManualAcceptanceMinutes = manualAcceptanceMinutes;
        UnpaidExpiryMinutes = unpaidExpiryMinutes;
        SlotCapacity = slotCapacity;
        _policies = merged;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new OnlineOrderingSettingsChanged(TenantId, Id, LocationId, IsPaused, UpdatedAtUtc));
    }

    /// <summary>Met en pause ou reprend la prise de commandes en ligne, sans toucher au reste.</summary>
    public void SetPaused(bool isPaused, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (isPaused == IsPaused)
        {
            return;
        }

        IsPaused = isPaused;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new OnlineOrderingSettingsChanged(TenantId, Id, LocationId, IsPaused, UpdatedAtUtc));
    }
}

public static class OnlineOrderingErrors
{
    public static readonly DomainError ServiceModeRequired = new("storefront.settings.service_mode_required", "Offer at least one service mode.");
    public static readonly DomainError ServiceModeInvalid = new("storefront.settings.service_mode_invalid", "Unknown service mode.");
    public static readonly DomainError PreparationInvalid = new("storefront.settings.preparation_invalid", "The preparation time is between 5 and 240 minutes.");
    public static readonly DomainError AmountInvalid = new("storefront.settings.amount_invalid", "The amount is not valid.");
    public static readonly DomainError AcceptanceDelayInvalid = new("storefront.settings.acceptance_delay_invalid", "The acceptance delay is between 2 and 60 minutes.");
    public static readonly DomainError ExpiryInvalid = new("storefront.settings.expiry_invalid", "The unpaid order delay is between 5 and 120 minutes.");
    public static readonly DomainError AcceptanceInvalid = new("storefront.settings.acceptance_invalid", "Unknown acceptance mode.");
    public static readonly DomainError PaymentRequired = new("storefront.settings.payment_required", "Accept at least one way to pay.");
    public static readonly DomainError CounterLimitWithoutCounter = new("storefront.settings.counter_limit_without_counter", "A counter limit needs payment at the counter.");
    public static readonly DomainError PolicyInvalid = new("storefront.settings.policy_invalid", "A channel rule is missing.");
    public static readonly DomainError SlotCapacityInvalid = new("storefront.settings.slot_capacity_invalid", "The capacity per slot is between 1 and 500 orders.");
    public static readonly DomainError PolicyDuplicated = new("storefront.settings.policy_duplicated", "One rule per channel.");
}

public sealed record OnlineOrderingSettingsChanged(
    TenantId TenantId, OnlineOrderingSettingsId SettingsId, LocationId LocationId, bool IsPaused, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);
