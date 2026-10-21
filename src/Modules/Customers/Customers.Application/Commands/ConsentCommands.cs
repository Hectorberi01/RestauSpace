using Customers.Domain;

namespace Customers.Application.Commands;

// Consentements marketing : la PREUVE légale (une par client et par canal, historique conservé), distincte des
// préférences de la fiche. La source est un code court (« pos », « web-signup-form »...), jamais une phrase : elle ne
// peut pas porter de donnée personnelle.

/// <summary>Source enregistrée quand le personnel ne précise rien : saisie depuis le back-office du restaurant.</summary>
public static class ConsentSources
{
    public const string BackOffice = "back-office";

    public static ConsentSource Of(string? source) => ConsentSource.Create(string.IsNullOrWhiteSpace(source) ? BackOffice : source);
}

/// <summary>
/// Enregistre le consentement du client sur un canal : premier octroi (crée la preuve) ou nouvel octroi après un retrait
/// (l'historique garde le retrait). Refusé pour un client anonymisé et quand le consentement est déjà accordé.
/// </summary>
public sealed record GrantConsentCommand(Guid CustomerId, CommunicationChannel Channel, string? Source = null) : ICommand;

public sealed class GrantConsentHandler(CustomersWorkspace workspace, IConsentUniquenessChecker uniqueness, IClock clock)
    : ICommandHandler<GrantConsentCommand>
{
    public async Task HandleAsync(GrantConsentCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        var source = ConsentSources.Of(command.Source);
        var customer = (await workspace.LoadCustomerAsync(tenant, command.CustomerId, cancellationToken)).ToReference();

        var existing = Enum.IsDefined(command.Channel)
            ? await workspace.Consents.FindAsync(tenant, customer.CustomerId, command.Channel, cancellationToken)
            : null;
        if (existing is null)
        {
            // Le canal est vérifié par le domaine ; l'index unique (client, canal) reste le dernier garde-fou.
            workspace.Consents.Add(Consent.Grant(
                customer,
                command.Channel,
                source,
                await uniqueness.IsUniqueAsync(tenant, customer.CustomerId, command.Channel, cancellationToken),
                clock));
        }
        else
        {
            existing.Regrant(customer, source, clock);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Retire le consentement du client sur un canal (date, source et motif enregistrés). Jamais bloqué : ni par
/// l'anonymisation, ni par la suspension du restaurant (un désabonnement doit toujours aboutir).
/// </summary>
public sealed record WithdrawConsentCommand(
    Guid CustomerId, CommunicationChannel Channel, WithdrawalReason Reason = WithdrawalReason.CustomerRequest, string? Source = null) : ICommand;

public sealed class WithdrawConsentHandler(CustomersWorkspace workspace, IClock clock) : ICommandHandler<WithdrawConsentCommand>
{
    public async Task HandleAsync(WithdrawConsentCommand command, CancellationToken cancellationToken = default)
    {
        var tenant = workspace.CurrentTenant;
        var source = ConsentSources.Of(command.Source);
        var customer = await workspace.LoadCustomerAsync(tenant, command.CustomerId, cancellationToken);
        var consent = await workspace.Consents.FindAsync(tenant, customer.Id, command.Channel, cancellationToken)
            ?? throw new NotFoundException(CustomersApplicationErrors.ConsentNotFound);

        consent.Withdraw(source, command.Reason, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Règle de coordonnées exigées à la création d'une fiche. Ne modifie pas les fiches existantes (la règle y est figée) ;
/// <c>ApplyContactPolicyCommand</c> l'applique à une fiche donnée.
/// </summary>
public sealed record SetContactPolicyCommand(ContactRequirement Requirement) : ICommand;

public sealed class SetContactPolicyHandler(CustomersWorkspace workspace, IClock clock) : ICommandHandler<SetContactPolicyCommand>
{
    public async Task HandleAsync(SetContactPolicyCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var policy = ContactRequirementPolicy.Of(command.Requirement);
        await workspace.Settings.SetContactPolicyAsync(workspace.CurrentTenant, policy, clock.UtcNow, cancellationToken);
        await workspace.SaveAsync(cancellationToken);
    }
}
