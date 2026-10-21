namespace Reservations.Domain;

public readonly record struct WaitlistEntryId
{
    private WaitlistEntryId(Guid value) => Value = value;

    public Guid Value { get; }

    public static WaitlistEntryId New() => new(Guid.CreateVersion7());

    public static WaitlistEntryId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new WaitlistEntryId(value);
    }

    public override string ToString() => Value.ToString();
}

public enum WaitlistStatus
{
    /// <summary>Attend qu'une place se libère.</summary>
    Waiting = 1,

    /// <summary>Une place lui est proposée jusqu'à <see cref="WaitlistEntry.OfferExpiresAtUtc"/>.</summary>
    Offered = 2,

    /// <summary>Le client a accepté : une réservation a été créée.</summary>
    Booked = 3,

    /// <summary>L'heure souhaitée est passée sans place libre.</summary>
    Expired = 4,

    Cancelled = 5,
}

public enum WaitlistSource
{
    Online = 1,
    Staff = 2,
}

public static class WaitlistErrors
{
    public static readonly DomainError FlexibilityInvalid = new("reservations.waitlist.flexibility_invalid", "The flexibility must be between 0 and 180 minutes.");
    public static readonly DomainError PhoneRequired = new("reservations.waitlist.phone_required", "A phone number is required to be told when a table frees up.");
    public static readonly DomainError StartInPast = new("reservations.waitlist.start_in_past", "The wished time has already passed.");
    public static readonly DomainError NotWaiting = new("reservations.waitlist.not_waiting", "This entry is not waiting for a table.");
    public static readonly DomainError NotOffered = new("reservations.waitlist.not_offered", "No table is offered to this entry.");
    public static readonly DomainError OfferExpired = new("reservations.waitlist.offer_expired", "The offer has expired.");
    public static readonly DomainError OfferOutsideWindow = new("reservations.waitlist.offer_outside_window", "The offered time is outside what the guest accepts.");
    public static readonly DomainError AlreadyClosed = new("reservations.waitlist.already_closed", "This entry is already booked, expired or cancelled.");
    public static readonly DomainError OfferWindowInvalid = new("reservations.waitlist.offer_window_invalid", "The time to accept an offer must be between 5 and 120 minutes.");
    public static readonly DomainError SourceInvalid = new("reservations.waitlist.source_invalid", "The source is invalid.");
}

/// <summary>Réglages de la liste d'attente d'un établissement.</summary>
public sealed class WaitlistSettings : ValueObject
{
    public static readonly TimeSpan DefaultOfferWindow = TimeSpan.FromMinutes(15);

    private WaitlistSettings(bool acceptsOnline, TimeSpan offerWindow)
    {
        AcceptsOnline = acceptsOnline;
        OfferWindow = offerWindow;
    }

    public static WaitlistSettings Default { get; } = new(false, DefaultOfferWindow);

    /// <summary>Les clients s'inscrivent eux-mêmes sur le site quand un créneau est complet.</summary>
    public bool AcceptsOnline { get; }

    /// <summary>Temps laissé au client pour accepter une place proposée.</summary>
    public TimeSpan OfferWindow { get; }

    public static WaitlistSettings Create(bool acceptsOnline, TimeSpan offerWindow)
    {
        DomainException.ThrowIf(offerWindow < TimeSpan.FromMinutes(5) || offerWindow > TimeSpan.FromMinutes(120), WaitlistErrors.OfferWindowInvalid);
        return new WaitlistSettings(acceptsOnline, offerWindow);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return AcceptsOnline;
        yield return OfferWindow;
    }
}

/// <summary>
/// Un client qui attend une table sur un créneau complet : heure souhaitée, avec une souplesse de part et d'autre.
/// Quand une place se libère, elle lui est proposée pour un temps limité (l'offre retient les places pendant ce
/// temps) ; il l'accepte (une réservation est créée) ou la laisse passer (elle va au suivant, il reste en attente). Le
/// contact est un instantané effaçable, comme celui d'une réservation.
/// </summary>
public sealed class WaitlistEntry : AggregateRoot<WaitlistEntryId>, ITenantOwned
{
    public const int MaxFlexibilityMinutes = 180;
    public const int NotesMaxLength = 500;

    private WaitlistEntry(
        WaitlistEntryId id, TenantId tenantId, LocationId locationId, CustomerId? customerId, GuestContact contact, int partySize,
        DateTimeOffset desiredStartUtc, int flexibilityMinutes, string? notes, WaitlistSource source, DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        CustomerId = customerId;
        GuestContact = contact;
        PartySize = partySize;
        DesiredStartUtc = desiredStartUtc;
        FlexibilityMinutes = flexibilityMinutes;
        Notes = notes;
        Source = source;
        Status = WaitlistStatus.Waiting;
        CreatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private WaitlistEntry()
    {
        GuestContact = null!;
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public CustomerId? CustomerId { get; private set; }

    public GuestContact GuestContact { get; private set; }

    public int PartySize { get; private set; }

    public DateTimeOffset DesiredStartUtc { get; private set; }

    /// <summary>Minutes acceptées avant et après l'heure souhaitée.</summary>
    public int FlexibilityMinutes { get; private set; }

    public string? Notes { get; private set; }

    public WaitlistSource Source { get; private set; }

    public WaitlistStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? OfferedStartUtc { get; private set; }

    public DateTimeOffset? OfferExpiresAtUtc { get; private set; }

    /// <summary>Offres faites puis laissées passer.</summary>
    public int MissedOffers { get; private set; }

    public ReservationId? ReservationId { get; private set; }

    public bool IsOpen => Status is WaitlistStatus.Waiting or WaitlistStatus.Offered;

    /// <summary>Premier début accepté.</summary>
    public DateTimeOffset EarliestStartUtc => DesiredStartUtc.AddMinutes(-FlexibilityMinutes);

    /// <summary>Dernier début accepté : au-delà, l'inscription expire.</summary>
    public DateTimeOffset LatestStartUtc => DesiredStartUtc.AddMinutes(FlexibilityMinutes);

    public static WaitlistEntry Create(
        TenantId tenantId,
        LocationReference location,
        CustomerReference? customer,
        GuestContact contact,
        int partySize,
        DateTimeOffset desiredStartUtc,
        int flexibilityMinutes,
        string? notes,
        WaitlistSource source,
        ReservationPolicy policy,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(contact);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        DomainException.ThrowIf(!Enum.IsDefined(source), WaitlistErrors.SourceInvalid);
        DomainException.ThrowIf(contact.Phone is null, WaitlistErrors.PhoneRequired);
        DomainException.ThrowIf(partySize <= 0, ReservationErrors.PartySizeNotPositive);
        DomainException.ThrowIf(partySize > policy.MaxPartySize, ReservationErrors.PartySizeTooLarge);
        DomainException.ThrowIf(flexibilityMinutes is < 0 or > MaxFlexibilityMinutes, WaitlistErrors.FlexibilityInvalid);
        var now = clock.UtcNow;
        var start = desiredStartUtc.ToUniversalTime();
        DomainException.ThrowIf(start.AddMinutes(flexibilityMinutes) <= now, WaitlistErrors.StartInPast);
        DomainException.ThrowIf(policy.IsBeyondHorizon(start, now), ReservationErrors.BeyondBookingHorizon);
        var normalizedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        DomainException.ThrowIf(normalizedNotes?.Length > NotesMaxLength, ReservationErrors.NotesTooLong);

        var entry = new WaitlistEntry(
            WaitlistEntryId.New(), tenantId, location.LocationId, customer?.CustomerId, contact, partySize, start, flexibilityMinutes, normalizedNotes, source, now);
        entry.EnsureSameTenant(location.TenantId);
        DomainException.ThrowIf(!location.IsOperational, ReservationErrors.LocationNotOperational);
        if (customer is not null)
        {
            entry.EnsureSameTenant(customer.TenantId);
            DomainException.ThrowIf(customer.IsAnonymized, ReservationErrors.CustomerAnonymized);
        }

        entry.Raise(new WaitlistEntryCreated(tenantId, entry.Id, entry.LocationId, partySize, start, flexibilityMinutes, source, now));
        return entry;
    }

    /// <summary>Accepte-t-il ce début ?</summary>
    public bool Accepts(DateTimeOffset startUtc) => startUtc >= EarliestStartUtc && startUtc <= LatestStartUtc;

    /// <summary>Propose une place libérée à ce début, jusqu'à <paramref name="expiresAtUtc"/>.</summary>
    public void Offer(DateTimeOffset startUtc, DateTimeOffset expiresAtUtc, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != WaitlistStatus.Waiting, WaitlistErrors.NotWaiting);
        DomainException.ThrowIf(!Accepts(startUtc), WaitlistErrors.OfferOutsideWindow);
        var now = clock.UtcNow;
        DomainException.ThrowIf(expiresAtUtc <= now || startUtc <= now, WaitlistErrors.OfferExpired);

        Status = WaitlistStatus.Offered;
        OfferedStartUtc = startUtc;
        OfferExpiresAtUtc = expiresAtUtc;
        Raise(new WaitlistOfferMade(TenantId, Id, LocationId, startUtc, expiresAtUtc, now));
    }

    /// <summary>Vrai tant que l'offre en cours peut être acceptée.</summary>
    public bool HasLiveOffer(DateTimeOffset now) => Status == WaitlistStatus.Offered && OfferExpiresAtUtc > now;

    /// <summary>Le client a accepté et sa réservation est créée.</summary>
    public void MarkBooked(ReservationId reservationId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(reservationId);
        DomainException.ThrowIf(Status != WaitlistStatus.Offered && Status != WaitlistStatus.Waiting, WaitlistErrors.AlreadyClosed);
        Status = WaitlistStatus.Booked;
        ReservationId = reservationId;
        OfferExpiresAtUtc = null;
        Raise(new WaitlistEntryBooked(TenantId, Id, LocationId, reservationId, clock.UtcNow));
    }

    /// <summary>
    /// L'offre est passée (temps écoulé, ou le client n'en veut pas à cette heure) : l'inscription reprend sa place en
    /// attente, ou expire si l'heure souhaitée est dépassée.
    /// </summary>
    public void ReleaseOffer(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status != WaitlistStatus.Offered, WaitlistErrors.NotOffered);
        var now = clock.UtcNow;
        MissedOffers++;
        OfferedStartUtc = null;
        OfferExpiresAtUtc = null;
        Status = WaitlistStatus.Waiting;
        Raise(new WaitlistOfferReleased(TenantId, Id, LocationId, now));
        if (LatestStartUtc <= now)
        {
            Expire(clock);
        }
    }

    /// <summary>L'heure souhaitée est passée sans place : l'inscription se ferme.</summary>
    public void Expire(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsOpen, WaitlistErrors.AlreadyClosed);
        Status = WaitlistStatus.Expired;
        OfferedStartUtc = null;
        OfferExpiresAtUtc = null;
        Raise(new WaitlistEntryClosed(TenantId, Id, LocationId, WaitlistStatus.Expired, clock.UtcNow));
    }

    public void Cancel(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsOpen, WaitlistErrors.AlreadyClosed);
        Status = WaitlistStatus.Cancelled;
        OfferedStartUtc = null;
        OfferExpiresAtUtc = null;
        Raise(new WaitlistEntryClosed(TenantId, Id, LocationId, WaitlistStatus.Cancelled, clock.UtcNow));
    }

    /// <summary>Efface le contact (client anonymisé). Tous statuts ; idempotent.</summary>
    public void AnonymizeGuest()
    {
        if (GuestContact.IsAnonymized)
        {
            return;
        }

        GuestContact = GuestContact.Anonymized;
        Notes = null;
    }

    /// <summary>Les places qu'une offre en cours retient, pour l'évaluation de la capacité.</summary>
    public ExistingBooking? OfferHold(TimeSpan duration, DateTimeOffset now) =>
        HasLiveOffer(now) ? ExistingBooking.ForWalkIn(Period.Create(OfferedStartUtc!.Value, OfferedStartUtc.Value + duration), PartySize) : null;
}

public sealed record WaitlistEntryCreated(
    TenantId TenantId, WaitlistEntryId EntryId, LocationId LocationId, int PartySize, DateTimeOffset DesiredStartUtc, int FlexibilityMinutes,
    WaitlistSource Source, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

/// <summary>Une place est proposée : l'hôte prévient le client (WhatsApp ou SMS) avec le lien pour l'accepter.</summary>
public sealed record WaitlistOfferMade(
    TenantId TenantId, WaitlistEntryId EntryId, LocationId LocationId, DateTimeOffset StartAtUtc, DateTimeOffset ExpiresAtUtc, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record WaitlistOfferReleased(TenantId TenantId, WaitlistEntryId EntryId, LocationId LocationId, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record WaitlistEntryBooked(
    TenantId TenantId, WaitlistEntryId EntryId, LocationId LocationId, ReservationId ReservationId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record WaitlistEntryClosed(TenantId TenantId, WaitlistEntryId EntryId, LocationId LocationId, WaitlistStatus Status, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);
