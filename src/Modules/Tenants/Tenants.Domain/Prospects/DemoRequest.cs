namespace Tenants.Domain.Prospects;

public enum DemoRequestStatus
{
    New = 0,
    Contacted = 1,
    Converted = 2,
    Declined = 3,
}

/// <summary>
/// Demande de démonstration envoyée par un prospect depuis le site vitrine. Agrégat de la PLATEFORME : il n'appartient
/// à aucun restaurant tant que le prospect n'est pas devenu client. Contient des données personnelles (nom, email,
/// téléphone) : elles ne sortent jamais dans un événement, un log ou une trace ; la plateforme peut supprimer la
/// demande à la requête du prospect.
/// </summary>
public sealed class DemoRequest : AggregateRoot<DemoRequestId>
{
    public const int NameMaxLength = 100;
    public const int CountryMaxLength = 60;
    public const int PlanMaxLength = 40;
    public const int MessageMaxLength = 2000;
    public const int NoteMaxLength = 1000;

    private DemoRequest(
        DemoRequestId id, string contactName, string restaurantName, Email? email, PhoneNumber? phone, string country,
        string? planInterest, string? message, DateTimeOffset submittedAtUtc)
        : base(id)
    {
        ContactName = contactName;
        RestaurantName = restaurantName;
        Email = email;
        Phone = phone;
        Country = country;
        PlanInterest = planInterest;
        Message = message;
        Status = DemoRequestStatus.New;
        SubmittedAtUtc = submittedAtUtc;
        UpdatedAtUtc = submittedAtUtc;
    }

    // Réservé à l'ORM.
    private DemoRequest()
    {
        ContactName = null!;
        RestaurantName = null!;
        Country = null!;
    }

    public string ContactName { get; private set; }

    public string RestaurantName { get; private set; }

    public Email? Email { get; private set; }

    public PhoneNumber? Phone { get; private set; }

    public string Country { get; private set; }

    public string? PlanInterest { get; private set; }

    public string? Message { get; private set; }

    public DemoRequestStatus Status { get; private set; }

    /// <summary>Note interne de l'équipe commerciale, jamais montrée au prospect.</summary>
    public string? Note { get; private set; }

    public DateTimeOffset SubmittedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static DemoRequest Submit(
        string? contactName, string? restaurantName, string? email, string? phone, string? country, string? planInterest,
        string? message, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var name = Line(contactName, NameMaxLength, DemoRequestErrors.ContactNameInvalid, required: true)!;
        var restaurant = Line(restaurantName, NameMaxLength, DemoRequestErrors.RestaurantNameInvalid, required: true)!;
        var place = Line(country, CountryMaxLength, DemoRequestErrors.CountryInvalid, required: true)!;
        var plan = Line(planInterest, PlanMaxLength, DemoRequestErrors.PlanInvalid, required: false);
        var parsedEmail = string.IsNullOrWhiteSpace(email) ? null : Email.Create(email);
        var parsedPhone = string.IsNullOrWhiteSpace(phone) ? null : PhoneNumber.Create(phone);
        DomainException.ThrowIf(parsedEmail is null && parsedPhone is null, DemoRequestErrors.ContactMissing);
        var text = Text(message, MessageMaxLength, DemoRequestErrors.MessageInvalid);

        var now = clock.UtcNow;
        var request = new DemoRequest(DemoRequestId.New(), name, restaurant, parsedEmail, parsedPhone, place, plan, text, now);
        // Aucune donnée personnelle dans l'événement : l'identifiant suffit pour prévenir l'équipe.
        request.Raise(new DemoRequestSubmitted(request.Id, now));
        return request;
    }

    public void ChangeStatus(DemoRequestStatus status, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(!Enum.IsDefined(status), DemoRequestErrors.StatusInvalid);
        if (status == Status)
        {
            return;
        }

        var previous = Status;
        Status = status;
        UpdatedAtUtc = clock.UtcNow;
        Raise(new DemoRequestStatusChanged(Id, previous, status, UpdatedAtUtc));
    }

    public void ChangeNote(string? note, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Note = Text(note, NoteMaxLength, DemoRequestErrors.NoteInvalid);
        UpdatedAtUtc = clock.UtcNow;
    }

    /// <summary>Une ligne de texte : sans caractère de contrôle, espaces superflus retirés.</summary>
    private static string? Line(string? value, int maxLength, DomainError error, bool required)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            DomainException.ThrowIf(required, error);
            return null;
        }

        var trimmed = value.Trim();
        DomainException.ThrowIf(trimmed.Length < (required ? 2 : 1) || trimmed.Length > maxLength || trimmed.Any(char.IsControl), error);
        return trimmed;
    }

    /// <summary>Un texte libre : retours à la ligne admis, autres caractères de contrôle refusés.</summary>
    private static string? Text(string? value, int maxLength, DomainError error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim().ReplaceLineEndings("\n");
        DomainException.ThrowIf(trimmed.Length > maxLength || trimmed.Any(c => char.IsControl(c) && c != '\n'), error);
        return trimmed;
    }
}

public sealed record DemoRequestSubmitted(DemoRequestId DemoRequestId, DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record DemoRequestStatusChanged(
    DemoRequestId DemoRequestId, DemoRequestStatus PreviousStatus, DemoRequestStatus NewStatus, DateTimeOffset OccurredAtUtc)
    : DomainEvent(OccurredAtUtc);

public static class DemoRequestErrors
{
    public static readonly DomainError ContactNameInvalid =
        new("tenants.demo_request.contact_name_invalid", "The contact name must be between 2 and 100 characters on a single line.");

    public static readonly DomainError RestaurantNameInvalid =
        new("tenants.demo_request.restaurant_name_invalid", "The restaurant name must be between 2 and 100 characters on a single line.");

    public static readonly DomainError CountryInvalid =
        new("tenants.demo_request.country_invalid", "The country is required (60 characters at most).");

    public static readonly DomainError PlanInvalid =
        new("tenants.demo_request.plan_invalid", "The plan name is too long.");

    public static readonly DomainError ContactMissing =
        new("tenants.demo_request.contact_missing", "An email address or a phone number is required to answer the request.");

    public static readonly DomainError MessageInvalid =
        new("tenants.demo_request.message_invalid", "The message is limited to 2000 characters.");

    public static readonly DomainError NoteInvalid =
        new("tenants.demo_request.note_invalid", "The note is limited to 1000 characters.");

    public static readonly DomainError StatusInvalid =
        new("tenants.demo_request.status_invalid", "Unknown demo request status.");
}
