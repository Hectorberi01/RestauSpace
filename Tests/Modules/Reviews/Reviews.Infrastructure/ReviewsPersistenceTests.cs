using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reviews.Application;
using Reviews.Application.Abstractions;
using Reviews.Domain;
using Reviews.Infrastructure.Persistence;

namespace Reviews.Infrastructure;

public sealed class ReviewsPersistenceTests(ReviewsDatabase database) : IClassFixture<ReviewsDatabase>
{
    private IClock Clock => database.Clock;

    private Feedback NewFeedback(
        TenantId tenantId, LocationId? locationId = null, int rating = 4, string? comment = "Très bon accueil", CustomerId? customerId = null, OrderId? orderId = null) =>
        Feedback.Submit(
            tenantId,
            new LocationReference(tenantId, locationId ?? LocationId.New(), IsOperational: true),
            Rating.Create(rating),
            comment,
            Clock,
            customerId is { } customer ? new CustomerReference(customer, tenantId, isAnonymized: false) : null,
            orderId is { } order ? new OrderReference(tenantId, order) : null);

    private async Task<Feedback> SaveAsync(Feedback feedback)
    {
        await using var scope = database.NewScope(feedback.TenantId);
        scope.ServiceProvider.GetRequiredService<IFeedbackRepository>().Add(feedback);
        await scope.ServiceProvider.GetRequiredService<IReviewsUnitOfWork>().SaveChangesAsync();
        return feedback;
    }

    private async Task<Survey> SaveAsync(Survey survey)
    {
        await using var scope = database.NewScope(survey.TenantId);
        scope.ServiceProvider.GetRequiredService<ISurveyRepository>().Add(survey);
        await scope.ServiceProvider.GetRequiredService<IReviewsUnitOfWork>().SaveChangesAsync();
        return survey;
    }

    private async Task<T> InScopeAsync<T>(TenantId? tenantId, Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = database.NewScope(tenantId);
        return await read(scope.ServiceProvider);
    }

    [Fact]
    public async Task A_feedback_comes_back_whole_and_detaching_its_customer_clears_the_comment()
    {
        var tenant = TenantId.New();
        var customer = CustomerId.New();
        var order = OrderId.New();
        var feedback = await SaveAsync(NewFeedback(tenant, rating: 2, customerId: customer, orderId: order));

        var loaded = await InScopeAsync(tenant, services => services.GetRequiredService<IFeedbackRepository>().FindAsync(tenant, feedback.Id));

        Assert.NotNull(loaded);
        Assert.Equal(Rating.Create(2), loaded.Rating);
        Assert.Equal(("Très bon accueil", FeedbackStatus.Submitted), (loaded.Comment, loaded.Status));
        Assert.Equal((customer, order), (loaded.CustomerId!.Value, loaded.OrderId!.Value));
        Assert.Equal(feedback.SubmittedAtUtc, loaded.SubmittedAtUtc);

        var reviewer = UserId.New();
        await using (var scope = database.NewScope(tenant))
        {
            var repository = scope.ServiceProvider.GetRequiredService<IFeedbackRepository>();
            var toChange = Assert.Single(await repository.ListForCustomerAsync(tenant, customer));
            toChange.MarkReviewed(reviewer, Clock);
            toChange.DetachCustomer(Clock);
            await scope.ServiceProvider.GetRequiredService<IReviewsUnitOfWork>().SaveChangesAsync();
        }

        var reloaded = await InScopeAsync(tenant, services => services.GetRequiredService<IFeedbackRepository>().FindAsync(tenant, feedback.Id));
        Assert.True(reloaded!.IsCustomerDetached);
        Assert.Null(reloaded.CustomerId);
        Assert.Null(reloaded.Comment);
        Assert.Equal(order, reloaded.OrderId);
        Assert.Equal(reviewer, reloaded.ReviewedBy);
        Assert.Empty(await InScopeAsync(tenant, services => services.GetRequiredService<IFeedbackRepository>().ListForCustomerAsync(tenant, customer)));

        var types = await InScopeAsync(tenant, services => services.GetRequiredService<ReviewsDbContext>().OutboxMessages
            .Select(message => message.Type)
            .ToListAsync());
        Assert.Contains(nameof(FeedbackSubmitted), types);
        Assert.Contains(nameof(FeedbackReviewed), types);
        Assert.Contains(nameof(FeedbackCustomerDetached), types);
    }

    [Fact]
    public async Task A_feedback_of_another_tenant_cannot_be_loaded_even_with_its_id()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var feedback = await SaveAsync(NewFeedback(tenantA));

        // Le filtre du repository ET le query filter refusent.
        Assert.Null(await InScopeAsync(tenantB, services => services.GetRequiredService<IFeedbackRepository>().FindAsync(tenantA, feedback.Id)));
        Assert.Null(await InScopeAsync(tenantB, services => services.GetRequiredService<IFeedbackRepository>().FindAsync(tenantB, feedback.Id)));
        // Sans restaurant sélectionné, rien n'est visible.
        Assert.Empty(await InScopeAsync(null, services => services.GetRequiredService<ReviewsDbContext>().Feedbacks.ToListAsync()));
        Assert.Empty(await InScopeAsync(null, services => services.GetRequiredService<ReviewsDbContext>().Surveys.ToListAsync()));
    }

    [Fact]
    public async Task A_second_feedback_for_the_same_order_is_refused_by_the_database()
    {
        var tenant = TenantId.New();
        var order = OrderId.New();
        await SaveAsync(NewFeedback(tenant, orderId: order));

        var refused = await Assert.ThrowsAsync<DomainException>(() => SaveAsync(NewFeedback(tenant, orderId: order)));

        Assert.Equal(ReviewsApplicationErrors.OrderAlreadyReviewed, refused.Error);
        Assert.True(await InScopeAsync(tenant, services => services.GetRequiredService<IFeedbackRepository>().ExistsForOrderAsync(tenant, order)));
        // Le même id de commande dans un autre restaurant n'a rien à voir, et plusieurs avis sans commande cohabitent.
        await SaveAsync(NewFeedback(TenantId.New(), orderId: order));
        await SaveAsync(NewFeedback(tenant));
        await SaveAsync(NewFeedback(tenant));
    }

    [Fact]
    public async Task Search_and_counts_filter_in_the_database()
    {
        var tenant = TenantId.New();
        var location = LocationId.New();
        await SaveAsync(NewFeedback(tenant, location, rating: 5));
        await SaveAsync(NewFeedback(tenant, location, rating: 1, comment: null));
        var archived = NewFeedback(tenant, location, rating: 2);
        archived.Archive(Clock);
        await SaveAsync(archived);
        await SaveAsync(NewFeedback(tenant, LocationId.New(), rating: 3));
        await SaveAsync(NewFeedback(TenantId.New(), location, rating: 1));

        var here = new FeedbackSearch(LocationId: location);
        var page = await InScopeAsync(tenant, services => services.GetRequiredService<IFeedbackRepository>().SearchAsync(tenant, here, 0, 10));
        var low = await InScopeAsync(
            tenant, services => services.GetRequiredService<IFeedbackRepository>().SearchAsync(tenant, here with { MaxRating = 2, IncludeArchived = true }, 0, 10));
        var withComment = await InScopeAsync(
            tenant, services => services.GetRequiredService<IFeedbackRepository>().SearchAsync(tenant, here with { HasComment = true }, 0, 10));
        var counts = await InScopeAsync(
            tenant, services => services.GetRequiredService<IFeedbackRepository>().CountAsync(tenant, new FeedbackSearch(IncludeArchived: true)));

        Assert.Equal(2, page.Count);
        Assert.Equal(new[] { 1, 2 }, low.Select(feedback => feedback.Rating.Value).Order());
        Assert.Equal(5, Assert.Single(withComment).Rating.Value);
        Assert.Equal(4, counts.Sum(count => count.Count));
        Assert.Equal(1, Assert.Single(counts, count => count.Status == FeedbackStatus.Archived).Count);
        Assert.False(Assert.Single(counts, count => count.Rating == 1).HasComment);
    }

    [Fact]
    public async Task A_survey_comes_back_with_its_questions_in_order_and_their_choices()
    {
        var tenant = TenantId.New();
        var survey = Survey.Create(tenant, "Votre visite");
        var rating = survey.AddQuestion("Note globale ?", SurveyQuestionType.Rating, isRequired: true);
        var dish = survey.AddQuestion("Plat préféré ?", SurveyQuestionType.SingleChoice, choices: ["Poisson", "Poulet"]);
        await SaveAsync(survey);

        await using (var scope = database.NewScope(tenant))
        {
            var toChange = await scope.ServiceProvider.GetRequiredService<ISurveyRepository>().FindAsync(tenant, survey.Id);
            toChange!.MoveQuestion(dish, 1);
            toChange.ChangeQuestion(dish, "Plat préféré ?", SurveyQuestionType.MultipleChoice, choices: ["Poisson", "Poulet", "Légumes"]);
            toChange.RemoveQuestion(rating);
            toChange.AddQuestion("Un mot ?", SurveyQuestionType.FreeText);
            toChange.Publish(Clock);
            await scope.ServiceProvider.GetRequiredService<IReviewsUnitOfWork>().SaveChangesAsync();
        }

        var loaded = await InScopeAsync(tenant, services => services.GetRequiredService<ISurveyRepository>().FindAsync(tenant, survey.Id));
        Assert.Equal(SurveyStatus.Published, loaded!.Status);
        Assert.Equal(new[] { "Plat préféré ?", "Un mot ?" }, loaded.Questions.Select(question => question.Text));
        Assert.Equal(new[] { "Poisson", "Poulet", "Légumes" }, loaded.Questions.First().Choices);
        Assert.Empty(loaded.Questions.Last().Choices);
        var listed = Assert.Single(await InScopeAsync(tenant, services => services.GetRequiredService<ISurveyRepository>().ListAsync(tenant, SurveyStatus.Published)));
        Assert.Equal(2, listed.Questions.Count);
    }

    [Fact]
    public async Task Two_concurrent_edits_of_the_same_draft_conflict_even_on_different_questions()
    {
        var tenant = TenantId.New();
        var survey = Survey.Create(tenant, "Brouillon");
        var first = survey.AddQuestion("Première ?", SurveyQuestionType.FreeText);
        var second = survey.AddQuestion("Seconde ?", SurveyQuestionType.FreeText);
        await SaveAsync(survey);

        await using var one = database.NewScope(tenant);
        await using var two = database.NewScope(tenant);
        var mine = await one.ServiceProvider.GetRequiredService<ISurveyRepository>().FindAsync(tenant, survey.Id);
        var theirs = await two.ServiceProvider.GetRequiredService<ISurveyRepository>().FindAsync(tenant, survey.Id);
        mine!.ChangeQuestion(first, "Première, reformulée ?", SurveyQuestionType.FreeText);
        theirs!.ChangeQuestion(second, "Seconde, reformulée ?", SurveyQuestionType.FreeText);

        await one.ServiceProvider.GetRequiredService<IReviewsUnitOfWork>().SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConcurrencyException>(() => two.ServiceProvider.GetRequiredService<IReviewsUnitOfWork>().SaveChangesAsync());

        Assert.Equal(ReviewsApplicationErrors.ConcurrentUpdate, conflict.Error);
    }

    [Fact]
    public async Task Reviewing_and_archiving_the_same_feedback_at_once_conflict()
    {
        var tenant = TenantId.New();
        var feedback = await SaveAsync(NewFeedback(tenant));

        await using var one = database.NewScope(tenant);
        await using var two = database.NewScope(tenant);
        var mine = await one.ServiceProvider.GetRequiredService<IFeedbackRepository>().FindAsync(tenant, feedback.Id);
        var theirs = await two.ServiceProvider.GetRequiredService<IFeedbackRepository>().FindAsync(tenant, feedback.Id);
        mine!.MarkReviewed(UserId.New(), Clock);
        theirs!.Archive(Clock);

        await one.ServiceProvider.GetRequiredService<IReviewsUnitOfWork>().SaveChangesAsync();
        await Assert.ThrowsAsync<ConcurrencyException>(() => two.ServiceProvider.GetRequiredService<IReviewsUnitOfWork>().SaveChangesAsync());
    }
}
