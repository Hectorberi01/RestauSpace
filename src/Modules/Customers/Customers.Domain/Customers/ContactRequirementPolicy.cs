namespace Customers.Domain;

public enum ContactRequirement
{
    None = 1,
    EmailOrPhone = 2,
    Email = 3,
    Phone = 4,
}

/// <summary>Coordonnées qu'un profil client doit contenir. Réglage du tenant, figé sur le client à sa création.</summary>
public sealed class ContactRequirementPolicy : ValueObject
{
    /// <summary>Au moins un email ou un numéro de téléphone.</summary>
    public static readonly ContactRequirementPolicy Default = new(ContactRequirement.EmailOrPhone);

    private ContactRequirementPolicy(ContactRequirement requirement) => Requirement = requirement;

    public ContactRequirement Requirement { get; }

    public static ContactRequirementPolicy Of(ContactRequirement requirement)
    {
        DomainException.ThrowIf(!Enum.IsDefined(requirement), CustomerErrors.ContactRequirementUnknown);

        return new ContactRequirementPolicy(requirement);
    }

    public bool IsSatisfiedBy(Email? email, PhoneNumber? phone) => Requirement switch
    {
        ContactRequirement.None => true,
        ContactRequirement.EmailOrPhone => email is not null || phone is not null,
        ContactRequirement.Email => email is not null,
        ContactRequirement.Phone => phone is not null,
        _ => false,
    };

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Requirement;
    }
}
