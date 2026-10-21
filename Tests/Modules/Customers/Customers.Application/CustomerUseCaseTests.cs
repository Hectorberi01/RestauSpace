using Customers.Application.Commands;
using Customers.Application.Queries;
using Customers.Domain;

namespace Customers.Application;

public sealed class CustomerUseCaseTests
{
    private readonly CustomersFixture _fixture = new();

    private Task<Guid> CreateAsync(string name = "Awa Dossou", string? email = "awa@example.com", string? phone = null, IReadOnlyList<string>? tags = null) =>
        _fixture.Create.HandleAsync(new CreateCustomerCommand(name, email, phone, tags));

    [Fact]
    public async Task A_customer_is_created_with_normalized_contact_tags_and_preferences()
    {
        var id = await _fixture.Create.HandleAsync(new CreateCustomerCommand(
            "  Awa Dossou ",
            " Awa@Example.com ",
            "+229 97 00 00 00",
            ["VIP", " vip ", "Allergie arachide"],
            new PreferencesInput("fr-bj", [CommunicationChannel.Sms, CommunicationChannel.Sms])));

        var dto = await _fixture.GetById.HandleAsync(new GetCustomerByIdQuery(id));

        Assert.Equal(("Awa Dossou", "awa@example.com", "+22997000000"), (dto.Name, dto.Email, dto.Phone));
        Assert.Equal(new[] { "allergie arachide", "vip" }, dto.Tags.ToArray());
        Assert.Equal("fr-BJ", dto.Preferences!.Language);
        Assert.Equal(new[] { CommunicationChannel.Sms }, dto.Preferences.MarketingChannels.ToArray());
        Assert.Equal(ContactRequirement.EmailOrPhone, dto.ContactRequirement);
        Assert.False(dto.IsAnonymized);
        Assert.Equal(1, _fixture.SaveCount);
    }

    [Fact]
    public async Task The_contact_rule_comes_from_the_restaurant_setting_and_is_pinned_on_the_customer()
    {
        var refused = await Assert.ThrowsAsync<DomainException>(() => CreateAsync(email: null));
        await _fixture.SetPolicy.HandleAsync(new SetContactPolicyCommand(ContactRequirement.None));
        var walkIn = await CreateAsync("Client de passage", email: null);
        await _fixture.SetPolicy.HandleAsync(new SetContactPolicyCommand(ContactRequirement.Phone));

        Assert.Equal(CustomerErrors.ContactRequired, refused.Error);
        Assert.Equal(ContactRequirement.Phone, (await _fixture.GetPolicy.HandleAsync(new GetContactPolicyQuery())).Requirement);
        // La fiche garde la règle de sa création ; le nouveau réglage ne s'applique que sur demande, contact complété.
        Assert.Equal(ContactRequirement.None, _fixture.CustomerById(walkIn).ContactPolicy.Requirement);
        var notYet = await Assert.ThrowsAsync<DomainException>(() => _fixture.ApplyPolicy.HandleAsync(new ApplyContactPolicyCommand(walkIn)));
        await _fixture.UpdateContact.HandleAsync(new UpdateCustomerContactCommand(walkIn, null, "+22997000001"));
        await _fixture.ApplyPolicy.HandleAsync(new ApplyContactPolicyCommand(walkIn));

        Assert.Equal(CustomerErrors.ContactRequired, notYet.Error);
        Assert.Equal(ContactRequirement.Phone, _fixture.CustomerById(walkIn).ContactPolicy.Requirement);
    }

    [Fact]
    public async Task A_customer_of_another_restaurant_is_never_found()
    {
        var id = await CreateAsync();
        _fixture.CurrentTenantId = _fixture.TenantB;

        var read = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetById.HandleAsync(new GetCustomerByIdQuery(id)));
        var renamed = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Rename.HandleAsync(new RenameCustomerCommand(id, "Pirate")));
        var erased = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Anonymize.HandleAsync(new AnonymizeCustomerCommand(id)));
        var consent = await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.Grant.HandleAsync(new GrantConsentCommand(id, CommunicationChannel.Email)));
        var reference = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetReference.HandleAsync(new GetCustomerReferenceQuery(id)));

        Assert.All(new[] { read, renamed, erased, consent, reference }, error => Assert.Equal(CustomersApplicationErrors.CustomerNotFound, error.Error));
        Assert.Empty(await _fixture.Search.HandleAsync(new SearchCustomersQuery()));
        Assert.Equal("Awa Dossou", _fixture.CustomerById(id).Name);
        Assert.Empty(_fixture.Consents);
    }

    [Fact]
    public async Task A_suspended_restaurant_reads_its_customers_and_still_honours_withdrawals_and_erasure()
    {
        var id = await CreateAsync();
        await _fixture.Grant.HandleAsync(new GrantConsentCommand(id, CommunicationChannel.Email, "pos"));
        _fixture.Writable[_fixture.TenantA] = false;

        var created = await Assert.ThrowsAsync<DomainException>(() => CreateAsync("Kofi"));
        var tagged = await Assert.ThrowsAsync<DomainException>(() => _fixture.AddTag.HandleAsync(new AddCustomerTagCommand(id, "vip")));
        await _fixture.Withdraw.HandleAsync(new WithdrawConsentCommand(id, CommunicationChannel.Email, WithdrawalReason.UnsubscribeLink, "unsubscribe-link"));
        await _fixture.Anonymize.HandleAsync(new AnonymizeCustomerCommand(id));

        Assert.All(new[] { created, tagged }, error => Assert.Equal(CustomersApplicationErrors.TenantNotWritable, error.Error));
        Assert.True((await _fixture.GetById.HandleAsync(new GetCustomerByIdQuery(id))).IsAnonymized);
    }

    [Fact]
    public async Task Search_finds_by_name_part_exact_contact_or_tag_and_hides_anonymized_customers()
    {
        var awa = await CreateAsync("Awa Dossou", "awa@example.com", tags: ["vip"]);
        var kofi = await CreateAsync("Kofi Mensah", null, "+22997000002");
        var erased = await CreateAsync("Awa Houngbo", "awa.h@example.com");
        await _fixture.Anonymize.HandleAsync(new AnonymizeCustomerCommand(erased));

        async Task<Guid[]> Find(SearchCustomersQuery query) => (await _fixture.Search.HandleAsync(query)).Select(dto => dto.Id).ToArray();

        Assert.Equal(new[] { awa }, await Find(new SearchCustomersQuery(Name: "awa")));
        Assert.Equal(new[] { awa }, await Find(new SearchCustomersQuery(Email: " AWA@example.com ")));
        Assert.Empty(await Find(new SearchCustomersQuery(Email: "awa@example.org")));
        Assert.Equal(new[] { kofi }, await Find(new SearchCustomersQuery(Phone: "+229 97-00-00-02")));
        Assert.Equal(new[] { awa }, await Find(new SearchCustomersQuery(Tag: "VIP")));
        Assert.Equal(3, (await Find(new SearchCustomersQuery(IncludeAnonymized: true))).Length);
        Assert.Equal(new[] { kofi }, await Find(new SearchCustomersQuery(Skip: 1, Take: 1)));
        var page = await Assert.ThrowsAsync<DomainException>(() => Find(new SearchCustomersQuery(Take: 101)));
        Assert.Equal(CustomersApplicationErrors.PageInvalid, page.Error);
    }

    [Fact]
    public async Task Tags_and_preferences_are_changed_through_the_aggregate()
    {
        var id = await CreateAsync(tags: ["vip"]);

        await _fixture.AddTag.HandleAsync(new AddCustomerTagCommand(id, "Terrasse"));
        await _fixture.RemoveTag.HandleAsync(new RemoveCustomerTagCommand(id, "VIP"));
        var twice = await Assert.ThrowsAsync<DomainException>(() => _fixture.AddTag.HandleAsync(new AddCustomerTagCommand(id, "terrasse")));
        var missing = await Assert.ThrowsAsync<DomainException>(() => _fixture.RemoveTag.HandleAsync(new RemoveCustomerTagCommand(id, "vip")));
        await _fixture.UpdatePreferences.HandleAsync(new UpdateCustomerPreferencesCommand(id, new PreferencesInput("en", [CommunicationChannel.Email])));
        var noLanguage = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.UpdatePreferences.HandleAsync(new UpdateCustomerPreferencesCommand(id, null)));

        var dto = await _fixture.GetById.HandleAsync(new GetCustomerByIdQuery(id));
        Assert.Equal(new[] { "terrasse" }, dto.Tags.ToArray());
        Assert.Equal("en", dto.Preferences!.Language);
        Assert.Equal(
            (CustomerErrors.TagAlreadyPresent, CustomerErrors.TagNotFound, CustomerErrors.LanguageInvalid),
            (twice.Error, missing.Error, noLanguage.Error));
    }

    [Fact]
    public async Task A_consent_is_granted_withdrawn_and_granted_again_with_its_whole_history()
    {
        var id = await CreateAsync();

        await _fixture.Grant.HandleAsync(new GrantConsentCommand(id, CommunicationChannel.Email, "web-signup-form"));
        var twice = await Assert.ThrowsAsync<DomainException>(() => _fixture.Grant.HandleAsync(new GrantConsentCommand(id, CommunicationChannel.Email)));
        _fixture.Clock.Advance(TimeSpan.FromDays(1));
        await _fixture.Withdraw.HandleAsync(new WithdrawConsentCommand(id, CommunicationChannel.Email, WithdrawalReason.UnsubscribeLink, "unsubscribe-link"));
        _fixture.Clock.Advance(TimeSpan.FromDays(1));
        await _fixture.Grant.HandleAsync(new GrantConsentCommand(id, CommunicationChannel.Email));
        var never = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Withdraw.HandleAsync(new WithdrawConsentCommand(id, CommunicationChannel.Sms)));
        var phrase = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.Grant.HandleAsync(new GrantConsentCommand(id, CommunicationChannel.Sms, "Accord donné par Awa au téléphone")));

        var consent = Assert.Single(await _fixture.ListConsents.HandleAsync(new ListCustomerConsentsQuery(id)));
        Assert.True(consent.IsGranted);
        Assert.Equal(ConsentSources.BackOffice, consent.Source);
        Assert.Equal(
            new[] { (ConsentStatus.Granted, "web-signup-form"), (ConsentStatus.Withdrawn, "unsubscribe-link"), (ConsentStatus.Granted, "back-office") },
            consent.History.Select(change => (change.Status, change.Source)).ToArray());
        Assert.Equal(WithdrawalReason.UnsubscribeLink, consent.History[1].Reason);
        Assert.Equal(
            (ConsentErrors.AlreadyGranted, CustomersApplicationErrors.ConsentNotFound, ConsentErrors.SourceInvalid),
            (twice.Error, never.Error, phrase.Error));
    }

    [Fact]
    public async Task Anonymizing_erases_the_customer_and_withdraws_every_consent_in_one_save()
    {
        var id = await CreateAsync(phone: "+22997000003", tags: ["vip"]);
        await _fixture.UpdatePreferences.HandleAsync(new UpdateCustomerPreferencesCommand(id, new PreferencesInput("fr")));
        await _fixture.Grant.HandleAsync(new GrantConsentCommand(id, CommunicationChannel.Email));
        await _fixture.Grant.HandleAsync(new GrantConsentCommand(id, CommunicationChannel.Sms));
        await _fixture.Withdraw.HandleAsync(new WithdrawConsentCommand(id, CommunicationChannel.Sms));
        var saves = _fixture.SaveCount;

        await _fixture.Anonymize.HandleAsync(new AnonymizeCustomerCommand(id));

        Assert.Equal(saves + 1, _fixture.SaveCount);
        var dto = await _fixture.GetById.HandleAsync(new GetCustomerByIdQuery(id));
        Assert.Equal((Customer.AnonymizedName, (string?)null, (string?)null, 0, (PreferencesDto?)null), (dto.Name, dto.Email, dto.Phone, dto.Tags.Count, dto.Preferences));
        var consents = await _fixture.ListConsents.HandleAsync(new ListCustomerConsentsQuery(id));
        Assert.All(consents, consent => Assert.False(consent.IsGranted));
        // L'e-mail est retiré par le système ; le SMS déjà retiré garde son retrait d'origine.
        Assert.Equal(WithdrawalReason.CustomerAnonymized, consents.Single(consent => consent.Channel == CommunicationChannel.Email).WithdrawalReason);
        Assert.Equal(WithdrawalReason.CustomerRequest, consents.Single(consent => consent.Channel == CommunicationChannel.Sms).WithdrawalReason);
        Assert.True((await _fixture.GetReference.HandleAsync(new GetCustomerReferenceQuery(id))).IsAnonymized);
        var again = await Assert.ThrowsAsync<DomainException>(() => _fixture.Anonymize.HandleAsync(new AnonymizeCustomerCommand(id)));
        var regrant = await Assert.ThrowsAsync<DomainException>(() => _fixture.Grant.HandleAsync(new GrantConsentCommand(id, CommunicationChannel.Email)));
        Assert.Equal((CustomerErrors.Anonymized, ConsentErrors.CustomerAnonymized), (again.Error, regrant.Error));
    }

    [Fact]
    public async Task Marketing_needs_a_granted_consent_and_the_address_of_the_channel_never_a_preference()
    {
        var id = await CreateAsync(email: "awa@example.com");
        await _fixture.UpdatePreferences.HandleAsync(new UpdateCustomerPreferencesCommand(
            id, new PreferencesInput("fr", [CommunicationChannel.Email, CommunicationChannel.Sms])));

        async Task<string?> Why(CommunicationChannel channel) =>
            (await _fixture.MarketingPermission.HandleAsync(new GetMarketingPermissionQuery(id, channel))).Reason;

        Assert.Equal("no_consent", await Why(CommunicationChannel.Email));
        await _fixture.Grant.HandleAsync(new GrantConsentCommand(id, CommunicationChannel.Email));
        await _fixture.Grant.HandleAsync(new GrantConsentCommand(id, CommunicationChannel.Sms));
        Assert.Null(await Why(CommunicationChannel.Email));
        Assert.Equal("no_contact", await Why(CommunicationChannel.Sms));
        await _fixture.Withdraw.HandleAsync(new WithdrawConsentCommand(id, CommunicationChannel.Email));
        Assert.Equal("consent_withdrawn", await Why(CommunicationChannel.Email));
        await _fixture.Anonymize.HandleAsync(new AnonymizeCustomerCommand(id));
        Assert.Equal("anonymized", await Why(CommunicationChannel.Sms));
    }

    [Fact]
    public async Task Other_modules_only_learn_the_tenant_the_id_and_the_anonymized_flag()
    {
        var id = await CreateAsync();

        var reference = await _fixture.GetReference.HandleAsync(new GetCustomerReferenceQuery(id));

        Assert.Equal(new CustomerReferenceDto(_fixture.TenantA.Value, id, false), reference);
        Assert.Equal(3, typeof(CustomerReferenceDto).GetProperties().Length);
    }
}
