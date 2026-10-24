using Storefront.Application.Customers;

namespace Storefront.Application;

public sealed class CustomerContactMatchTests
{
    private static readonly Guid EmailOnly = Guid.NewGuid();
    private static readonly Guid PhoneOnly = Guid.NewGuid();
    private static readonly Guid Both = Guid.NewGuid();
    private static readonly Guid Mixed = Guid.NewGuid();

    private static CustomerContactCandidate E(Guid id) => new(id, true, false);

    private static CustomerContactCandidate P(Guid id) => new(id, false, true);

    private static CustomerContactCandidate EP(Guid id) => new(id, true, true);

    [Fact]
    public void A_proven_contact_only_opens_a_record_that_has_no_other_contact()
    {
        Assert.Null(CustomerContactMatch.Pick([EP(Mixed)], []));
        Assert.Null(CustomerContactMatch.Pick([], [EP(Mixed)]));
        Assert.Equal(PhoneOnly, CustomerContactMatch.Pick([], [EP(Mixed), P(PhoneOnly)]));
        Assert.Equal(EmailOnly, CustomerContactMatch.Pick([EP(Mixed), E(EmailOnly)], []));
    }

    [Fact]
    public void A_guest_joins_a_record_only_when_every_contact_on_it_was_given()
    {
        // L'e-mail donné est sur une fiche dont le téléphone n'est pas celui donné : refusée.
        Assert.Null(CustomerContactMatch.Pick([EP(Mixed)], []));
        Assert.Null(CustomerContactMatch.Pick([EP(Mixed)], [EP(Guid.NewGuid())]));
        // Même e-mail ET même téléphone.
        Assert.Equal(Both, CustomerContactMatch.Pick([EP(Both)], [EP(Both)]));
        // Fiche à un seul contact, donné : retenue, et préférée à la fiche aux deux contacts.
        Assert.Equal(EmailOnly, CustomerContactMatch.Pick([EP(Both), E(EmailOnly)], [EP(Both), P(PhoneOnly)]));
        Assert.Equal(PhoneOnly, CustomerContactMatch.Pick([EP(Both)], [EP(Both), P(PhoneOnly)]));
        Assert.Null(CustomerContactMatch.Pick([], []));
    }
}
