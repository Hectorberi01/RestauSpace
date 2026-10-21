namespace Locations.Domain.Locations;

/// <summary>
/// Coordonnées publiques de l'établissement, affichées aux clients (site, application) : téléphone, WhatsApp et e-mail,
/// chacun facultatif. Ce sont des coordonnées professionnelles, publiées telles quelles.
/// </summary>
public sealed class LocationContact : ValueObject
{
    public static readonly LocationContact None = new(null, null, null);

    private LocationContact(PhoneNumber? phone, PhoneNumber? whatsApp, Email? email)
    {
        Phone = phone;
        WhatsApp = whatsApp;
        Email = email;
    }

    public PhoneNumber? Phone { get; }
    public PhoneNumber? WhatsApp { get; }
    public Email? Email { get; }
    public bool IsEmpty => Phone is null && WhatsApp is null && Email is null;

    public static LocationContact Create(string? phone, string? whatsApp, string? email) => new(
        string.IsNullOrWhiteSpace(phone) ? null : PhoneNumber.Create(phone),
        string.IsNullOrWhiteSpace(whatsApp) ? null : PhoneNumber.Create(whatsApp),
        string.IsNullOrWhiteSpace(email) ? null : Email.Create(email));

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Phone;
        yield return WhatsApp;
        yield return Email;
    }

    public override string ToString() => "LocationContact(***)";
}
