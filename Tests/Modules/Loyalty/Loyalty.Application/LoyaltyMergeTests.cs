using Loyalty.Application.Commands;
using Loyalty.Domain;

namespace Loyalty.Application;

public sealed class LoyaltyMergeTests
{
    private readonly LoyaltyFixture _fixture = new();

    private async Task<(Guid Customer, Guid Account)> AccountWithPointsAsync(int points)
    {
        var customer = _fixture.NewCustomer();
        var account = await _fixture.Open.HandleAsync(new OpenLoyaltyAccountCommand(customer));
        if (points > 0)
        {
            await _fixture.Adjust.HandleAsync(new AdjustLoyaltyPointsCommand(account, points, LedgerDirection.Credit, "Reprise"));
        }

        return (customer, account);
    }

    [Fact]
    public async Task The_points_of_the_duplicate_join_the_kept_account_and_the_duplicate_closes()
    {
        var (duplicate, duplicateAccount) = await AccountWithPointsAsync(120);
        var (kept, keptAccount) = await AccountWithPointsAsync(30);

        var first = await _fixture.MergeCustomers.HandleAsync(new MergeCustomerLoyaltyAccountsCommand(duplicate, kept));
        var again = await _fixture.MergeCustomers.HandleAsync(new MergeCustomerLoyaltyAccountsCommand(duplicate, kept));

        Assert.Equal((LoyaltyReactionOutcome.Done, LoyaltyReactionOutcome.AlreadyDone), (first, again));
        Assert.Equal(150, _fixture.AccountById(keptAccount).Balance);
        Assert.Equal((LoyaltyAccountStatus.Closed, 0L), (_fixture.AccountById(duplicateAccount).Status, _fixture.AccountById(duplicateAccount).Balance));
    }

    [Fact]
    public async Task A_kept_customer_without_account_gets_one_with_the_points()
    {
        var (duplicate, _) = await AccountWithPointsAsync(80);
        var kept = _fixture.NewCustomer();

        await _fixture.MergeCustomers.HandleAsync(new MergeCustomerLoyaltyAccountsCommand(duplicate, kept));

        var account = _fixture.Accounts.Single(candidate => candidate.CustomerId.Value == kept);
        Assert.Equal(80, account.Balance);
    }

    [Fact]
    public async Task A_duplicate_without_account_changes_nothing()
    {
        var outcome = await _fixture.MergeCustomers.HandleAsync(new MergeCustomerLoyaltyAccountsCommand(_fixture.NewCustomer(), _fixture.NewCustomer()));

        Assert.Equal(LoyaltyReactionOutcome.Ignored, outcome);
        Assert.Empty(_fixture.Accounts);
    }
}
