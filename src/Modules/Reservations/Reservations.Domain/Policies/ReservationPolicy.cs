namespace Reservations.Domain;

/// <summary>
/// Règles de réservation d'un établissement. Un créneau est réservable s'il commence au plus tôt dans
/// <see cref="MinLeadTime"/> et au plus tard dans <see cref="MaxAdvance"/>, dure entre <see cref="MinDuration"/> et
/// <see cref="MaxDuration"/>, pour au plus <see cref="MaxPartySize"/> convives ; un client peut annuler ou modifier
/// une réservation jusqu'à <see cref="CancellationCutoff"/> avant son début. Toutes les bornes sont incluses.
/// </summary>
public sealed class ReservationPolicy : ValueObject
{
    public const int DefaultMaxPartySize = 50;

    /// <summary>Aucune policy n'accepte un groupe plus grand : garde tout décompte de places loin des limites d'int.</summary>
    public const int MaxPartySizeCeiling = 10_000;

    public static readonly TimeSpan DefaultMinDuration = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan DefaultMaxDuration = TimeSpan.FromHours(6);

    /// <summary>Créneau le plus long qu'une policy puisse autoriser.</summary>
    public static readonly TimeSpan MaxDurationCeiling = TimeSpan.FromHours(24);

    /// <summary>Horizon de réservation et délai d'annulation les plus longs qu'une policy puisse contenir.</summary>
    public static readonly TimeSpan MaxAdvanceCeiling = TimeSpan.FromDays(730);

    private ReservationPolicy(
        TimeSpan minLeadTime,
        TimeSpan maxAdvance,
        TimeSpan defaultDuration,
        TimeSpan cancellationCutoff,
        TimeSpan minDuration,
        TimeSpan maxDuration,
        int maxPartySize,
        bool acceptsOnlineBookings)
    {
        AcceptsOnlineBookings = acceptsOnlineBookings;
        MinLeadTime = minLeadTime;
        MaxAdvance = maxAdvance;
        DefaultDuration = defaultDuration;
        CancellationCutoff = cancellationCutoff;
        MinDuration = minDuration;
        MaxDuration = maxDuration;
        MaxPartySize = maxPartySize;
    }

    /// <summary>
    /// 30 minutes de délai minimal, 90 jours d'horizon, tables de 2 heures (15 minutes à 6 heures autorisées),
    /// annulation jusqu'à 2 heures avant, groupes de 50 convives au plus.
    /// </summary>
    public static ReservationPolicy Default { get; } =
        Create(TimeSpan.FromMinutes(30), TimeSpan.FromDays(90), TimeSpan.FromHours(2), TimeSpan.FromHours(2));

    public TimeSpan MinLeadTime { get; }

    public TimeSpan MaxAdvance { get; }

    public TimeSpan DefaultDuration { get; }

    public TimeSpan CancellationCutoff { get; }

    /// <summary>Créneau le plus court qu'une réservation puisse occuper.</summary>
    public TimeSpan MinDuration { get; }

    /// <summary>Créneau le plus long qu'une réservation puisse occuper.</summary>
    public TimeSpan MaxDuration { get; }

    /// <summary>Groupe le plus nombreux qu'une seule réservation puisse amener.</summary>
    public int MaxPartySize { get; }

    /// <summary>Les clients réservent eux-mêmes sur le site du restaurant (désactivé par défaut).</summary>
    public bool AcceptsOnlineBookings { get; }

    /// <param name="minDuration">Vaut <see cref="DefaultMinDuration"/> par défaut.</param>
    /// <param name="maxDuration">Vaut <see cref="DefaultMaxDuration"/> par défaut.</param>
    public static ReservationPolicy Create(
        TimeSpan minLeadTime,
        TimeSpan maxAdvance,
        TimeSpan defaultDuration,
        TimeSpan cancellationCutoff,
        TimeSpan? minDuration = null,
        TimeSpan? maxDuration = null,
        int maxPartySize = DefaultMaxPartySize,
        bool acceptsOnlineBookings = false)
    {
        var shortest = minDuration ?? DefaultMinDuration;
        var longest = maxDuration ?? DefaultMaxDuration;

        DomainException.ThrowIf(minLeadTime < TimeSpan.Zero, ReservationErrors.PolicyMinLeadTimeNegative);
        DomainException.ThrowIf(maxAdvance <= TimeSpan.Zero, ReservationErrors.PolicyMaxAdvanceNotPositive);
        DomainException.ThrowIf(maxAdvance <= minLeadTime, ReservationErrors.PolicyMaxAdvanceNotAfterLeadTime);
        DomainException.ThrowIf(maxAdvance > MaxAdvanceCeiling, ReservationErrors.PolicyMaxAdvanceTooLong);
        DomainException.ThrowIf(defaultDuration <= TimeSpan.Zero, ReservationErrors.PolicyDefaultDurationNotPositive);
        DomainException.ThrowIf(cancellationCutoff < TimeSpan.Zero, ReservationErrors.PolicyCancellationCutoffNegative);
        DomainException.ThrowIf(cancellationCutoff > MaxAdvanceCeiling, ReservationErrors.PolicyCancellationCutoffTooLong);
        DomainException.ThrowIf(
            shortest <= TimeSpan.Zero || longest > MaxDurationCeiling || shortest > longest,
            ReservationErrors.PolicyDurationBoundsInvalid);
        DomainException.ThrowIf(
            defaultDuration < shortest || defaultDuration > longest,
            ReservationErrors.PolicyDefaultDurationOutOfBounds);
        DomainException.ThrowIf(
            maxPartySize is <= 0 or > MaxPartySizeCeiling,
            ReservationErrors.PolicyMaxPartySizeInvalid);

        return new ReservationPolicy(minLeadTime, maxAdvance, defaultDuration, cancellationCutoff, shortest, longest, maxPartySize, acceptsOnlineBookings);
    }

    public bool IsTooSoon(DateTimeOffset startAt, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return IsTooSoon(startAt, clock.UtcNow);
    }

    public bool IsBeyondHorizon(DateTimeOffset startAt, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return IsBeyondHorizon(startAt, clock.UtcNow);
    }

    public bool CanBookAt(DateTimeOffset startAt, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var now = clock.UtcNow;
        return !IsTooSoon(startAt, now) && !IsBeyondHorizon(startAt, now);
    }

    /// <summary>Vrai tant qu'une réservation commençant à <paramref name="startAt"/> est avant son délai d'annulation.</summary>
    public bool CanCancelAt(DateTimeOffset startAt, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return CanCancelAt(startAt, clock.UtcNow);
    }

    public bool IsTooShort(TimeSpan duration) => duration < MinDuration;

    public bool IsTooLong(TimeSpan duration) => duration > MaxDuration;

    /// <summary>Le créneau occupé par une réservation commençant à <paramref name="startAt"/> avec la durée par défaut.</summary>
    public Period SlotFor(DateTimeOffset startAt)
    {
        // Un début si proche de la fin du calendrier que le créneau ne peut pas finir est au-delà de tout horizon.
        var startUtc = startAt.ToUniversalTime();
        DomainException.ThrowIf(startUtc > DateTimeOffset.MaxValue - DefaultDuration, ReservationErrors.BeyondBookingHorizon);
        return Period.Create(startUtc, startUtc + DefaultDuration);
    }

    // Les trois comparaisons ci-dessous soustraient deux instants (ce qui ne sort jamais de la plage de TimeSpan) au
    // lieu d'ajouter une durée à un instant (ce qui lève une exception près des bornes du calendrier).
    internal bool IsTooSoon(DateTimeOffset startAt, DateTimeOffset now) => startAt - now < MinLeadTime;

    internal bool IsBeyondHorizon(DateTimeOffset startAt, DateTimeOffset now) => startAt - now > MaxAdvance;

    internal bool CanCancelAt(DateTimeOffset startAt, DateTimeOffset now) => startAt - now >= CancellationCutoff;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return MinLeadTime;
        yield return MaxAdvance;
        yield return DefaultDuration;
        yield return CancellationCutoff;
        yield return MinDuration;
        yield return MaxDuration;
        yield return MaxPartySize;
        yield return AcceptsOnlineBookings;
    }
}
