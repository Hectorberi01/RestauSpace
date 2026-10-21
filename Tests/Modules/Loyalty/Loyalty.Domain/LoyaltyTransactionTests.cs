using System.Reflection;
using Loyalty.Domain;

namespace Loyalty.Domain;

public class LoyaltyTransactionTests
{
    private const BindingFlags Declared =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly Type Transaction = typeof(LoyaltyTransaction);

    [Fact]
    public void Transaction_properties_have_no_setter()
    {
        var properties = Transaction.GetProperties(Declared);

        Assert.NotEmpty(properties);
        Assert.All(properties, property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void Transaction_inherited_id_cannot_be_set_from_outside()
    {
        var id = Transaction.GetProperty(nameof(LoyaltyTransaction.Id))!;

        Assert.True(id.DeclaringType!.GetProperty(id.Name)!.SetMethod!.IsPrivate);
    }

    [Fact]
    public void Transaction_fields_are_all_read_only() =>
        Assert.All(Transaction.GetFields(Declared), field => Assert.True(field.IsInitOnly));

    [Fact]
    public void Transaction_exposes_no_mutator_method()
    {
        var instanceMethods = Transaction
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName);

        // La seule méthode d'instance est la projection pure transmise au compte pour une annulation.
        var method = Assert.Single(instanceMethods);
        Assert.Equal(nameof(LoyaltyTransaction.ToPostedReference), method.Name);
        Assert.Equal(typeof(PostedTransaction), method.ReturnType);
    }

    [Fact]
    public void Transaction_cannot_be_created_outside_its_aggregate()
    {
        Assert.Empty(Transaction.GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.All(
            Transaction.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly),
            method => Assert.False(method.IsPublic));
        Assert.True(Transaction.IsSealed);
    }

    [Fact]
    public void Posted_transaction_keeps_its_values_whatever_happens_next()
    {
        var clock = new FakeClock();
        var tenant = TenantId.New();
        var account = LoyaltyAccount.Open(tenant, new CustomerReference(tenant, CustomerId.New(), false), true, clock);
        account.Earn(Points.Of(80), TransactionReference.Create("order-1"), false, clock);
        var entry = account.Transactions.Single();
        Assert.Equal((tenant, account.Id), (entry.TenantId, entry.AccountId));
        var snapshot = (entry.Id, entry.Type, entry.Direction, entry.Points, entry.Reference, entry.Reason, entry.OccurredAtUtc, entry.SignedPoints);

        clock.Advance(TimeSpan.FromDays(3));
        account.Redeem(Points.Of(30), TransactionReference.Create("order-2"), false, clock);
        account.Adjust(Points.Of(5), LedgerDirection.Debit, "correction", clock);
        account.ExpirePoints(Points.Of(1000), clock);
        account.Suspend(clock);

        Assert.Equal(snapshot, (entry.Id, entry.Type, entry.Direction, entry.Points, entry.Reference, entry.Reason, entry.OccurredAtUtc, entry.SignedPoints));
        Assert.Equal(FakeClock.DefaultNow, entry.OccurredAtUtc);
        Assert.Equal(0, account.Balance);
    }

    [Fact]
    public void Transactions_are_compared_by_identity()
    {
        var clock = new FakeClock();
        var tenant = TenantId.New();
        var account = LoyaltyAccount.Open(tenant, new CustomerReference(tenant, CustomerId.New(), false), true, clock);
        account.Earn(Points.Of(10), TransactionReference.Create("order-1"), false, clock);
        account.Earn(Points.Of(10), TransactionReference.Create("order-2"), false, clock);

        var entries = account.Transactions.ToArray();

        Assert.NotEqual(entries[0], entries[1]);
        Assert.NotEqual(entries[0].Id, entries[1].Id);
    }
}
