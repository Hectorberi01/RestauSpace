using Customers.Application.Abstractions;
using Customers.Application.Queries;
using Customers.Domain;

namespace Customers.Application.Commands;

// Outils du CRM : notes de l'équipe, fusion de doublons, export du droit d'accès et import d'un fichier de clients.

public sealed record CustomerNoteDto(Guid Id, string Text, Guid? AuthorId, DateTimeOffset CreatedAtUtc)
{
    public static CustomerNoteDto From(CustomerNote note) => new(note.Id.Value, note.Text, note.AuthorId?.Value, note.CreatedAtUtc);
}

/// <summary>Ajoute une note datée ; <paramref name="AuthorId"/> vient de l'access token (jamais du corps de la requête).</summary>
public sealed record AddCustomerNoteCommand(Guid CustomerId, string? Text, Guid? AuthorId) : ICommand<Guid>;

public sealed class AddCustomerNoteHandler(CustomersWorkspace workspace, ICustomerNoteRepository notes, IClock clock)
    : ICommandHandler<AddCustomerNoteCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddCustomerNoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var customer = await workspace.LoadCustomerAsync(workspace.CurrentTenant, command.CustomerId, cancellationToken);
        var note = CustomerNote.Write(
            customer.ToReference(), command.Text, command.AuthorId is { } author && author != Guid.Empty ? UserId.From(author) : null, clock);
        notes.Add(note);
        await workspace.SaveAsync(cancellationToken);
        return note.Id.Value;
    }
}

public sealed record DeleteCustomerNoteCommand(Guid CustomerId, Guid NoteId) : ICommand;

public sealed class DeleteCustomerNoteHandler(CustomersWorkspace workspace, ICustomerNoteRepository notes) : ICommandHandler<DeleteCustomerNoteCommand>
{
    public async Task HandleAsync(DeleteCustomerNoteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        var note = (command.NoteId == Guid.Empty ? null : await notes.FindAsync(tenant, CustomerNoteId.From(command.NoteId), cancellationToken))
            ?? throw new NotFoundException(CustomerNoteErrors.NotFound);
        if (note.CustomerId.Value != command.CustomerId)
        {
            throw new NotFoundException(CustomerNoteErrors.NotFound);
        }

        notes.Remove(note);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record ListCustomerNotesQuery(Guid CustomerId) : IQuery<IReadOnlyList<CustomerNoteDto>>;

public sealed class ListCustomerNotesHandler(CustomersWorkspace workspace, ICustomerNoteRepository notes)
    : IQueryHandler<ListCustomerNotesQuery, IReadOnlyList<CustomerNoteDto>>
{
    public async Task<IReadOnlyList<CustomerNoteDto>> HandleAsync(ListCustomerNotesQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var customer = await workspace.LoadCustomerAsync(tenant, query.CustomerId, cancellationToken);
        return (await notes.ListForCustomerAsync(tenant, customer.Id, cancellationToken))
            .OrderByDescending(note => note.CreatedAtUtc)
            .Select(CustomerNoteDto.From)
            .ToList();
    }
}

/// <summary>
/// Fusionne un doublon dans une fiche : coordonnées, préférences et tags manquants, notes, et pour chaque canal la
/// décision de consentement la plus récente des deux fiches (la preuve d'origine reste dans l'historique du doublon).
/// Le doublon garde son id (l'historique des autres modules y pointe jusqu'à ce qu'ils le rattachent, sur l'événement
/// <c>CustomerMerged</c>) mais plus aucune donnée personnelle ; ses consentements sont retirés.
/// </summary>
public sealed record MergeCustomersCommand(Guid TargetId, Guid DuplicateId) : ICommand;

public sealed class MergeCustomersHandler(
    CustomersWorkspace workspace, ICustomerNoteRepository notes, IConsentUniquenessChecker uniqueness, IClock clock)
    : ICommandHandler<MergeCustomersCommand>
{
    public static readonly ConsentSource MergeSource = ConsentSource.Create("customers.merge");

    public async Task HandleAsync(MergeCustomersCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        DomainException.ThrowIf(command.TargetId == command.DuplicateId, CustomerErrors.MergeWithItself);
        var tenant = workspace.CurrentTenant;
        var target = await workspace.LoadCustomerAsync(tenant, command.TargetId, cancellationToken);
        var duplicate = await workspace.LoadCustomerAsync(tenant, command.DuplicateId, cancellationToken);

        target.Absorb(duplicate, clock);
        var reference = target.ToReference();

        var targetConsents = (await workspace.Consents.ListForCustomerAsync(tenant, target.Id, cancellationToken)).ToDictionary(consent => consent.Channel);
        foreach (var consent in await workspace.Consents.ListForCustomerAsync(tenant, duplicate.Id, cancellationToken))
        {
            var existing = targetConsents.GetValueOrDefault(consent.Channel);
            if (consent.IsGranted && (existing is null || (!existing.IsGranted && LastDecision(consent) > LastDecision(existing))))
            {
                if (existing is null)
                {
                    workspace.Consents.Add(Consent.Grant(
                        reference, consent.Channel, MergeSource,
                        await uniqueness.IsUniqueAsync(tenant, target.Id, consent.Channel, cancellationToken), clock));
                }
                else
                {
                    existing.Regrant(reference, MergeSource, clock);
                }
            }
            else if (!consent.IsGranted && existing is { IsGranted: true } && LastDecision(consent) > LastDecision(existing))
            {
                existing.Withdraw(MergeSource, consent.WithdrawalReason ?? WithdrawalReason.CustomerRequest, clock);
            }

            if (consent.IsGranted)
            {
                consent.Withdraw(MergeSource, WithdrawalReason.Other, clock);
            }
        }

        foreach (var note in await notes.ListForCustomerAsync(tenant, duplicate.Id, cancellationToken))
        {
            note.MoveTo(reference);
        }

        await workspace.SaveAsync(cancellationToken);
    }

    private static DateTimeOffset LastDecision(Consent consent) =>
        consent.WithdrawnAtUtc is { } withdrawn && withdrawn > consent.GrantedAtUtc ? withdrawn : consent.GrantedAtUtc;
}

/// <summary>Tout ce que le module sait d'un client : fiche, consentements avec leur historique, notes.</summary>
public sealed record CustomerExportDto(
    DateTimeOffset ExportedAtUtc,
    CustomerDto Customer,
    IReadOnlyList<ConsentDto> Consents,
    IReadOnlyList<CustomerNoteDto> Notes);

/// <summary>Droit d'accès (RGPD) : l'export des données du client gardées par ce module.</summary>
public sealed record ExportCustomerDataQuery(Guid CustomerId) : IQuery<CustomerExportDto>;

public sealed class ExportCustomerDataHandler(CustomersWorkspace workspace, ICustomerNoteRepository notes, IClock clock)
    : IQueryHandler<ExportCustomerDataQuery, CustomerExportDto>
{
    public async Task<CustomerExportDto> HandleAsync(ExportCustomerDataQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var tenant = workspace.CurrentTenant;
        var customer = await workspace.LoadCustomerAsync(tenant, query.CustomerId, cancellationToken);
        var consents = await workspace.Consents.ListForCustomerAsync(tenant, customer.Id, cancellationToken);
        var written = await notes.ListForCustomerAsync(tenant, customer.Id, cancellationToken);
        return new CustomerExportDto(
            clock.UtcNow,
            CustomerDto.From(customer),
            consents.OrderBy(consent => consent.Channel).Select(ConsentDto.From).ToList(),
            written.OrderByDescending(note => note.CreatedAtUtc).Select(CustomerNoteDto.From).ToList());
    }
}

/// <summary>Une ligne du fichier importé. <paramref name="MarketingChannels"/> : canaux pour lesquels le client a consenti.</summary>
public sealed record CustomerImportRow(
    string? Name, string? Email, string? Phone, IReadOnlyList<string>? Tags = null, IReadOnlyList<CommunicationChannel>? MarketingChannels = null);

public enum CustomerImportOutcome
{
    Created = 1,
    Updated = 2,
    Skipped = 3,
    Invalid = 4,
}

/// <param name="Row">Position dans le fichier (à partir de 1).</param>
/// <param name="Error">Code de l'erreur d'une ligne refusée.</param>
public sealed record CustomerImportLineDto(int Row, CustomerImportOutcome Outcome, Guid? CustomerId, string? Error);

public sealed record CustomerImportResultDto(int Created, int Updated, int Skipped, int Invalid, IReadOnlyList<CustomerImportLineDto> Lines);

/// <summary>
/// Importe jusqu'à 500 clients. Une fiche existante (même email ou même téléphone) n'est pas dupliquée : elle reçoit les
/// tags manquants. Les consentements ne sont enregistrés que si l'équipe certifie les avoir recueillis
/// (<paramref name="ConsentCertified"/>), avec la source <paramref name="ConsentSource"/> ; sinon ils sont ignorés.
/// Chaque ligne est traitée et enregistrée seule : une ligne invalide n'empêche pas les autres.
/// </summary>
public sealed record ImportCustomersCommand(
    IReadOnlyList<CustomerImportRow>? Rows, bool ConsentCertified = false, string? ConsentSource = null) : ICommand<CustomerImportResultDto>
{
    public override string ToString() => $"ImportCustomersCommand ({Rows?.Count ?? 0} rows)";
}

public sealed class ImportCustomersHandler(CustomersWorkspace workspace, IConsentUniquenessChecker uniqueness, IClock clock)
    : ICommandHandler<ImportCustomersCommand, CustomerImportResultDto>
{
    public const int MaxRows = 500;

    public async Task<CustomerImportResultDto> HandleAsync(ImportCustomersCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var rows = command.Rows ?? [];
        DomainException.ThrowIf(rows.Count is 0 or > MaxRows, CustomersApplicationErrors.ImportSizeInvalid);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        await workspace.RequireFeatureAsync(PlanFeatures.CustomersAdvanced, cancellationToken);
        var tenant = workspace.CurrentTenant;
        var policy = await workspace.Settings.GetContactPolicyAsync(tenant, cancellationToken);
        var source = command.ConsentCertified ? ConsentSources.Of(string.IsNullOrWhiteSpace(command.ConsentSource) ? "import" : command.ConsentSource) : null;

        var lines = new List<CustomerImportLineDto>();
        for (var index = 0; index < rows.Count; index++)
        {
            lines.Add(await ImportAsync(tenant, policy, rows[index], index + 1, source, cancellationToken));
        }

        return new CustomerImportResultDto(
            lines.Count(line => line.Outcome == CustomerImportOutcome.Created),
            lines.Count(line => line.Outcome == CustomerImportOutcome.Updated),
            lines.Count(line => line.Outcome == CustomerImportOutcome.Skipped),
            lines.Count(line => line.Outcome == CustomerImportOutcome.Invalid),
            lines);
    }

    // Tout ce qui peut refuser une ligne est vérifié avant d'ajouter quoi que ce soit : une ligne refusée ne laisse rien
    // en attente pour la sauvegarde de la suivante.
    private async Task<CustomerImportLineDto> ImportAsync(
        TenantId tenant, ContactRequirementPolicy policy, CustomerImportRow row, int position, ConsentSource? source, CancellationToken cancellationToken)
    {
        try
        {
            var email = CustomersWorkspace.OptionalEmail(row.Email);
            var phone = CustomersWorkspace.OptionalPhone(row.Phone);
            var tags = (row.Tags ?? []).Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(CustomerTag.Create).Distinct().ToList();
            var channels = source is null ? [] : (row.MarketingChannels ?? []).Distinct().ToList();
            DomainException.ThrowIf(channels.Any(channel => !Enum.IsDefined(channel)), ConsentErrors.ChannelNotSupported);
            var existing = await FindExistingAsync(tenant, email, phone, cancellationToken);
            Customer customer;
            CustomerImportOutcome outcome;
            if (existing is not null)
            {
                customer = existing;
                var missing = tags.Where(tag => !customer.Tags.Contains(tag)).Take(Customer.MaxTags - customer.Tags.Count).ToList();
                missing.ForEach(customer.AddTag);
                outcome = missing.Count > 0 ? CustomerImportOutcome.Updated : CustomerImportOutcome.Skipped;
            }
            else
            {
                customer = Customer.Create(tenant, row.Name ?? string.Empty, email, phone, clock, policy);
                tags.Take(Customer.MaxTags).ToList().ForEach(customer.AddTag);
                workspace.Customers.Add(customer);
                outcome = CustomerImportOutcome.Created;
            }

            if (source is not null)
            {
                foreach (var channel in channels)
                {
                    var consent = await workspace.Consents.FindAsync(tenant, customer.Id, channel, cancellationToken);
                    if (consent is null)
                    {
                        workspace.Consents.Add(Consent.Grant(
                            customer.ToReference(), channel, source, await uniqueness.IsUniqueAsync(tenant, customer.Id, channel, cancellationToken), clock));
                        outcome = outcome == CustomerImportOutcome.Skipped ? CustomerImportOutcome.Updated : outcome;
                    }
                }
            }

            await workspace.SaveAsync(cancellationToken);
            return new CustomerImportLineDto(position, outcome, customer.Id.Value, null);
        }
        catch (DomainException exception)
        {
            return new CustomerImportLineDto(position, CustomerImportOutcome.Invalid, null, exception.Code);
        }
    }

    private async Task<Customer?> FindExistingAsync(TenantId tenant, Email? email, PhoneNumber? phone, CancellationToken cancellationToken)
    {
        if (email is not null
            && (await workspace.Customers.SearchAsync(tenant, new CustomerSearch(Email: email, Take: 1), cancellationToken)).FirstOrDefault() is { } byEmail)
        {
            return await workspace.Customers.FindAsync(tenant, byEmail.Id, cancellationToken);
        }

        if (phone is not null
            && (await workspace.Customers.SearchAsync(tenant, new CustomerSearch(Phone: phone, Take: 1), cancellationToken)).FirstOrDefault() is { } byPhone)
        {
            return await workspace.Customers.FindAsync(tenant, byPhone.Id, cancellationToken);
        }

        return null;
    }
}
