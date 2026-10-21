using Microsoft.Extensions.DependencyInjection;
using Reviews.Application.Abstractions;
using Reviews.Domain;

namespace Reviews.Infrastructure;

/// <summary>Fusion de clients : les avis du doublon passent à la fiche gardée, dans son restaurant seulement.</summary>
public sealed class FeedbackCustomerReassignmentTests(ReviewsDatabase database) : IClassFixture<ReviewsDatabase>
{
    private IClock Clock => database.Clock;

    private async Task<Feedback> SavedAsync(TenantId tenant, CustomerId customer)
    {
        var feedback = Feedback.Submit(
            tenant, new LocationReference(tenant, LocationId.New(), IsOperational: true), Rating.Create(4), "Bon", Clock,
            new CustomerReference(customer, tenant, isAnonymized: false));
        await using var scope = database.NewScope(tenant);
        scope.ServiceProvider.GetRequiredService<IFeedbackRepository>().Add(feedback);
        await scope.ServiceProvider.GetRequiredService<IReviewsUnitOfWork>().SaveChangesAsync();
        return feedback;
    }

    [Fact]
    public async Task The_feedbacks_of_the_duplicate_move_to_the_kept_customer()
    {
        var tenant = TenantId.New();
        var other = TenantId.New();
        var duplicate = CustomerId.New();
        var kept = CustomerId.New();
        var mine = await SavedAsync(tenant, duplicate);
        var foreign = await SavedAsync(other, duplicate);

        int moved, again;
        await using (var scope = database.NewScope(tenant))
        {
            var reassignment = scope.ServiceProvider.GetRequiredService<IFeedbackCustomerReassignment>();
            moved = await reassignment.ReassignAsync(tenant, duplicate, kept);
            again = await reassignment.ReassignAsync(tenant, duplicate, kept);
        }

        await using var check = database.NewScope(tenant);
        var feedbacks = check.ServiceProvider.GetRequiredService<IFeedbackRepository>();
        Assert.Equal((1, 0), (moved, again));
        Assert.Equal(kept, (await feedbacks.FindAsync(tenant, mine.Id))!.CustomerId);
        await using var otherScope = database.NewScope(other);
        Assert.Equal(duplicate, (await otherScope.ServiceProvider.GetRequiredService<IFeedbackRepository>().FindAsync(other, foreign.Id))!.CustomerId);
    }
}
