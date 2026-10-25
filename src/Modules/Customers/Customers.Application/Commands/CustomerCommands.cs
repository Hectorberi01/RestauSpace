using Customers.Domain;

namespace Customers.Application.Commands;

// La fiche client du restaurant : données minimales (nom, email et/ou téléphone selon le réglage du restaurant, tags,
// préférences). Les règles sont celles de l'agrégat Customer ; ici, d'où vient la règle de contact et l'ordre des étapes.

/// <summary>
/// Crée une fiche active. Les coordonnées exigées sont celles du réglage du restaurant au moment de la création (puis
/// figées sur la fiche). Tags et préférences facultatifs. Aucune unicité de l'email ou du téléphone : rechercher d'abord
/// (<c>SearchCustomersQuery</c>) pour éviter un doublon. Renvoie l'id.
/// </summary>
public sealed record CreateCustomerCommand(
    string? Name,
    string? Email = null,
    string? Phone = null,
    IReadOnlyList<string>? Tags = null,
    PreferencesInput? Preferences = null) : ICommand<Guid>;

/// <summary>Langue (<c>fr</c>, <c>fr-FR</c>...) et canaux marketing SOUHAITÉS (un souhait, jamais un consentement).</summary>
public sealed record PreferencesInput(string? Language, IReadOnlyList<CommunicationChannel>? MarketingChannels = null)
{
    public CustomerPreference ToPreference() => CustomerPreference.Create(Language, MarketingChannels);
}

public sealed class CreateCustomerHandler(CustomersWorkspace workspace, IClock clock) : ICommandHandler<CreateCustomerCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateCustomerCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        if (command.Tags is { Count: > 0 })
        {
            // Étiquettes : gestion avancée des clients.
            await workspace.RequireFeatureAsync(PlanFeatures.CustomersAdvanced, cancellationToken);
        }

        var tenant = workspace.CurrentTenant;
        var policy = await workspace.Settings.GetContactPolicyAsync(tenant, cancellationToken);

        var customer = Customer.Create(
            tenant,
            command.Name ?? string.Empty,
            CustomersWorkspace.OptionalEmail(command.Email),
            CustomersWorkspace.OptionalPhone(command.Phone),
            clock,
            policy);
        foreach (var tag in (command.Tags ?? Array.Empty<string>()).Select(CustomerTag.Create).Distinct())
        {
            customer.AddTag(tag);
        }

        if (command.Preferences is { } preferences)
        {
            customer.UpdatePreferences(preferences.ToPreference());
        }

        workspace.Customers.Add(customer);
        await workspace.SaveAsync(cancellationToken);
        return customer.Id.Value;
    }
}

public sealed record RenameCustomerCommand(Guid CustomerId, string? Name) : ICommand;

public sealed class RenameCustomerHandler(CustomersWorkspace workspace) : ICommandHandler<RenameCustomerCommand>
{
    public Task HandleAsync(RenameCustomerCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditCustomerAsync(command.CustomerId, customer => customer.Rename(command.Name ?? string.Empty), cancellationToken);
}

/// <summary>Remplace email ET téléphone (un champ vide efface la valeur), vérifiés selon la règle figée sur la fiche.</summary>
public sealed record UpdateCustomerContactCommand(Guid CustomerId, string? Email, string? Phone) : ICommand;

public sealed class UpdateCustomerContactHandler(CustomersWorkspace workspace, IClock clock) : ICommandHandler<UpdateCustomerContactCommand>
{
    public Task HandleAsync(UpdateCustomerContactCommand command, CancellationToken cancellationToken = default)
    {
        // Validés AVANT de charger : une saisie invalide n'ouvre pas de transaction.
        var email = CustomersWorkspace.OptionalEmail(command.Email);
        var phone = CustomersWorkspace.OptionalPhone(command.Phone);
        return workspace.EditCustomerAsync(command.CustomerId, customer => customer.UpdateContact(email, phone, clock), cancellationToken);
    }
}

/// <summary>
/// Applique à une fiche existante le réglage ACTUEL du restaurant (il n'est figé qu'à la création). Refusé
/// (<c>customers.customer.contact_required</c>) tant que les coordonnées de la fiche ne le satisfont pas : les compléter
/// d'abord.
/// </summary>
public sealed record ApplyContactPolicyCommand(Guid CustomerId) : ICommand;

public sealed class ApplyContactPolicyHandler(CustomersWorkspace workspace, IClock clock) : ICommandHandler<ApplyContactPolicyCommand>
{
    public async Task HandleAsync(ApplyContactPolicyCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var customer = await workspace.LoadCustomerAsync(tenant, command.CustomerId, cancellationToken);
        customer.ChangeContactRequirement(await workspace.Settings.GetContactPolicyAsync(tenant, cancellationToken), clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record AddCustomerTagCommand(Guid CustomerId, string? Tag) : ICommand;

public sealed class AddCustomerTagHandler(CustomersWorkspace workspace) : ICommandHandler<AddCustomerTagCommand>
{
    public async Task HandleAsync(AddCustomerTagCommand command, CancellationToken cancellationToken = default)
    {
        var tag = CustomerTag.Create(command.Tag);
        await workspace.RequireFeatureAsync(PlanFeatures.CustomersAdvanced, cancellationToken);
        await workspace.EditCustomerAsync(command.CustomerId, customer => customer.AddTag(tag), cancellationToken);
    }
}

public sealed record RemoveCustomerTagCommand(Guid CustomerId, string? Tag) : ICommand;

public sealed class RemoveCustomerTagHandler(CustomersWorkspace workspace) : ICommandHandler<RemoveCustomerTagCommand>
{
    public async Task HandleAsync(RemoveCustomerTagCommand command, CancellationToken cancellationToken = default)
    {
        var tag = CustomerTag.Create(command.Tag);
        await workspace.RequireFeatureAsync(PlanFeatures.CustomersAdvanced, cancellationToken);
        await workspace.EditCustomerAsync(command.CustomerId, customer => customer.RemoveTag(tag), cancellationToken);
    }
}

public sealed record UpdateCustomerPreferencesCommand(Guid CustomerId, PreferencesInput? Preferences) : ICommand;

public sealed class UpdateCustomerPreferencesHandler(CustomersWorkspace workspace) : ICommandHandler<UpdateCustomerPreferencesCommand>
{
    public Task HandleAsync(UpdateCustomerPreferencesCommand command, CancellationToken cancellationToken = default)
    {
        var preferences = (command.Preferences ?? new PreferencesInput(null)).ToPreference();
        return workspace.EditCustomerAsync(command.CustomerId, customer => customer.UpdatePreferences(preferences), cancellationToken);
    }
}

/// <summary>
/// Droit à l'effacement : efface DÉFINITIVEMENT les données personnelles de la fiche (nom neutre, ni contact, ni tags,
/// ni préférences), supprime les notes de l'équipe et retire, dans la MÊME transaction, chaque consentement encore
/// accordé (source système) : à aucun instant un client anonymisé ne reste joignable par une campagne. Les autres
/// modules effacent leurs copies sur l'événement <c>CustomerAnonymized</c> (réservations : contact et notes). Possible
/// même pour un restaurant suspendu : une obligation légale ne dépend pas de l'abonnement. Rejouer sur une fiche déjà
/// anonymisée est refusé (<c>customers.customer.anonymized</c>).
/// </summary>
public sealed record AnonymizeCustomerCommand(Guid CustomerId) : ICommand;

public sealed class AnonymizeCustomerHandler(CustomersWorkspace workspace, Abstractions.ICustomerNoteRepository notes, IClock clock)
    : ICommandHandler<AnonymizeCustomerCommand>
{
    public async Task HandleAsync(AnonymizeCustomerCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var customer = await workspace.LoadCustomerAsync(tenant, command.CustomerId, cancellationToken);
        customer.Anonymize(clock);
        foreach (var consent in await workspace.Consents.ListForCustomerAsync(tenant, customer.Id, cancellationToken))
        {
            consent.WithdrawBecauseCustomerAnonymized(clock);
        }

        foreach (var note in await notes.ListForCustomerAsync(tenant, customer.Id, cancellationToken))
        {
            notes.Remove(note);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}
