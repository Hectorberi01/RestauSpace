namespace Storefront.Application.Customers;

/// <summary>Une fiche active trouvée par Customers sur un e-mail ou un téléphone exact.</summary>
public sealed record CustomerContactCandidate(Guid Id, bool HasEmail, bool HasPhone);

/// <summary>
/// Choix de la fiche d'un client du site (commande invité ou connexion par code). Une fiche n'est retenue que si CHACUN
/// de ses contacts fait partie des contacts donnés : jamais une fiche qui porte un autre contact, qui peut être celui d'un
/// tiers. À la connexion, seul le contact prouvé est donné : la session ne s'ouvre que sur une fiche à ce seul contact.
/// Ordre : e-mail seul, puis téléphone seul, puis les deux.
/// </summary>
public static class CustomerContactMatch
{
    /// <param name="byEmail">Les fiches qui portent l'e-mail donné (vide sans e-mail).</param>
    /// <param name="byPhone">Les fiches qui portent le téléphone donné (vide sans téléphone).</param>
    public static Guid? Pick(IReadOnlyList<CustomerContactCandidate> byEmail, IReadOnlyList<CustomerContactCandidate> byPhone)
    {
        ArgumentNullException.ThrowIfNull(byEmail);
        ArgumentNullException.ThrowIfNull(byPhone);
        var withEmail = byEmail.Select(candidate => candidate.Id).ToHashSet();
        var withPhone = byPhone.Select(candidate => candidate.Id).ToHashSet();
        bool Fits(CustomerContactCandidate candidate) =>
            (!candidate.HasEmail || withEmail.Contains(candidate.Id)) && (!candidate.HasPhone || withPhone.Contains(candidate.Id));

        return byEmail.Where(candidate => !candidate.HasPhone)
            .Concat(byPhone.Where(candidate => !candidate.HasEmail))
            .Concat(byEmail.Where(candidate => candidate.HasPhone))
            .FirstOrDefault(Fits)?.Id;
    }
}
