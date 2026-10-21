namespace Loyalty.Domain;

/// <summary>
/// Le grand livre stocké est consulté via le port, mais les écritures ajoutées depuis le chargement ne sont pas encore
/// enregistrées : le compte doit refuser de passer deux fois la même référence ou d'annuler deux fois la même écriture
/// dans une même unit of work.
/// </summary>
public class UnsavedLedgerGuardTests
{
    private readonly FakeClock _clock = new();

    private LoyaltyAccount Account()
    {
        var tenant = TenantId.New();
        return LoyaltyAccount.Open(tenant, new CustomerReference(tenant, CustomerId.New(), false), true, _clock);
    }

    [Fact]
    public void Same_reference_cannot_be_earned_twice_before_the_first_entry_is_saved()
    {
        var account = Account();
        var reference = TransactionReference.Create("order-1");
        account.Earn(Points.Of(10), reference, referenceAlreadyPosted: false, _clock);

        DomainAssert.Throws(
            LoyaltyAccountErrors.DuplicateTransaction,
            () => account.Earn(Points.Of(10), reference, referenceAlreadyPosted: false, _clock));

        Assert.Equal(10, account.Balance);
        Assert.Single(account.Transactions);
    }

    [Fact]
    public void Same_reference_may_be_used_once_per_transaction_type_within_one_unit_of_work()
    {
        var account = Account();
        var reference = TransactionReference.Create("order-1");

        account.Earn(Points.Of(10), reference, referenceAlreadyPosted: false, _clock);
        account.Redeem(Points.Of(4), reference, referenceAlreadyPosted: false, _clock);

        Assert.Equal(6, account.Balance);
    }

    [Fact]
    public void Same_entry_cannot_be_reversed_twice_before_the_reversal_is_saved()
    {
        var account = Account();
        account.Earn(Points.Of(10), TransactionReference.Create("order-1"), referenceAlreadyPosted: false, _clock);
        var original = account.Transactions.Single().ToPostedReference(isAlreadyReversed: false);
        account.ReverseEarn(original, "order cancelled", _clock);

        DomainAssert.Throws(
            LoyaltyAccountErrors.AlreadyReversed,
            () => account.ReverseEarn(original, "order cancelled", _clock));

        Assert.Equal(0, account.Balance);
        Assert.Equal(2, account.Transactions.Count);
    }
}
