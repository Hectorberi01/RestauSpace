namespace Reviews.Domain;

public class SurveyResponseTests
{
    private static readonly TenantId Tenant = TenantId.New();

    private readonly FakeClock _clock = new();

    private (Survey Survey, Guid Rating, Guid Single, Guid Multiple, Guid Text) Published()
    {
        var survey = Survey.Create(Tenant, "Votre visite");
        var rating = survey.AddQuestion("Note ?", SurveyQuestionType.Rating, isRequired: true);
        var single = survey.AddQuestion("Moment ?", SurveyQuestionType.SingleChoice, choices: ["Midi", "Soir"]);
        var multiple = survey.AddQuestion("Plats ?", SurveyQuestionType.MultipleChoice, choices: ["Poisson", "Poulet", "Attiéké"]);
        var text = survey.AddQuestion("Remarque ?", SurveyQuestionType.FreeText);
        survey.Publish(_clock);
        return (survey, rating.Value, single.Value, multiple.Value, text.Value);
    }

    [Fact]
    public void A_complete_response_keeps_the_question_order_and_skips_empty_answers()
    {
        var (survey, rating, single, multiple, text) = Published();

        var response = SurveyResponse.Submit(survey,
            [new SurveyAnswerInput(text, Text: "  "), new SurveyAnswerInput(multiple, Choices: ["Poulet", "Attiéké"]), new SurveyAnswerInput(rating, Rating: 4), new SurveyAnswerInput(single, Choices: ["Soir"])],
            null, null, _clock);

        Assert.Equal([rating, single, multiple], response.Answers.Select(answer => answer.QuestionId.Value));
        Assert.Equal(["Poulet", "Attiéké"], response.Answers[2].Choices);
    }

    [Fact]
    public void A_required_question_must_be_answered()
    {
        var (survey, _, single, _, _) = Published();

        DomainAssert.Throws(SurveyResponseErrors.RequiredMissing, () =>
            SurveyResponse.Submit(survey, [new SurveyAnswerInput(single, Choices: ["Midi"])], null, null, _clock));
    }

    [Fact]
    public void Answers_must_match_their_question()
    {
        var (survey, rating, single, multiple, text) = Published();

        DomainAssert.Throws(SurveyResponseErrors.RatingOutOfRange, () => SurveyResponse.Submit(survey, [new SurveyAnswerInput(rating, Rating: 6)], null, null, _clock));
        DomainAssert.Throws(SurveyResponseErrors.AnswerMismatch, () => SurveyResponse.Submit(survey, [new SurveyAnswerInput(rating, Text: "5")], null, null, _clock));
        DomainAssert.Throws(SurveyResponseErrors.SingleChoiceOnly, () =>
            SurveyResponse.Submit(survey, [new SurveyAnswerInput(rating, Rating: 3), new SurveyAnswerInput(single, Choices: ["Midi", "Soir"])], null, null, _clock));
        DomainAssert.Throws(SurveyResponseErrors.ChoiceUnknown, () =>
            SurveyResponse.Submit(survey, [new SurveyAnswerInput(rating, Rating: 3), new SurveyAnswerInput(multiple, Choices: ["Pizza"])], null, null, _clock));
        DomainAssert.Throws(SurveyResponseErrors.TextTooLong, () =>
            SurveyResponse.Submit(survey, [new SurveyAnswerInput(rating, Rating: 3), new SurveyAnswerInput(text, Text: new string('a', 1001))], null, null, _clock));
        DomainAssert.Throws(SurveyResponseErrors.QuestionUnknown, () => SurveyResponse.Submit(survey, [new SurveyAnswerInput(Guid.NewGuid(), Rating: 3)], null, null, _clock));
        DomainAssert.Throws(SurveyResponseErrors.QuestionAnsweredTwice, () =>
            SurveyResponse.Submit(survey, [new SurveyAnswerInput(rating, Rating: 3), new SurveyAnswerInput(rating, Rating: 4)], null, null, _clock));
    }

    [Fact]
    public void Only_a_published_survey_takes_answers()
    {
        var draft = Survey.Create(Tenant, "Brouillon");
        var question = draft.AddQuestion("Note ?", SurveyQuestionType.Rating);

        DomainAssert.Throws(SurveyResponseErrors.NotOpen, () => SurveyResponse.Submit(draft, [new SurveyAnswerInput(question.Value, Rating: 3)], null, null, _clock));
    }

    [Fact]
    public void An_order_of_another_restaurant_is_refused()
    {
        var (survey, rating, _, _, _) = Published();

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => SurveyResponse.Submit(
            survey, [new SurveyAnswerInput(rating, Rating: 3)], null, new OrderReference(TenantId.New(), OrderId.New()), _clock));
    }
}
