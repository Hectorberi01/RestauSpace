using Customers.Application.Commands;
using Customers.Domain;

namespace Customers.Api;

public sealed record CreatedResponse(Guid Id);

/// <param name="Preferences">Facultatif : { language, marketingChannels? }.</param>
public sealed record CreateCustomerRequest(
    string? Name,
    string? Email = null,
    string? Phone = null,
    IReadOnlyList<string>? Tags = null,
    PreferencesInput? Preferences = null);

public sealed record NameRequest(string? Name);

/// <summary>Remplace les deux : un champ absent ou vide efface la valeur.</summary>
public sealed record ContactRequest(string? Email, string? Phone);

public sealed record TagRequest(string? Tag);

/// <param name="Source">Code court de l'endroit où le client a donné son accord ; « back-office » si absent.</param>
public sealed record GrantConsentRequest(CommunicationChannel Channel, string? Source = null);

/// <param name="Reason">Motif (liste fermée) ; « CustomerRequest » si absent.</param>
public sealed record WithdrawConsentRequest(CommunicationChannel Channel, WithdrawalReason? Reason = null, string? Source = null);

public sealed record ContactPolicyRequest(ContactRequirement Requirement);

public sealed record NoteRequest(string? Text);

/// <param name="DuplicateId">La fiche en double, absorbée par celle de l'adresse.</param>
public sealed record MergeRequest(Guid DuplicateId);

/// <param name="ConsentCertified">L'équipe certifie avoir recueilli les consentements marketing indiqués dans le fichier.</param>
/// <param name="ConsentSource">Code court de l'origine des consentements (« import » par défaut).</param>
public sealed record ImportCustomersRequest(IReadOnlyList<CustomerImportRow>? Rows, bool ConsentCertified = false, string? ConsentSource = null)
{
    public override string ToString() => $"ImportCustomersRequest ({Rows?.Count ?? 0} rows)";
}
