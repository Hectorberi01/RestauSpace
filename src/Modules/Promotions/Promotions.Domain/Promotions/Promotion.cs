namespace Promotions.Domain;

public sealed class Promotion : AggregateRoot<PromotionId>, ITenantOwned
{
    public const int MaxNameLength = 120;

    private Promotion(PromotionId id, TenantId tenantId, string name, PromotionRules rules, DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Rules = rules;
        Status = PromotionStatus.Draft;
        CreatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private Promotion()
    {
        Name = null!;
        Rules = null!;
    }

    public TenantId TenantId { get; }

    public string Name { get; private set; }

    public PromotionRules Rules { get; private set; }

    public PromotionStatus Status { get; private set; }

    public int UsageCount { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? ExpiredAtUtc { get; private set; }

    public Period ValidityPeriod => Rules.ValidityPeriod;

    public Conditions Conditions => Rules.Conditions;

    public Benefit? Benefit => Rules.Benefit;

    public UsageLimit? UsageLimit => Rules.UsageLimit;

    public bool HasBeenUsed => UsageCount > 0;

    public bool IsUsageLimitReached => UsageLimit is not null && UsageCount >= UsageLimit.Value;

    public static Promotion Create(TenantId tenantId, string name, PromotionRules rules, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);

        var trimmed = name?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed), PromotionErrors.NameRequired);
        DomainException.ThrowIf(trimmed!.Length > MaxNameLength, PromotionErrors.NameTooLong);

        var now = clock.UtcNow;
        var promotion = new Promotion(PromotionId.New(), tenantId, trimmed, rules, now);
        promotion.Raise(new PromotionCreated(tenantId, promotion.Id, trimmed, now));
        return promotion;
    }

    public void Activate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotExpired();
        DomainException.ThrowIf(Status != PromotionStatus.Draft, PromotionErrors.NotDraft);
        DomainException.ThrowIf(!Rules.IsComplete, PromotionErrors.RulesIncomplete);
        var now = clock.UtcNow;
        DomainException.ThrowIf(ValidityPeriod.HasEndedAt(now), PromotionErrors.PeriodEnded);

        Status = PromotionStatus.Active;
        Raise(new PromotionActivated(TenantId, Id, ValidityPeriod, now));
    }

    public void Pause(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotExpired();
        DomainException.ThrowIf(Status != PromotionStatus.Active, PromotionErrors.NotActive);

        Status = PromotionStatus.Paused;
        Raise(new PromotionPaused(TenantId, Id, clock.UtcNow));
    }

    public void Resume(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotExpired();
        DomainException.ThrowIf(Status != PromotionStatus.Paused, PromotionErrors.NotPaused);
        var now = clock.UtcNow;
        DomainException.ThrowIf(ValidityPeriod.HasEndedAt(now), PromotionErrors.PeriodEnded);

        Status = PromotionStatus.Active;
        Raise(new PromotionResumed(TenantId, Id, now));
    }

    /// <summary>Termine la promotion pour de bon, que sa période soit échue ou que le tenant l'arrête plus tôt.</summary>
    public void Expire(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotExpired();

        var now = clock.UtcNow;
        Status = PromotionStatus.Expired;
        ExpiredAtUtc = now;
        Raise(new PromotionExpired(TenantId, Id, UsageCount, now));
    }

    public void ChangeRules(PromotionRules newRules, IClock clock, RuleChangePolicy policy = RuleChangePolicy.NonDestructiveOnly)
    {
        ArgumentNullException.ThrowIfNull(newRules);
        ArgumentNullException.ThrowIfNull(clock);
        // Vérifié d'abord, quel que soit l'état : une policy inconnue ne doit jamais passer car rien ne la lit encore.
        DomainException.ThrowIf(!Enum.IsDefined(policy), PromotionErrors.RuleChangePolicyUnknown);
        EnsureNotExpired();
        if (newRules == Rules)
        {
            return;
        }

        var now = clock.UtcNow;
        if (Status != PromotionStatus.Draft)
        {
            DomainException.ThrowIf(!newRules.IsComplete, PromotionErrors.RulesIncomplete);
            DomainException.ThrowIf(newRules.ValidityPeriod.HasEndedAt(now), PromotionErrors.PeriodEnded);
        }

        if (HasBeenUsed)
        {
            DomainException.ThrowIf(policy == RuleChangePolicy.Frozen, PromotionErrors.RulesLockedAfterUsage);
            DomainException.ThrowIf(
                policy == RuleChangePolicy.NonDestructiveOnly && !IsNonDestructive(newRules),
                PromotionErrors.DestructiveChangeAfterUsage);

            DomainException.ThrowIf(
                newRules.UsageLimit is not null && newRules.UsageLimit.Value < UsageCount,
                PromotionErrors.UsageLimitBelowUsage);
        }

        Rules = newRules;
        Raise(new PromotionRulesChanged(TenantId, Id, newRules, now));
    }

    /// <summary>
    /// Enregistre une utilisation de la promotion sur une commande et renvoie la remise, calculée ici par
    /// <see cref="DiscountPolicy"/> pour qu'aucun appelant ne puisse enregistrer un montant arbitraire.
    /// <paramref name="orderAlreadyCounted"/> provient de <see cref="IPromotionUsageLedger"/> : un second appel pour
    /// la même commande (rejeu d'événement) est rejeté au lieu d'être compté deux fois.
    /// </summary>
    public Money Apply(OrderSnapshot order, bool orderAlreadyCounted, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(clock);
        this.EnsureSameTenant(order.TenantId);
        DomainException.ThrowIf(orderAlreadyCounted, PromotionErrors.AlreadyAppliedToOrder);
        EnsureNotExpired();
        DomainException.ThrowIf(Status != PromotionStatus.Active, PromotionErrors.NotActive);
        var now = clock.UtcNow;
        DomainException.ThrowIf(!ValidityPeriod.Contains(now), PromotionErrors.OutsideValidityPeriod);
        DomainException.ThrowIf(IsUsageLimitReached, PromotionErrors.UsageLimitReached);

        var evaluation = DiscountPolicy.Evaluate(order, Rules, now);
        if (evaluation.Reason is not null)
        {
            throw new DomainException(evaluation.Reason);
        }

        UsageCount++;
        Raise(new PromotionApplied(TenantId, Id, order.OrderId, evaluation.Discount, UsageCount, now));
        return evaluation.Discount;
    }

    /// <summary>
    /// Restitue l'utilisation détenue par une commande annulée. C'est une correction de compteur, donc permise dans
    /// tous les statuts, Expired compris. <paramref name="orderHoldsUsage"/> vient de <see cref="IPromotionUsageLedger"/>.
    /// </summary>
    public void RevertUsage(OrderId orderId, bool orderHoldsUsage, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!orderHoldsUsage, PromotionErrors.NoUsageForOrder);

        UsageCount = Math.Max(0, UsageCount - 1);
        Raise(new PromotionUsageReverted(TenantId, Id, orderId, UsageCount, clock.UtcNow));
    }

    public PromotionReference ToReference() => new(TenantId, Id, Status == PromotionStatus.Expired);

    private void EnsureNotExpired() =>
        DomainException.ThrowIf(Status == PromotionStatus.Expired, PromotionErrors.Expired);

    private bool IsNonDestructive(PromotionRules next) =>
        next.Benefit == Rules.Benefit
        && next.Conditions == Rules.Conditions
        && next.RequiresCoupon == Rules.RequiresCoupon
        && next.ValidityPeriod.StartUtc == ValidityPeriod.StartUtc
        && next.ValidityPeriod.EndUtc >= ValidityPeriod.EndUtc
        && (next.UsageLimit is null || (UsageLimit is not null && next.UsageLimit.Value >= UsageLimit.Value));
}
