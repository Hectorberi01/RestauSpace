using Reviews.Application.Commands;
using Reviews.Domain;

namespace Reviews.Application;

public sealed class SurveyResponseUseCaseTests
{
    private readonly ReviewsFixture _fixture = new();

    private async Task<(Guid Survey, Guid Rating, Guid Choice, Guid Text)> PublishedAsync()
    {
        var (survey, rating, choice) = await _fixture.GivenDraftSurveyAsync();
        var text = await _fixture.AddQuestion.HandleAsync(new AddSurveyQuestionCommand(survey, "Une remarque ?", SurveyQuestionType.FreeText));
        await _fixture.Publish.HandleAsync(new PublishSurveyCommand(survey));
        return (survey, rating, choice, text);
    }

    [Fact]
    public async Task Answers_are_summed_up_per_question()
    {
        var (survey, rating, choice, text) = await PublishedAsync();

        await _fixture.SubmitResponse.HandleAsync(new SubmitSurveyResponseCommand(survey, _fixture.Location, null,
            [new SurveyAnswerInput(rating, Rating: 5), new SurveyAnswerInput(choice, Choices: ["Poisson"]), new SurveyAnswerInput(text, Text: " Très bon ")]));
        _fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        await _fixture.SubmitResponse.HandleAsync(new SubmitSurveyResponseCommand(survey, null, null,
            [new SurveyAnswerInput(rating, Rating: 2), new SurveyAnswerInput(choice, Choices: ["Poisson"])]));
        var results = await _fixture.Results.HandleAsync(new GetSurveyResultsQuery(survey));

        Assert.Equal(2, results.Responses);
        var ratings = results.Questions.Single(question => question.QuestionId == rating);
        Assert.Equal((2, 3.5m), (ratings.Answered, ratings.Average));
        Assert.Equal([0, 1, 0, 0, 1], ratings.Ratings);
        Assert.Equal([2, 0], results.Questions.Single(question => question.QuestionId == choice).Choices!.Select(item => item.Count));
        Assert.Equal("Très bon", results.Questions.Single(question => question.QuestionId == text).Texts!.Single().Text);
    }

    [Fact]
    public async Task An_order_answers_a_survey_once_and_gives_its_location()
    {
        var (survey, rating, _, _) = await PublishedAsync();
        var otherLocation = _fixture.GivenLocation();
        var order = _fixture.GivenOrder(otherLocation);

        await _fixture.SubmitResponse.HandleAsync(new SubmitSurveyResponseCommand(survey, null, order, [new SurveyAnswerInput(rating, Rating: 4)]));
        var twice = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.SubmitResponse.HandleAsync(new SubmitSurveyResponseCommand(survey, null, order, [new SurveyAnswerInput(rating, Rating: 1)])));

        Assert.Equal(SurveyResponseErrors.AlreadyAnswered, twice.Error);
        Assert.Equal(LocationId.From(otherLocation), _fixture.Responses.Items.Single().LocationId);
    }

    [Fact]
    public async Task A_draft_survey_is_unknown_to_the_public_and_a_closed_one_refuses_answers()
    {
        var (draft, first, _) = await _fixture.GivenDraftSurveyAsync();
        var hidden = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.PublicSurvey.HandleAsync(new GetPublicSurveyQuery(draft)));
        var draftAnswer = await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.SubmitResponse.HandleAsync(new SubmitSurveyResponseCommand(draft, null, null, [new SurveyAnswerInput(first, Rating: 4)])));
        await _fixture.Publish.HandleAsync(new PublishSurveyCommand(draft));
        await _fixture.Close.HandleAsync(new CloseSurveyCommand(draft));

        var closed = await Assert.ThrowsAsync<DomainException>(() =>
            _fixture.SubmitResponse.HandleAsync(new SubmitSurveyResponseCommand(draft, null, null, [new SurveyAnswerInput(first, Rating: 4)])));
        var shown = await _fixture.PublicSurvey.HandleAsync(new GetPublicSurveyQuery(draft));

        Assert.Equal(ReviewsApplicationErrors.SurveyNotFound, hidden.Error);
        Assert.Equal(ReviewsApplicationErrors.SurveyNotFound, draftAnswer.Error);
        Assert.Equal(SurveyResponseErrors.NotOpen, closed.Error);
        Assert.False(shown.IsOpen);
    }

    [Fact]
    public async Task The_active_survey_is_the_last_published_one_still_open()
    {
        var none = await _fixture.ActiveSurvey.HandleAsync(new GetActiveSurveyQuery());
        var (first, _, _, _) = await PublishedAsync();
        _fixture.Clock.Advance(TimeSpan.FromDays(1));
        var (second, _, _, _) = await PublishedAsync();

        var active = await _fixture.ActiveSurvey.HandleAsync(new GetActiveSurveyQuery());
        await _fixture.Close.HandleAsync(new CloseSurveyCommand(second));
        var afterClose = await _fixture.ActiveSurvey.HandleAsync(new GetActiveSurveyQuery());

        Assert.Null(none);
        Assert.Equal(second, active!.Id);
        Assert.Equal(first, afterClose!.Id);
    }

    [Fact]
    public async Task A_survey_of_another_restaurant_cannot_be_answered()
    {
        var (survey, rating, _, _) = await PublishedAsync();
        _fixture.CurrentTenantId = _fixture.TenantB;

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _fixture.SubmitResponse.HandleAsync(new SubmitSurveyResponseCommand(survey, null, null, [new SurveyAnswerInput(rating, Rating: 4)])));
    }
}
