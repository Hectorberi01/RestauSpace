using Microsoft.Extensions.DependencyInjection;
using Reviews.Application.Abstractions;
using Reviews.Domain;

namespace Reviews.Infrastructure;

public sealed class SurveyResponsePersistenceTests(ReviewsDatabase database) : IClassFixture<ReviewsDatabase>
{
    private IClock Clock => database.Clock;

    private async Task<(Survey Survey, Guid Rating, Guid Choice)> PublishedAsync(TenantId tenant)
    {
        var survey = Survey.Create(tenant, "Votre visite");
        var rating = survey.AddQuestion("Note ?", SurveyQuestionType.Rating, isRequired: true);
        var choice = survey.AddQuestion("Plats ?", SurveyQuestionType.MultipleChoice, choices: ["Poisson", "Poulet"]);
        survey.Publish(Clock);
        await using var scope = database.NewScope(tenant);
        scope.ServiceProvider.GetRequiredService<ISurveyRepository>().Add(survey);
        await scope.ServiceProvider.GetRequiredService<IReviewsUnitOfWork>().SaveChangesAsync();
        return (survey, rating.Value, choice.Value);
    }

    private async Task SaveAsync(TenantId tenant, SurveyResponse response)
    {
        await using var scope = database.NewScope(tenant);
        scope.ServiceProvider.GetRequiredService<ISurveyResponseRepository>().Add(response);
        await scope.ServiceProvider.GetRequiredService<IReviewsUnitOfWork>().SaveChangesAsync();
    }

    [Fact]
    public async Task A_response_comes_back_with_its_answers_and_one_order_answers_once()
    {
        var tenant = TenantId.New();
        var (survey, rating, choice) = await PublishedAsync(tenant);
        var order = new OrderReference(tenant, OrderId.New());
        var location = LocationId.New();
        await SaveAsync(tenant, SurveyResponse.Submit(survey,
            [new SurveyAnswerInput(rating, Rating: 5), new SurveyAnswerInput(choice, Choices: ["Poisson", "Poulet"])], location, order, Clock));

        await using (var scope = database.NewScope(tenant))
        {
            var responses = scope.ServiceProvider.GetRequiredService<ISurveyResponseRepository>();
            var loaded = (await responses.ListAsync(tenant, survey.Id, null, null, 0, 10)).Single();
            Assert.Equal((location, order.OrderId), (loaded.LocationId!.Value, loaded.OrderId!.Value));
            Assert.Equal(5, loaded.Answers[0].Rating);
            Assert.Equal(["Poisson", "Poulet"], loaded.Answers[1].Choices);
            Assert.True(await responses.ExistsForOrderAsync(tenant, survey.Id, order.OrderId));
            Assert.Equal(1, await responses.CountAsync(tenant, survey.Id, null, null));
        }

        var duplicate = await Assert.ThrowsAsync<DomainException>(() =>
            SaveAsync(tenant, SurveyResponse.Submit(survey, [new SurveyAnswerInput(rating, Rating: 1)], null, order, Clock)));
        Assert.Equal(SurveyResponseErrors.AlreadyAnswered, duplicate.Error);
    }

    [Fact]
    public async Task The_responses_of_another_restaurant_are_not_visible()
    {
        var tenant = TenantId.New();
        var (survey, rating, _) = await PublishedAsync(tenant);
        await SaveAsync(tenant, SurveyResponse.Submit(survey, [new SurveyAnswerInput(rating, Rating: 3)], null, null, Clock));

        await using var scope = database.NewScope(TenantId.New());
        var responses = scope.ServiceProvider.GetRequiredService<ISurveyResponseRepository>();

        Assert.Equal(0, await responses.CountAsync(tenant, survey.Id, null, null));
    }
}
