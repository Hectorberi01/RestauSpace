namespace Customers.Domain;

public class CustomerTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly Email AdaEmail = Email.Create("ada@example.com");
    private static readonly PhoneNumber AdaPhone = PhoneNumber.Create("+22997000000");

    private readonly FakeClock _clock = new();

    private Customer NewCustomer(bool withEmail = true, bool withPhone = true)
    {
        var customer = Customer.Create(Tenant, "Ada Lovelace", withEmail ? AdaEmail : null, withPhone ? AdaPhone : null, _clock);
        customer.ClearDomainEvents();
        return customer;
    }

    private Customer NewAnonymizedCustomer()
    {
        var customer = NewCustomer();
        customer.AddTag(CustomerTag.Create("vip"));
        customer.UpdatePreferences(CustomerPreference.Create("fr", [CommunicationChannel.Email]));
        customer.Anonymize(_clock);
        customer.ClearDomainEvents();
        return customer;
    }

    // ---- Create

    [Fact]
    public void Create_builds_an_active_customer_and_raises_CustomerCreated()
    {
        var customer = Customer.Create(Tenant, "  Ada Lovelace ", AdaEmail, AdaPhone, _clock);

        Assert.Equal(Tenant, customer.TenantId);
        Assert.Equal("Ada Lovelace", customer.Name);
        Assert.Equal(AdaEmail, customer.Email);
        Assert.Equal(AdaPhone, customer.Phone);
        Assert.Equal(CustomerStatus.Active, customer.Status);
        Assert.False(customer.IsAnonymized);
        Assert.Empty(customer.Tags);
        Assert.Null(customer.Preferences);
        Assert.Equal(_clock.UtcNow, customer.CreatedAtUtc);
        Assert.Null(customer.AnonymizedAtUtc);
        Assert.Equal(new CustomerCreated(Tenant, customer.Id, _clock.UtcNow), DomainAssert.Raised<CustomerCreated>(customer));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_name(string name) =>
        DomainAssert.Throws(CustomerErrors.NameRequired, () => Customer.Create(Tenant, name, AdaEmail, null, _clock));

    [Fact]
    public void Create_accepts_a_name_at_the_limit_and_rejects_a_longer_one()
    {
        var atLimit = Customer.Create(Tenant, new string('a', Customer.NameMaxLength), AdaEmail, null, _clock);

        Assert.Equal(Customer.NameMaxLength, atLimit.Name.Length);
        DomainAssert.Throws(
            CustomerErrors.NameTooLong,
            () => Customer.Create(Tenant, new string('a', Customer.NameMaxLength + 1), AdaEmail, null, _clock));
    }

    [Fact]
    public void Create_with_the_default_policy_accepts_an_email_only_or_a_phone_only()
    {
        Assert.Null(Customer.Create(Tenant, "Ada", AdaEmail, null, _clock).Phone);
        Assert.Null(Customer.Create(Tenant, "Ada", null, AdaPhone, _clock).Email);
    }

    [Fact]
    public void Create_with_the_default_policy_rejects_a_customer_without_any_contact() =>
        DomainAssert.Throws(CustomerErrors.ContactRequired, () => Customer.Create(Tenant, "Ada", null, null, _clock));

    [Fact]
    public void Create_applies_the_explicit_contact_policy()
    {
        var none = ContactRequirementPolicy.Of(ContactRequirement.None);
        var emailRequired = ContactRequirementPolicy.Of(ContactRequirement.Email);
        var phoneRequired = ContactRequirementPolicy.Of(ContactRequirement.Phone);

        Assert.Null(Customer.Create(Tenant, "Walk-in", null, null, _clock, none).Email);
        DomainAssert.Throws(CustomerErrors.ContactRequired, () => Customer.Create(Tenant, "Ada", null, AdaPhone, _clock, emailRequired));
        DomainAssert.Throws(CustomerErrors.ContactRequired, () => Customer.Create(Tenant, "Ada", AdaEmail, null, _clock, phoneRequired));
        Assert.NotNull(Customer.Create(Tenant, "Ada", AdaEmail, null, _clock, emailRequired).Email);
        Assert.NotNull(Customer.Create(Tenant, "Ada", null, AdaPhone, _clock, phoneRequired).Phone);
    }

    // ---- Rename

    [Fact]
    public void Rename_changes_the_name()
    {
        var customer = NewCustomer();

        customer.Rename(" Ada King ");

        Assert.Equal("Ada King", customer.Name);
    }

    [Fact]
    public void Rename_rejects_a_blank_or_a_too_long_name()
    {
        var customer = NewCustomer();

        DomainAssert.Throws(CustomerErrors.NameRequired, () => customer.Rename(" "));
        DomainAssert.Throws(CustomerErrors.NameTooLong, () => customer.Rename(new string('a', Customer.NameMaxLength + 1)));
        Assert.Equal("Ada Lovelace", customer.Name);
    }

    [Fact]
    public void Rename_to_the_current_name_is_a_silent_no_op()
    {
        var customer = NewCustomer();

        customer.Rename(" Ada Lovelace ");

        Assert.Equal("Ada Lovelace", customer.Name);
        DomainAssert.NoEvents(customer);
    }

    // ---- UpdateContact

    [Fact]
    public void UpdateContact_replaces_the_contact_and_raises_CustomerContactChanged()
    {
        var customer = NewCustomer();
        var newEmail = Email.Create("ada.king@example.com");
        _clock.Advance(TimeSpan.FromDays(1));

        customer.UpdateContact(newEmail, AdaPhone, _clock);

        Assert.Equal(newEmail, customer.Email);
        Assert.Equal(AdaPhone, customer.Phone);
        Assert.Equal(
            new CustomerContactChanged(Tenant, customer.Id, EmailChanged: true, PhoneChanged: false, _clock.UtcNow),
            DomainAssert.Raised<CustomerContactChanged>(customer));
    }

    [Fact]
    public void UpdateContact_can_remove_one_identifier_while_the_other_remains()
    {
        var customer = NewCustomer();

        customer.UpdateContact(null, AdaPhone, _clock);

        Assert.Null(customer.Email);
        var changed = DomainAssert.Raised<CustomerContactChanged>(customer);
        Assert.True(changed.EmailChanged);
        Assert.False(changed.PhoneChanged);
    }

    [Fact]
    public void UpdateContact_rejects_removing_the_last_contact_under_the_default_policy()
    {
        var customer = NewCustomer();

        DomainAssert.Throws(CustomerErrors.ContactRequired, () => customer.UpdateContact(null, null, _clock));

        Assert.Equal(AdaEmail, customer.Email);
        Assert.Equal(AdaPhone, customer.Phone);
        DomainAssert.NoEvents(customer);
    }

    [Fact]
    public void UpdateContact_applies_the_policy_pinned_at_creation()
    {
        var emailRequired = Customer.Create(
            Tenant, "Ada", AdaEmail, AdaPhone, _clock, ContactRequirementPolicy.Of(ContactRequirement.Email));
        var walkIn = Customer.Create(Tenant, "Walk-in", AdaEmail, null, _clock, ContactRequirementPolicy.Of(ContactRequirement.None));

        DomainAssert.Throws(CustomerErrors.ContactRequired, () => emailRequired.UpdateContact(null, AdaPhone, _clock));
        Assert.Equal(AdaEmail, emailRequired.Email);

        walkIn.UpdateContact(null, null, _clock);
        Assert.Null(walkIn.Email);
        Assert.Null(walkIn.Phone);
    }

    [Fact]
    public void UpdateContact_cannot_be_given_another_policy()
    {
        var parameters = typeof(Customer).GetMethod(nameof(Customer.UpdateContact))!.GetParameters();

        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(ContactRequirementPolicy));
    }

    [Fact]
    public void UpdateContact_with_the_current_contact_details_is_a_silent_no_op()
    {
        var customer = NewCustomer();

        customer.UpdateContact(Email.Create(" ADA@example.com "), PhoneNumber.Create("+229 97 00 00 00"), _clock);

        Assert.Equal(AdaEmail, customer.Email);
        Assert.Equal(AdaPhone, customer.Phone);
        DomainAssert.NoEvents(customer);
    }

    // ---- ChangeContactRequirement

    [Fact]
    public void Create_pins_the_contact_policy()
    {
        var phoneRequired = ContactRequirementPolicy.Of(ContactRequirement.Phone);

        Assert.Equal(ContactRequirementPolicy.Default, NewCustomer().ContactPolicy);
        Assert.Equal(phoneRequired, Customer.Create(Tenant, "Ada", null, AdaPhone, _clock, phoneRequired).ContactPolicy);
    }

    [Fact]
    public void ChangeContactRequirement_replaces_the_pinned_policy_and_raises_CustomerContactRequirementChanged()
    {
        var customer = NewCustomer();
        var emailRequired = ContactRequirementPolicy.Of(ContactRequirement.Email);
        _clock.Advance(TimeSpan.FromDays(1));

        customer.ChangeContactRequirement(emailRequired, _clock);

        Assert.Equal(emailRequired, customer.ContactPolicy);
        Assert.Equal(
            new CustomerContactRequirementChanged(Tenant, customer.Id, ContactRequirement.Email, _clock.UtcNow),
            DomainAssert.Raised<CustomerContactRequirementChanged>(customer));
        DomainAssert.Throws(CustomerErrors.ContactRequired, () => customer.UpdateContact(null, AdaPhone, _clock));
    }

    [Fact]
    public void ChangeContactRequirement_rejects_a_policy_the_current_contact_does_not_satisfy()
    {
        var customer = NewCustomer(withPhone: false);

        DomainAssert.Throws(
            CustomerErrors.ContactRequired,
            () => customer.ChangeContactRequirement(ContactRequirementPolicy.Of(ContactRequirement.Phone), _clock));

        Assert.Equal(ContactRequirementPolicy.Default, customer.ContactPolicy);
        DomainAssert.NoEvents(customer);
    }

    [Fact]
    public void ChangeContactRequirement_can_relax_the_policy()
    {
        var customer = NewCustomer();

        customer.ChangeContactRequirement(ContactRequirementPolicy.Of(ContactRequirement.None), _clock);
        customer.UpdateContact(null, null, _clock);

        Assert.Null(customer.Email);
        Assert.Null(customer.Phone);
    }

    [Fact]
    public void ChangeContactRequirement_to_the_current_policy_is_a_silent_no_op()
    {
        var customer = NewCustomer();

        customer.ChangeContactRequirement(ContactRequirementPolicy.Of(ContactRequirement.EmailOrPhone), _clock);

        Assert.Equal(ContactRequirementPolicy.Default, customer.ContactPolicy);
        DomainAssert.NoEvents(customer);
    }

    // ---- Tags

    [Fact]
    public void AddTag_adds_a_normalized_tag()
    {
        var customer = NewCustomer();

        customer.AddTag(CustomerTag.Create("  VIP "));

        Assert.Equal("vip", Assert.Single(customer.Tags).Value);
    }

    [Fact]
    public void AddTag_rejects_a_tag_already_present_whatever_its_casing()
    {
        var customer = NewCustomer();
        customer.AddTag(CustomerTag.Create("vip"));

        DomainAssert.Throws(CustomerErrors.TagAlreadyPresent, () => customer.AddTag(CustomerTag.Create(" Vip ")));
        Assert.Single(customer.Tags);
    }

    [Fact]
    public void AddTag_rejects_more_tags_than_the_maximum()
    {
        var customer = NewCustomer();
        for (var index = 0; index < Customer.MaxTags; index++)
        {
            customer.AddTag(CustomerTag.Create($"tag-{index}"));
        }

        DomainAssert.Throws(CustomerErrors.TooManyTags, () => customer.AddTag(CustomerTag.Create("one-too-many")));
        Assert.Equal(Customer.MaxTags, customer.Tags.Count);
    }

    [Fact]
    public void RemoveTag_removes_the_tag()
    {
        var customer = NewCustomer();
        customer.AddTag(CustomerTag.Create("vip"));
        customer.AddTag(CustomerTag.Create("regular"));

        customer.RemoveTag(CustomerTag.Create("VIP"));

        Assert.Equal("regular", Assert.Single(customer.Tags).Value);
    }

    [Fact]
    public void RemoveTag_rejects_a_tag_the_customer_does_not_have()
    {
        var customer = NewCustomer();

        DomainAssert.Throws(CustomerErrors.TagNotFound, () => customer.RemoveTag(CustomerTag.Create("vip")));
    }

    [Fact]
    public void Tags_cannot_be_modified_from_outside()
    {
        var customer = NewCustomer();

        var exposed = Assert.IsAssignableFrom<ICollection<CustomerTag>>(customer.Tags);

        Assert.True(exposed.IsReadOnly);
    }

    // ---- Preferences

    [Fact]
    public void UpdatePreferences_replaces_the_preferences()
    {
        var customer = NewCustomer();
        var preferences = CustomerPreference.Create("en-GB", [CommunicationChannel.Sms]);

        customer.UpdatePreferences(preferences);

        Assert.Equal(preferences, customer.Preferences);
        DomainAssert.NoEvents(customer);
    }

    [Fact]
    public void UpdatePreferences_with_identical_preferences_is_a_silent_no_op()
    {
        var customer = NewCustomer();
        var preferences = CustomerPreference.Create("fr", [CommunicationChannel.Email, CommunicationChannel.Sms]);
        customer.UpdatePreferences(preferences);

        customer.UpdatePreferences(CustomerPreference.Create("FR", [CommunicationChannel.Sms, CommunicationChannel.Email]));

        Assert.Equal(preferences, customer.Preferences);
        DomainAssert.NoEvents(customer);
    }

    // ---- Anonymize

    [Fact]
    public void Anonymize_erases_every_personal_datum_and_keeps_identity_and_tenant()
    {
        var customer = NewCustomer();
        var id = customer.Id;
        var createdAt = customer.CreatedAtUtc;
        customer.AddTag(CustomerTag.Create("vip"));
        customer.UpdatePreferences(CustomerPreference.Create("fr", [CommunicationChannel.Email]));
        _clock.Advance(TimeSpan.FromDays(30));

        customer.Anonymize(_clock);

        Assert.Equal(CustomerStatus.Anonymized, customer.Status);
        Assert.True(customer.IsAnonymized);
        Assert.Equal(Customer.AnonymizedName, customer.Name);
        Assert.Null(customer.Email);
        Assert.Null(customer.Phone);
        Assert.Empty(customer.Tags);
        Assert.Null(customer.Preferences);
        Assert.Equal(id, customer.Id);
        Assert.Equal(Tenant, customer.TenantId);
        Assert.Equal(createdAt, customer.CreatedAtUtc);
        Assert.Equal(_clock.UtcNow, customer.AnonymizedAtUtc);
    }

    [Fact]
    public void Anonymize_raises_CustomerAnonymized_without_personal_data()
    {
        var customer = NewCustomer();

        customer.Anonymize(_clock);

        Assert.Equal(new CustomerAnonymized(Tenant, customer.Id, _clock.UtcNow), DomainAssert.Raised<CustomerAnonymized>(customer));
    }

    [Fact]
    public void Anonymize_is_irreversible_and_rejected_a_second_time()
    {
        var customer = NewAnonymizedCustomer();
        var anonymizedAt = customer.AnonymizedAtUtc;
        _clock.Advance(TimeSpan.FromDays(1));

        DomainAssert.Throws(CustomerErrors.Anonymized, () => customer.Anonymize(_clock));

        Assert.Equal(anonymizedAt, customer.AnonymizedAtUtc);
        DomainAssert.NoEvents(customer);
    }

    [Fact]
    public void Anonymized_customer_rejects_Rename()
    {
        var customer = NewAnonymizedCustomer();

        DomainAssert.Throws(CustomerErrors.Anonymized, () => customer.Rename("Ada Lovelace"));
        Assert.Equal(Customer.AnonymizedName, customer.Name);
    }

    [Fact]
    public void Anonymized_customer_rejects_UpdateContact()
    {
        var customer = NewAnonymizedCustomer();

        DomainAssert.Throws(CustomerErrors.Anonymized, () => customer.UpdateContact(AdaEmail, AdaPhone, _clock));
        Assert.Null(customer.Email);
        Assert.Null(customer.Phone);
        DomainAssert.NoEvents(customer);
    }

    [Fact]
    public void Anonymized_customer_rejects_ChangeContactRequirement()
    {
        var customer = NewAnonymizedCustomer();

        DomainAssert.Throws(
            CustomerErrors.Anonymized,
            () => customer.ChangeContactRequirement(ContactRequirementPolicy.Of(ContactRequirement.None), _clock));
        Assert.Equal(ContactRequirementPolicy.Default, customer.ContactPolicy);
        DomainAssert.NoEvents(customer);
    }

    [Fact]
    public void Anonymized_customer_rejects_AddTag()
    {
        var customer = NewAnonymizedCustomer();

        DomainAssert.Throws(CustomerErrors.Anonymized, () => customer.AddTag(CustomerTag.Create("vip")));
        Assert.Empty(customer.Tags);
    }

    [Fact]
    public void Anonymized_customer_rejects_RemoveTag()
    {
        var customer = NewAnonymizedCustomer();

        DomainAssert.Throws(CustomerErrors.Anonymized, () => customer.RemoveTag(CustomerTag.Create("vip")));
    }

    [Fact]
    public void Anonymized_customer_rejects_UpdatePreferences()
    {
        var customer = NewAnonymizedCustomer();

        DomainAssert.Throws(CustomerErrors.Anonymized, () => customer.UpdatePreferences(CustomerPreference.Create("fr")));
        Assert.Null(customer.Preferences);
    }

    [Fact]
    public void ToReference_carries_the_tenant_the_id_and_the_anonymized_flag()
    {
        var reference = NewCustomer().ToReference();

        Assert.Equal(
            [nameof(CustomerReference.CustomerId), nameof(CustomerReference.IsAnonymized), nameof(CustomerReference.TenantId)],
            typeof(CustomerReference).GetProperties().Select(property => property.Name).Order());
        Assert.Equal(Tenant, reference.TenantId);
        Assert.False(reference.IsAnonymized);
    }

    [Fact]
    public void ToReference_reflects_the_anonymization()
    {
        var customer = NewCustomer();

        var before = customer.ToReference();
        customer.Anonymize(_clock);
        var after = customer.ToReference();

        Assert.Equal((customer.Id, Tenant, false), (before.CustomerId, before.TenantId, before.IsAnonymized));
        Assert.Equal((customer.Id, Tenant, true), (after.CustomerId, after.TenantId, after.IsAnonymized));
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void A_CustomerReference_can_only_be_obtained_from_a_customer()
    {
        // Ni constructeur public ni fabrique : une référence ne peut pas être forgée à partir d'ids isolés.
        Assert.Empty(typeof(CustomerReference).GetConstructors());
        Assert.DoesNotContain(
            typeof(CustomerReference).GetMethods(),
            method => method.IsStatic && method.ReturnType == typeof(CustomerReference));
    }

    [Fact]
    public void Create_rejects_an_empty_tenant()
    {
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => Customer.Create(default, "Ada Lovelace", AdaEmail, AdaPhone, _clock));
    }

    [Fact]
    public void Anonymize_stamps_the_state_and_the_event_with_the_same_instant()
    {
        var customer = NewCustomer();
        var ticking = new TickingClock(_clock.UtcNow);

        customer.Anonymize(ticking);

        Assert.Equal(customer.AnonymizedAtUtc, DomainAssert.Raised<CustomerAnonymized>(customer).OccurredAtUtc);
    }

    /// <summary>Avance à chaque lecture : prouve qu'une opération ne lit l'horloge qu'une fois.</summary>
    private sealed class TickingClock(DateTimeOffset start) : IClock
    {
        private DateTimeOffset _now = start;

        public DateTimeOffset UtcNow => _now = _now.AddSeconds(1);
    }
}
