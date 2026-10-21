namespace Floor.Domain;

public readonly record struct WaitingPartyId
{
    private WaitingPartyId(Guid value) => Value = value;

    public Guid Value { get; }

    public static WaitingPartyId New() => new(Guid.CreateVersion7());

    public static WaitingPartyId From(Guid value)
    {
        DomainException.ThrowIf(value == Guid.Empty, CommonErrors.EmptyId);
        return new WaitingPartyId(value);
    }

    public override string ToString() => Value.ToString();
}

public enum WaitingPartyStatus
{
    Waiting = 1,

    /// <summary>Prévenu que sa table est prête.</summary>
    Notified = 2,

    Seated = 3,

    /// <summary>Parti, ou retiré de la file par l'équipe.</summary>
    Left = 4,
}

public static class WaitingPartyErrors
{
    public static readonly DomainError NameRequired = new("floor.waiting_party.name_required", "Give a name to call the party.");
    public static readonly DomainError NameTooLong = new("floor.waiting_party.name_too_long", "The name is too long (80 characters at most).");
    public static readonly DomainError PartySizeInvalid = new("floor.waiting_party.party_size_invalid", "The party size must be between 1 and 100.");
    public static readonly DomainError NoteTooLong = new("floor.waiting_party.note_too_long", "The note is too long (200 characters at most).");
    public static readonly DomainError QuotedWaitInvalid = new("floor.waiting_party.quoted_wait_invalid", "The quoted wait must be between 0 and 240 minutes.");
    public static readonly DomainError Closed = new("floor.waiting_party.closed", "This party is already seated or gone.");
    public static readonly DomainError NoPhone = new("floor.waiting_party.no_phone", "No phone number to send the message to.");
    public static readonly DomainError TooManyNotifications = new("floor.waiting_party.too_many_notifications", "The party was already told 5 times.");
    public static readonly DomainError OtherLocation = new("floor.waiting_party.other_location", "The table is in another location.");
}

/// <summary>
/// Un groupe arrivé sans réservation qui attend une table (file à la porte, jour même). Le téléphone, facultatif, sert
/// seulement à le prévenir que sa table est prête : il est effacé dès que le groupe est installé ou parti.
/// </summary>
public sealed class WaitingParty : AggregateRoot<WaitingPartyId>, ITenantOwned
{
    public const int NameMaxLength = 80;
    public const int NoteMaxLength = 200;
    public const int MaxPartySize = 100;
    public const int MaxQuotedMinutes = 240;
    public const int MaxNotifications = 5;

    private WaitingParty(
        WaitingPartyId id, TenantId tenantId, LocationId locationId, string name, PhoneNumber? phone, int partySize, string? note, int? quotedMinutes,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        Name = name;
        Phone = phone;
        PartySize = partySize;
        Note = note;
        QuotedMinutes = quotedMinutes;
        Status = WaitingPartyStatus.Waiting;
        CreatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private WaitingParty()
    {
        Name = null!;
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public string Name { get; private set; }

    public PhoneNumber? Phone { get; private set; }

    public int PartySize { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Attente annoncée au client à son arrivée, en minutes.</summary>
    public int? QuotedMinutes { get; private set; }

    public WaitingPartyStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? NotifiedAtUtc { get; private set; }

    public int NotificationCount { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public TableSessionId? TableSessionId { get; private set; }

    public bool IsOpen => Status is WaitingPartyStatus.Waiting or WaitingPartyStatus.Notified;

    public static WaitingParty Create(
        TenantId tenantId, LocationReference location, string? name, PhoneNumber? phone, int partySize, string? note, int? quotedMinutes, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);
        var trimmed = name?.Trim();
        DomainException.ThrowIf(string.IsNullOrEmpty(trimmed), WaitingPartyErrors.NameRequired);
        DomainException.ThrowIf(trimmed!.Length > NameMaxLength, WaitingPartyErrors.NameTooLong);
        DomainException.ThrowIf(partySize is < 1 or > MaxPartySize, WaitingPartyErrors.PartySizeInvalid);
        var normalizedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        DomainException.ThrowIf(normalizedNote?.Length > NoteMaxLength, WaitingPartyErrors.NoteTooLong);
        DomainException.ThrowIf(quotedMinutes is < 0 or > MaxQuotedMinutes, WaitingPartyErrors.QuotedWaitInvalid);

        var now = clock.UtcNow;
        var party = new WaitingParty(WaitingPartyId.New(), tenantId, location.LocationId, trimmed, phone, partySize, normalizedNote, quotedMinutes, now);
        party.EnsureSameTenant(location.TenantId);
        party.Raise(new WaitingPartyAdded(tenantId, party.Id, party.LocationId, partySize, now));
        return party;
    }

    /// <summary>Prévient le groupe que sa table est prête (message au numéro donné). Possible plusieurs fois (5 au plus).</summary>
    public void Notify(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsOpen, WaitingPartyErrors.Closed);
        DomainException.ThrowIf(Phone is null, WaitingPartyErrors.NoPhone);
        DomainException.ThrowIf(NotificationCount >= MaxNotifications, WaitingPartyErrors.TooManyNotifications);
        var now = clock.UtcNow;
        Status = WaitingPartyStatus.Notified;
        NotifiedAtUtc = now;
        NotificationCount++;
        Raise(new WaitingPartyNotified(TenantId, Id, LocationId, NotificationCount, now));
    }

    /// <summary>Le groupe est installé : la file le retire et oublie son numéro.</summary>
    public void Seat(TableSessionId sessionId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(sessionId);
        DomainException.ThrowIf(!IsOpen, WaitingPartyErrors.Closed);
        Close(WaitingPartyStatus.Seated, clock);
        TableSessionId = sessionId;
    }

    /// <summary>Le groupe est parti (ou retiré de la file) : son numéro est oublié.</summary>
    public void Leave(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!IsOpen, WaitingPartyErrors.Closed);
        Close(WaitingPartyStatus.Left, clock);
    }

    private void Close(WaitingPartyStatus status, IClock clock)
    {
        var now = clock.UtcNow;
        Status = status;
        ClosedAtUtc = now;
        Phone = null;
        Raise(new WaitingPartyClosed(TenantId, Id, LocationId, status, now));
    }
}

public sealed record WaitingPartyAdded(TenantId TenantId, WaitingPartyId PartyId, LocationId LocationId, int PartySize, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

/// <summary>Le groupe doit être prévenu : l'hôte envoie le message « votre table est prête ».</summary>
public sealed record WaitingPartyNotified(TenantId TenantId, WaitingPartyId PartyId, LocationId LocationId, int Count, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public sealed record WaitingPartyClosed(TenantId TenantId, WaitingPartyId PartyId, LocationId LocationId, WaitingPartyStatus Status, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);
