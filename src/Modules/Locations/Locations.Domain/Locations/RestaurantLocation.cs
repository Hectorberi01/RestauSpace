namespace Locations.Domain.Locations;

/// <summary>
/// Établissement physique exploité par un tenant : identité, adresse, horaires d'ouverture (hebdomadaires + exceptions
/// datées), capacité d'accueil et cycle de vie. Les horaires sont des heures locales de <see cref="TimeZone"/> ; les
/// queries <c>...AtInstant</c> / <c>...During</c> prennent des instants UTC et les convertissent avec ce fuseau.
/// </summary>
public sealed class RestaurantLocation : AggregateRoot<LocationId>, ITenantOwned
{
    public const int ReasonMaxLength = 200;

    private readonly List<ExceptionalSchedule> _exceptionalSchedules = [];

    private RestaurantLocation(
        LocationId id,
        TenantId tenantId,
        LocationName name,
        Address address,
        BusinessHours hours,
        Capacity capacity,
        TimeZoneId timeZone,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Address = address;
        Hours = hours;
        Capacity = capacity;
        TimeZone = timeZone;
        Status = LocationStatus.Draft;
        CreatedAtUtc = createdAtUtc;
        Contact = LocationContact.None;
    }

    // Réservé à l'ORM.
    private RestaurantLocation()
    {
        Name = null!;
        Address = null!;
        Hours = null!;
        Capacity = null!;
        TimeZone = null!;
        Contact = LocationContact.None;
    }

    public TenantId TenantId { get; private set; }

    public LocationName Name { get; private set; }

    public Address Address { get; private set; }

    /// <summary>Coordonnées publiques (téléphone, WhatsApp, e-mail), toutes facultatives.</summary>
    public LocationContact Contact { get; private set; }

    public BusinessHours Hours { get; private set; }

    public LocationStatus Status { get; private set; }

    public Capacity Capacity { get; private set; }

    /// <summary>Fuseau des horaires d'ouverture. L'Application passe par défaut le fuseau du tenant à la
    /// création.</summary>
    public TimeZoneId TimeZone { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Renseigné uniquement en TemporarilyClosed.</summary>
    public string? TemporaryClosureReason { get; private set; }

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public IReadOnlyCollection<ExceptionalSchedule> ExceptionalSchedules => _exceptionalSchedules.AsReadOnly();

    /// <summary>Seul un établissement Active sert des clients (prend réservations et commandes).</summary>
    public bool IsOperational => Status == LocationStatus.Active;

    public static RestaurantLocation Create(
        TenantId tenantId,
        LocationName name,
        Address address,
        BusinessHours hours,
        Capacity capacity,
        TimeZoneId timeZone,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(hours);
        ArgumentNullException.ThrowIfNull(capacity);
        ArgumentNullException.ThrowIfNull(timeZone);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);

        var now = clock.UtcNow;
        var location = new RestaurantLocation(LocationId.New(), tenantId, name, address, hours, capacity, timeZone, now);
        location.Raise(new RestaurantLocationCreated(
            tenantId, location.Id, name.Value, address.CountryCode.Value, capacity.Seats, timeZone.Value, now));
        return location;
    }

    /// <summary>
    /// Indique si des clients peuvent être servis à ces date et heure locales. False sauf si l'établissement est
    /// Active. L'horaire en vigueur pour une date est son horaire exceptionnel s'il existe, sinon les horaires
    /// hebdomadaires ; une plage de nuit appartient à la date où elle commence, donc une fermeture exceptionnelle
    /// d'une date annule aussi le service de cette date après minuit, mais pas la fin de la plage de nuit de la veille.
    /// </summary>
    public bool IsOpenAt(DateOnly date, TimeOnly time) =>
        IsOperational && OpeningEndAfter(date.ToDateTime(time)) is not null;

    /// <summary>Comme <see cref="IsOpenAt"/> pour un instant UTC, lu à l'heure locale de <see cref="TimeZone"/>.</summary>
    public bool IsOpenAtInstant(DateTimeOffset instantUtc)
    {
        var local = TimeZone.ToLocal(instantUtc);
        return IsOpenAt(DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime));
    }

    /// <summary>
    /// Indique si l'établissement est ouvert à CHAQUE instant du créneau semi-ouvert, c.-à-d. sans aucun trou fermé.
    /// Le créneau est parcouru plage par plage, jamais échantillonné : depuis le curseur, la plage en vigueur couvrant
    /// l'heure locale est suivie jusqu'à sa fin, où une plage contiguë (même jour, lendemain ou fin d'une plage de nuit)
    /// doit prendre le relais. Quand un changement d'heure tombe dans une plage, le parcours reprend au changement
    /// lui-même, avec la nouvelle heure locale : la réponse égale toujours <see cref="IsOpenAtInstant"/> sur tout le
    /// créneau.
    /// </summary>
    public bool IsOpenDuring(Period slotUtc)
    {
        ArgumentNullException.ThrowIfNull(slotUtc);
        if (!IsOperational)
        {
            return false;
        }

        var cursor = slotUtc.StartUtc;
        while (cursor < slotUtc.EndUtc)
        {
            var local = TimeZone.ToLocal(cursor);
            if (OpeningEndAfter(local.DateTime) is not { } openUntil)
            {
                return false;
            }

            // Valable tant que le décalage UTC ne change pas avant la fin de la plage.
            var rangeEnd = cursor + (openUntil - local.DateTime);
            cursor = TimeZone.ToLocal(rangeEnd).Offset == local.Offset
                ? rangeEnd
                : FirstInstantWithAnotherOffset(cursor, rangeEnd, local.Offset);
        }

        return true;
    }

    /// <summary>Changement de fuseau : les horaires gardent leurs heures locales, lues dans le nouveau fuseau.</summary>
    public void ChangeTimeZone(TimeZoneId newTimeZone, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newTimeZone);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        if (newTimeZone == TimeZone)
        {
            return;
        }

        var previous = TimeZone;
        TimeZone = newTimeZone;
        Raise(new LocationTimeZoneChanged(TenantId, Id, previous.Value, newTimeZone.Value, clock.UtcNow));
    }

    /// <summary>Plages commençant le <paramref name="date"/> : celles de l'exception s'il y en a une, sinon les
    /// hebdomadaires.</summary>
    public IReadOnlyCollection<TimeRange> RangesInForceOn(DateOnly date)
    {
        var exception = _exceptionalSchedules.SingleOrDefault(schedule => schedule.Date == date);
        return exception is not null ? exception.Ranges : Hours.RangesOn(date.DayOfWeek);
    }

    public void Activate(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        DomainException.ThrowIf(Status != LocationStatus.Draft, LocationErrors.OnlyDraftCanBeActivated);

        Status = LocationStatus.Active;
        Raise(new LocationActivated(TenantId, Id, clock.UtcNow));
    }

    public void TemporarilyClose(IClock clock, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        DomainException.ThrowIf(Status != LocationStatus.Active, LocationErrors.OnlyActiveCanBeTemporarilyClosed);

        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        DomainException.ThrowIf(normalizedReason?.Length > ReasonMaxLength, LocationErrors.ReasonTooLong);

        Status = LocationStatus.TemporarilyClosed;
        TemporaryClosureReason = normalizedReason;
        Raise(new LocationTemporarilyClosed(TenantId, Id, normalizedReason, clock.UtcNow));
    }

    public void Reopen(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        DomainException.ThrowIf(Status != LocationStatus.TemporarilyClosed, LocationErrors.OnlyTemporarilyClosedCanBeReopened);

        Status = LocationStatus.Active;
        TemporaryClosureReason = null;
        Raise(new LocationReopened(TenantId, Id, clock.UtcNow));
    }

    /// <summary>Terminal : atteignable depuis tous les autres statuts, jamais quitté.</summary>
    public void Archive(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();

        var now = clock.UtcNow;
        var previous = Status;
        Status = LocationStatus.Archived;
        TemporaryClosureReason = null;
        ArchivedAtUtc = now;
        Raise(new LocationArchived(TenantId, Id, previous, now));
    }

    public void Rename(LocationName newName, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newName);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        if (newName == Name)
        {
            return;
        }

        var previous = Name;
        Name = newName;
        Raise(new LocationRenamed(TenantId, Id, previous.Value, newName.Value, clock.UtcNow));
    }

    public void ChangeAddress(Address newAddress, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newAddress);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        if (newAddress == Address)
        {
            return;
        }

        Address = newAddress;
        Raise(new LocationAddressChanged(TenantId, Id, newAddress, clock.UtcNow));
    }

    public void SetContact(LocationContact newContact, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newContact);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        if (newContact == Contact)
        {
            return;
        }

        Contact = newContact;
        Raise(new LocationContactChanged(TenantId, Id, clock.UtcNow));
    }

    public void SetBusinessHours(BusinessHours newHours, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newHours);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        if (newHours == Hours)
        {
            return;
        }

        Hours = newHours;
        Raise(new BusinessHoursChanged(TenantId, Id, newHours, clock.UtcNow));
    }

    public void SetCapacity(Capacity newCapacity, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(newCapacity);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        if (newCapacity == Capacity)
        {
            return;
        }

        var previous = Capacity;
        Capacity = newCapacity;
        Raise(new LocationCapacityChanged(TenantId, Id, previous.Seats, newCapacity.Seats, clock.UtcNow));
    }

    /// <summary>Jour de fermeture exceptionnelle (jour férié, événement privé...).</summary>
    public ExceptionalSchedule AddExceptionalClosure(DateOnly date, IClock clock, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        EnsureNoExceptionOn(date);

        return Register(ExceptionalSchedule.Closed(date, reason), clock);
    }

    /// <summary>Ouverture exceptionnelle : <paramref name="ranges"/> remplacent les plages hebdomadaires de cette
    /// date.</summary>
    public ExceptionalSchedule AddExceptionalSchedule(
        DateOnly date,
        IEnumerable<TimeRange> ranges,
        IClock clock,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();
        EnsureNoExceptionOn(date);

        return Register(ExceptionalSchedule.Open(date, ranges, reason), clock);
    }

    public void RemoveExceptionalSchedule(DateOnly date, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureNotArchived();

        var schedule = _exceptionalSchedules.SingleOrDefault(candidate => candidate.Date == date);
        DomainException.ThrowIf(schedule is null, LocationErrors.ExceptionalScheduleNotFound);

        _exceptionalSchedules.Remove(schedule!);
        Raise(new ExceptionalScheduleRemoved(TenantId, Id, schedule!.Id, date, clock.UtcNow));
    }

    /// <summary>
    /// Fin locale de la plage d'ouverture couvrant ces date et heure locales (plages seules, quel que soit le statut),
    /// ou null si aucune plage ne les couvre. Une plage de nuit de la veille compte encore après minuit.
    /// </summary>
    private DateTime? OpeningEndAfter(DateTime local)
    {
        var date = DateOnly.FromDateTime(local);
        var time = TimeOnly.FromDateTime(local);

        var startedToday = RangesInForceOn(date).FirstOrDefault(range => range.CoversOnStartDay(time));
        if (startedToday is not null)
        {
            return date.ToDateTime(TimeOnly.MinValue) + startedToday.EndOffset;
        }

        if (date == DateOnly.MinValue)
        {
            return null;
        }

        var previousDate = date.AddDays(-1);
        var startedTheDayBefore = RangesInForceOn(previousDate).FirstOrDefault(range => range.CoversOnNextDay(time));
        return startedTheDayBefore is null ? null : previousDate.ToDateTime(TimeOnly.MinValue) + startedTheDayBefore.EndOffset;
    }

    // Dichotomie entre un instant qui a le décalage et un instant ultérieur qui ne l'a pas : trouve le changement d'heure.
    private DateTimeOffset FirstInstantWithAnotherOffset(DateTimeOffset from, DateTimeOffset to, TimeSpan offset)
    {
        while (to.UtcTicks - from.UtcTicks > 1)
        {
            var middle = new DateTimeOffset(from.UtcTicks + ((to.UtcTicks - from.UtcTicks) / 2), TimeSpan.Zero);
            if (TimeZone.ToLocal(middle).Offset == offset)
            {
                from = middle;
            }
            else
            {
                to = middle;
            }
        }

        return to;
    }

    private ExceptionalSchedule Register(ExceptionalSchedule schedule, IClock clock)
    {
        _exceptionalSchedules.Add(schedule);
        Raise(new ExceptionalScheduleAdded(TenantId, Id, schedule.Id, schedule.Date, schedule.IsClosed, clock.UtcNow));
        return schedule;
    }

    private void EnsureNoExceptionOn(DateOnly date) =>
        DomainException.ThrowIf(
            _exceptionalSchedules.Any(schedule => schedule.Date == date),
            LocationErrors.ExceptionalDateAlreadyDefined);

    private void EnsureNotArchived() =>
        DomainException.ThrowIf(Status == LocationStatus.Archived, LocationErrors.Archived);
}
