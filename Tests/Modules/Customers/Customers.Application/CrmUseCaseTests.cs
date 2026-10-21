using Customers.Application.Commands;
using Customers.Domain;

namespace Customers.Application;

public sealed class CrmUseCaseTests
{
    private readonly CustomersFixture _fixture = new();

    private Task<Guid> CreateAsync(string name, string? email = null, string? phone = null, IReadOnlyList<string>? tags = null) =>
        _fixture.Create.HandleAsync(new CreateCustomerCommand(name, email, phone, tags));

    [Fact]
    public async Task Notes_are_listed_newest_first_and_removed_with_the_anonymized_customer()
    {
        var customer = await CreateAsync("Awa Dossou", "awa@example.com");
        var author = Guid.NewGuid();
        await _fixture.AddNote.HandleAsync(new AddCustomerNoteCommand(customer, " Allergique aux arachides ", author));
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await _fixture.AddNote.HandleAsync(new AddCustomerNoteCommand(customer, "Préfère la terrasse", author));

        var notes = await _fixture.ListNotes.HandleAsync(new ListCustomerNotesQuery(customer));
        await _fixture.Anonymize.HandleAsync(new AnonymizeCustomerCommand(customer));

        Assert.Equal([second, notes[1].Id], notes.Select(note => note.Id));
        Assert.Equal(("Allergique aux arachides", author), (notes[1].Text, notes[1].AuthorId));
        Assert.Empty(_fixture.Notes);
    }

    [Fact]
    public async Task A_note_of_another_customer_is_not_found()
    {
        var awa = await CreateAsync("Awa", "awa@example.com");
        var koffi = await CreateAsync("Koffi", "koffi@example.com");
        var note = await _fixture.AddNote.HandleAsync(new AddCustomerNoteCommand(awa, "Note", null));

        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.DeleteNote.HandleAsync(new DeleteCustomerNoteCommand(koffi, note)));
        await _fixture.DeleteNote.HandleAsync(new DeleteCustomerNoteCommand(awa, note));

        Assert.Empty(_fixture.Notes);
    }

    [Fact]
    public async Task Merging_takes_contact_tags_notes_and_the_latest_consent_decision()
    {
        var target = await CreateAsync("Awa Dossou", "awa@example.com", tags: ["vip"]);
        var duplicate = await CreateAsync("Awa D.", phone: "+22997000001", tags: ["terrasse"]);
        await _fixture.Grant.HandleAsync(new GrantConsentCommand(target, CommunicationChannel.Sms));
        _fixture.Clock.Advance(TimeSpan.FromDays(1));
        await _fixture.Withdraw.HandleAsync(new WithdrawConsentCommand(target, CommunicationChannel.Sms));
        _fixture.Clock.Advance(TimeSpan.FromDays(1));
        await _fixture.Grant.HandleAsync(new GrantConsentCommand(duplicate, CommunicationChannel.Sms));
        await _fixture.Grant.HandleAsync(new GrantConsentCommand(duplicate, CommunicationChannel.WhatsApp));
        await _fixture.AddNote.HandleAsync(new AddCustomerNoteCommand(duplicate, "Vient le dimanche", null));

        await _fixture.Merge.HandleAsync(new MergeCustomersCommand(target, duplicate));

        var kept = _fixture.CustomerById(target);
        var merged = _fixture.CustomerById(duplicate);
        Assert.Equal(("awa@example.com", "+22997000001"), (kept.Email!.Value, kept.Phone!.Value));
        Assert.Equal(["terrasse", "vip"], kept.Tags.Select(tag => tag.Value).Order());
        Assert.True(_fixture.Consents.Single(consent => consent.CustomerId == kept.Id && consent.Channel == CommunicationChannel.Sms).IsGranted);
        Assert.True(_fixture.Consents.Single(consent => consent.CustomerId == kept.Id && consent.Channel == CommunicationChannel.WhatsApp).IsGranted);
        Assert.All(_fixture.Consents.Where(consent => consent.CustomerId == merged.Id), consent => Assert.False(consent.IsGranted));
        Assert.Equal(kept.Id, _fixture.Notes.Single().CustomerId);
        Assert.Equal((CustomerStatus.Merged, kept.Id, null), (merged.Status, merged.MergedIntoId!.Value, merged.Email));
        Assert.Contains(merged.DomainEvents, domainEvent => domainEvent is CustomerMerged);
    }

    [Fact]
    public async Task A_withdrawal_more_recent_than_the_duplicate_consent_is_kept()
    {
        var target = await CreateAsync("Awa", "awa@example.com");
        var duplicate = await CreateAsync("Awa bis", "awa2@example.com");
        await _fixture.Grant.HandleAsync(new GrantConsentCommand(duplicate, CommunicationChannel.Email));
        _fixture.Clock.Advance(TimeSpan.FromDays(1));
        await _fixture.Grant.HandleAsync(new GrantConsentCommand(target, CommunicationChannel.Email));
        _fixture.Clock.Advance(TimeSpan.FromDays(1));
        await _fixture.Withdraw.HandleAsync(new WithdrawConsentCommand(target, CommunicationChannel.Email));

        await _fixture.Merge.HandleAsync(new MergeCustomersCommand(target, duplicate));

        Assert.False(_fixture.Consents.Single(consent => consent.CustomerId.Value == target).IsGranted);
    }

    [Fact]
    public async Task A_merged_customer_can_no_longer_change_or_be_merged_again()
    {
        var target = await CreateAsync("Awa", "awa@example.com");
        var duplicate = await CreateAsync("Awa bis", "awa2@example.com");
        await _fixture.Merge.HandleAsync(new MergeCustomersCommand(target, duplicate));

        var rename = await Assert.ThrowsAsync<DomainException>(() => _fixture.Rename.HandleAsync(new RenameCustomerCommand(duplicate, "Retour")));
        var again = await Assert.ThrowsAsync<DomainException>(() => _fixture.Merge.HandleAsync(new MergeCustomersCommand(target, duplicate)));
        var itself = await Assert.ThrowsAsync<DomainException>(() => _fixture.Merge.HandleAsync(new MergeCustomersCommand(target, target)));
        var reference = await _fixture.GetReference.HandleAsync(new Queries.GetCustomerReferenceQuery(duplicate));

        Assert.Equal(CustomerErrors.Merged, rename.Error);
        Assert.Equal(CustomerErrors.Merged, again.Error);
        Assert.Equal(CustomerErrors.MergeWithItself, itself.Error);
        Assert.True(reference.IsAnonymized);
    }

    [Fact]
    public async Task The_export_holds_the_record_consents_and_notes()
    {
        var customer = await CreateAsync("Awa", "awa@example.com");
        await _fixture.Grant.HandleAsync(new GrantConsentCommand(customer, CommunicationChannel.Email, "pos"));
        await _fixture.AddNote.HandleAsync(new AddCustomerNoteCommand(customer, "Note", null));

        var export = await _fixture.Export.HandleAsync(new ExportCustomerDataQuery(customer));

        Assert.Equal("awa@example.com", export.Customer.Email);
        Assert.Equal("pos", export.Consents.Single().History.Single().Source);
        Assert.Equal("Note", export.Notes.Single().Text);
    }

    [Fact]
    public async Task An_import_creates_completes_or_rejects_each_line()
    {
        var existing = await CreateAsync("Awa", "awa@example.com", tags: ["vip"]);

        var result = await _fixture.Import.HandleAsync(new ImportCustomersCommand(
            [
                new CustomerImportRow("Koffi", null, "+22997000002", ["habitue"], [CommunicationChannel.WhatsApp]),
                new CustomerImportRow("Awa D.", "AWA@example.com", null, ["terrasse"]),
                new CustomerImportRow("Sans contact", null, null),
                new CustomerImportRow("Awa encore", "awa@example.com", null, ["vip"]),
            ],
            ConsentCertified: true));

        Assert.Equal((1, 1, 1, 1), (result.Created, result.Updated, result.Skipped, result.Invalid));
        Assert.Equal([CustomerImportOutcome.Created, CustomerImportOutcome.Updated, CustomerImportOutcome.Invalid, CustomerImportOutcome.Skipped],
            result.Lines.Select(line => line.Outcome));
        Assert.Equal("customers.customer.contact_required", result.Lines[2].Error);
        Assert.Contains(_fixture.CustomerById(existing).Tags, tag => tag.Value == "terrasse");
        var koffi = _fixture.CustomerById(result.Lines[0].CustomerId!.Value);
        Assert.Equal("import", _fixture.Consents.Single(consent => consent.CustomerId == koffi.Id).Source.Value);
    }

    [Fact]
    public async Task Without_certification_imported_consents_are_ignored()
    {
        var result = await _fixture.Import.HandleAsync(new ImportCustomersCommand(
            [new CustomerImportRow("Koffi", null, "+22997000002", null, [CommunicationChannel.Sms])]));

        Assert.Equal(1, result.Created);
        Assert.Empty(_fixture.Consents);
    }
}
