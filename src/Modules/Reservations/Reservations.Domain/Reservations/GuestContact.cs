namespace Reservations.Domain;

/// <summary>
/// Coordonnées du client telles que données à la réservation. Un instantané : il ne suit pas les modifications
/// ultérieures du client CRM, pour que la réservation reste joignable et fidèle par elle-même. <see cref="Anonymized"/>
/// est le seul contact sans téléphone ni email : il remplace l'instantané quand les données personnelles sont effacées.
/// </summary>
public sealed class GuestContact : ValueObject
{
    /// <summary>Même limite que le nom du client dans le module Customers.</summary>
    public const int NameMaxLength = 150;

    public const string AnonymizedName = "Anonymized guest";

    private GuestContact(string name, PhoneNumber? phone, Email? email)
    {
        Name = name;
        Phone = phone;
        Email = email;
    }

    public string Name { get; }

    public PhoneNumber? Phone { get; }

    public Email? Email { get; }

    /// <summary>Valeur neutre laissée après un effacement. Inaccessible via <see cref="Create"/>, qui exige un canal.</summary>
    public static GuestContact Anonymized { get; } = new(AnonymizedName, phone: null, email: null);

    public bool IsAnonymized => Phone is null && Email is null;

    public static GuestContact Create(string? name, PhoneNumber? phone, Email? email)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), ReservationErrors.GuestNameRequired);

        var trimmed = name!.Trim();
        DomainException.ThrowIf(trimmed.Length > NameMaxLength, ReservationErrors.GuestNameTooLong);
        DomainException.ThrowIf(phone is null && email is null, ReservationErrors.GuestContactChannelRequired);

        return new GuestContact(trimmed, phone, email);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Name;
        yield return Phone;
        yield return Email;
    }
}
