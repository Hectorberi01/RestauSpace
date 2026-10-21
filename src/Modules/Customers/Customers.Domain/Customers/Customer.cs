namespace Customers.Domain;

public sealed class Customer : AggregateRoot<CustomerId>, ITenantOwned
{
    public const int NameMaxLength = 150;
    public const int MaxTags = 20;

    /// <summary>Nom neutre laissé sur un profil anonymisé.</summary>
    public const string AnonymizedName = "Anonymized customer";

    /// <summary>Nom neutre laissé sur un doublon fusionné.</summary>
    public const string MergedName = "Merged customer";

    private readonly List<CustomerTag> _tags = [];

    private Customer(
        CustomerId id,
        TenantId tenantId,
        string name,
        Email? email,
        PhoneNumber? phone,
        ContactRequirementPolicy contactPolicy,
        DateTimeOffset createdAtUtc) : base(id)
    {
        TenantId = tenantId;
        Name = name;
        Email = email;
        Phone = phone;
        ContactPolicy = contactPolicy;
        Status = CustomerStatus.Active;
        CreatedAtUtc = createdAtUtc;
    }

    // Réservé à l'ORM.
    private Customer()
    {
    }

    public TenantId TenantId { get; private set; }

    public string Name { get; private set; } = null!;

    public Email? Email { get; private set; }

    public PhoneNumber? Phone { get; private set; }

    /// <summary>
    /// Réglage du tenant appliqué à la création et figé ici : tout changement de coordonnées ultérieur est vérifié
    /// par rapport à lui. Il ne change que via <see cref="ChangeContactRequirement"/>.
    /// </summary>
    public ContactRequirementPolicy ContactPolicy { get; private set; } = null!;

    public CustomerStatus Status { get; private set; }

    public IReadOnlyCollection<CustomerTag> Tags => _tags.AsReadOnly();

    /// <summary>Null tant que le client n'a pas exprimé de préférences, et de nouveau après anonymisation.</summary>
    public CustomerPreference? Preferences { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? AnonymizedAtUtc { get; private set; }

    /// <summary>La fiche qui a absorbé ce doublon ; null sinon.</summary>
    public CustomerId? MergedIntoId { get; private set; }

    public DateTimeOffset? MergedAtUtc { get; private set; }

    public bool IsAnonymized => Status == CustomerStatus.Anonymized;

    public bool IsMerged => Status == CustomerStatus.Merged;

    /// <param name="contactPolicy">Exigence de contact du tenant ; <see cref="ContactRequirementPolicy.Default"/> si null.</param>
    public static Customer Create(
        TenantId tenantId,
        string name,
        Email? email,
        PhoneNumber? phone,
        IClock clock,
        ContactRequirementPolicy? contactPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);

        var normalizedName = NormalizeName(name);
        var policy = contactPolicy ?? ContactRequirementPolicy.Default;
        EnsureContact(email, phone, policy);

        var customer = new Customer(CustomerId.New(), tenantId, normalizedName, email, phone, policy, clock.UtcNow);
        customer.Raise(new CustomerCreated(tenantId, customer.Id, customer.CreatedAtUtc));

        return customer;
    }

    public void Rename(string name)
    {
        EnsureActive();

        Name = NormalizeName(name);
    }

    /// <summary>Vérifié selon la <see cref="ContactPolicy"/> figée : un appelant ne peut pas l'assouplir pour un appel.</summary>
    public void UpdateContact(Email? email, PhoneNumber? phone, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureActive();
        EnsureContact(email, phone, ContactPolicy);

        var emailChanged = email != Email;
        var phoneChanged = phone != Phone;
        if (!emailChanged && !phoneChanged)
        {
            return;
        }

        Email = email;
        Phone = phone;
        Raise(new CustomerContactChanged(TenantId, Id, emailChanged, phoneChanged, clock.UtcNow));
    }

    /// <summary>Applique un nouveau réglage du tenant. Rejeté tant que les coordonnées actuelles ne le satisfont pas.</summary>
    public void ChangeContactRequirement(ContactRequirementPolicy contactPolicy, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(contactPolicy);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureActive();

        if (contactPolicy == ContactPolicy)
        {
            return;
        }

        EnsureContact(Email, Phone, contactPolicy);

        ContactPolicy = contactPolicy;
        Raise(new CustomerContactRequirementChanged(TenantId, Id, contactPolicy.Requirement, clock.UtcNow));
    }

    public void AddTag(CustomerTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        EnsureActive();
        DomainException.ThrowIf(_tags.Contains(tag), CustomerErrors.TagAlreadyPresent);
        DomainException.ThrowIf(_tags.Count >= MaxTags, CustomerErrors.TooManyTags);

        _tags.Add(tag);
    }

    public void RemoveTag(CustomerTag tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        EnsureActive();
        DomainException.ThrowIf(!_tags.Remove(tag), CustomerErrors.TagNotFound);
    }

    public void UpdatePreferences(CustomerPreference preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        EnsureActive();

        Preferences = preferences;
    }

    /// <summary>Effacement irréversible des données personnelles. Id et tenant restent : l'historique pointe encore quelque part.</summary>
    public void Anonymize(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        EnsureActive();

        var now = clock.UtcNow;
        Name = AnonymizedName;
        Email = null;
        Phone = null;
        Preferences = null;
        _tags.Clear();
        Status = CustomerStatus.Anonymized;
        AnonymizedAtUtc = now;

        Raise(new CustomerAnonymized(TenantId, Id, now));
    }

    /// <summary>
    /// Absorbe un doublon du même restaurant : les coordonnées, préférences et tags qui manquent ici sont repris, puis le
    /// doublon perd ses données personnelles et pointe vers cette fiche. Les consentements et les notes sont repris par
    /// l'Application ; l'historique des autres modules suit l'événement <see cref="CustomerMerged"/>.
    /// </summary>
    public void Absorb(Customer duplicate, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(duplicate);
        ArgumentNullException.ThrowIfNull(clock);
        EnsureActive();
        duplicate.EnsureActive();
        this.EnsureSameTenant(duplicate.TenantId);
        DomainException.ThrowIf(duplicate.Id == Id, CustomerErrors.MergeWithItself);

        var now = clock.UtcNow;
        var email = Email ?? duplicate.Email;
        var phone = Phone ?? duplicate.Phone;
        if (email != Email || phone != Phone)
        {
            Raise(new CustomerContactChanged(TenantId, Id, email != Email, phone != Phone, now));
            Email = email;
            Phone = phone;
        }

        Preferences ??= duplicate.Preferences;
        // Une copie de chaque tag : celui du doublon reste attaché au doublon (qui le perd ensuite).
        foreach (var tag in duplicate._tags.Where(tag => !_tags.Contains(tag)).Take(MaxTags - _tags.Count).ToList())
        {
            _tags.Add(CustomerTag.Create(tag.Value));
        }

        duplicate.Name = MergedName;
        duplicate.Email = null;
        duplicate.Phone = null;
        duplicate.Preferences = null;
        duplicate._tags.Clear();
        duplicate.Status = CustomerStatus.Merged;
        duplicate.MergedIntoId = Id;
        duplicate.MergedAtUtc = now;
        duplicate.Raise(new CustomerMerged(TenantId, duplicate.Id, Id, now));
    }

    /// <summary>
    /// Ce qu'un autre agrégat ou module peut savoir de ce client : tenant, id, anonymisé ou non.
    /// Seul moyen d'obtenir une <see cref="CustomerReference"/> : elle ne peut pas être forgée à partir d'ids isolés.
    /// </summary>
    public CustomerReference ToReference() => new(Id, TenantId, Status != CustomerStatus.Active);

    private void EnsureActive()
    {
        DomainException.ThrowIf(IsAnonymized, CustomerErrors.Anonymized);
        DomainException.ThrowIf(IsMerged, CustomerErrors.Merged);
    }

    private static void EnsureContact(Email? email, PhoneNumber? phone, ContactRequirementPolicy contactPolicy) =>
        DomainException.ThrowIf(!contactPolicy.IsSatisfiedBy(email, phone), CustomerErrors.ContactRequired);

    private static string NormalizeName(string name)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), CustomerErrors.NameRequired);

        var normalized = name.Trim();
        DomainException.ThrowIf(normalized.Length > NameMaxLength, CustomerErrors.NameTooLong);

        return normalized;
    }
}
