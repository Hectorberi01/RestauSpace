namespace Reviews.Domain;

public class SurveyTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly string[] YesNo = ["Yes", "No"];

    private readonly FakeClock _clock = new();

    private static Survey NewDraft() => Survey.Create(Tenant, "After-dinner survey");

    private static Survey NewDraftWithQuestions(params string[] texts)
    {
        var survey = NewDraft();
        foreach (var text in texts)
        {
            survey.AddQuestion(text, SurveyQuestionType.FreeText);
        }

        return survey;
    }

    private Survey NewPublished()
    {
        var survey = NewDraftWithQuestions("How was the food?");
        survey.Publish(_clock);
        survey.ClearDomainEvents();
        return survey;
    }

    private Survey NewClosed()
    {
        var survey = NewPublished();
        survey.Close(_clock);
        survey.ClearDomainEvents();
        return survey;
    }

    private static string[] Texts(Survey survey) => survey.Questions.Select(question => question.Text).ToArray();

    private static int[] Orders(Survey survey) => survey.Questions.Select(question => question.Order).ToArray();

    /// <summary>Simule un ORM qui charge les questions dans un ordre sans rapport avec <see cref="SurveyQuestion.Order"/>.</summary>
    private static Survey LoadedInReverseOrder(Survey survey)
    {
        var field = typeof(Survey).GetField("_questions", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        ((List<SurveyQuestion>)field.GetValue(survey)!).Reverse();
        return survey;
    }

    // ---- Create / Rename

    [Fact]
    public void Create_builds_an_empty_draft()
    {
        var survey = Survey.Create(Tenant, "  After-dinner survey ");

        Assert.Equal(Tenant, survey.TenantId);
        Assert.Equal("After-dinner survey", survey.Title);
        Assert.Equal(SurveyStatus.Draft, survey.Status);
        Assert.Empty(survey.Questions);
        Assert.Null(survey.PublishedAtUtc);
        Assert.Null(survey.ClosedAtUtc);
        DomainAssert.NoEvents(survey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_a_blank_title(string title) =>
        DomainAssert.Throws(SurveyErrors.TitleRequired, () => Survey.Create(Tenant, title));

    [Fact]
    public void Create_accepts_a_title_at_the_limit_and_rejects_a_longer_one()
    {
        Assert.Equal(Survey.TitleMaxLength, Survey.Create(Tenant, new string('t', Survey.TitleMaxLength)).Title.Length);
        DomainAssert.Throws(
            SurveyErrors.TitleTooLong,
            () => Survey.Create(Tenant, new string('t', Survey.TitleMaxLength + 1)));
    }

    [Fact]
    public void Rename_changes_the_title_of_a_draft()
    {
        var survey = NewDraft();

        survey.Rename(" Lunch survey ");

        Assert.Equal("Lunch survey", survey.Title);
    }

    [Fact]
    public void Rename_rejects_a_blank_or_a_too_long_title()
    {
        var survey = NewDraft();

        DomainAssert.Throws(SurveyErrors.TitleRequired, () => survey.Rename(" "));
        DomainAssert.Throws(SurveyErrors.TitleTooLong, () => survey.Rename(new string('t', Survey.TitleMaxLength + 1)));
        Assert.Equal("After-dinner survey", survey.Title);
    }

    [Fact]
    public void Rename_to_the_current_title_is_a_silent_no_op()
    {
        var survey = NewDraft();

        survey.Rename(" After-dinner survey ");

        Assert.Equal("After-dinner survey", survey.Title);
        DomainAssert.NoEvents(survey);
    }

    // ---- AddQuestion

    [Fact]
    public void AddQuestion_appends_questions_in_order()
    {
        var survey = NewDraft();

        var firstId = survey.AddQuestion("  Rate your meal ", SurveyQuestionType.Rating, isRequired: true);
        var secondId = survey.AddQuestion("Any comment?", SurveyQuestionType.FreeText);

        Assert.Equal(2, survey.Questions.Count);
        var first = survey.Questions.First();
        Assert.Equal(firstId, first.Id);
        Assert.Equal("Rate your meal", first.Text);
        Assert.Equal(SurveyQuestionType.Rating, first.Type);
        Assert.True(first.IsRequired);
        Assert.Equal(1, first.Order);
        Assert.Empty(first.Choices);
        Assert.False(first.IsChoiceQuestion);
        var second = survey.Questions.Last();
        Assert.Equal(secondId, second.Id);
        Assert.False(second.IsRequired);
        Assert.Equal(2, second.Order);
        DomainAssert.NoEvents(survey);
    }

    [Theory]
    [InlineData(SurveyQuestionType.SingleChoice)]
    [InlineData(SurveyQuestionType.MultipleChoice)]
    public void AddQuestion_keeps_the_trimmed_choices_of_a_choice_question(SurveyQuestionType type)
    {
        var survey = NewDraft();

        survey.AddQuestion("Would you come back?", type, choices: [" Yes ", "No", "Maybe"]);

        var question = Assert.Single(survey.Questions);
        Assert.True(question.IsChoiceQuestion);
        Assert.Equal(["Yes", "No", "Maybe"], question.Choices);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void AddQuestion_rejects_a_blank_text(string text)
    {
        var survey = NewDraft();

        DomainAssert.Throws(SurveyErrors.QuestionTextRequired, () => survey.AddQuestion(text, SurveyQuestionType.FreeText));
        Assert.Empty(survey.Questions);
    }

    [Fact]
    public void AddQuestion_accepts_a_text_at_the_limit_and_rejects_a_longer_one()
    {
        var survey = NewDraft();

        survey.AddQuestion(new string('q', SurveyQuestion.TextMaxLength), SurveyQuestionType.FreeText);
        DomainAssert.Throws(
            SurveyErrors.QuestionTextTooLong,
            () => survey.AddQuestion(new string('q', SurveyQuestion.TextMaxLength + 1), SurveyQuestionType.FreeText));

        Assert.Single(survey.Questions);
    }

    [Fact]
    public void AddQuestion_rejects_an_unknown_type()
    {
        var survey = NewDraft();

        DomainAssert.Throws(SurveyErrors.QuestionTypeUnknown, () => survey.AddQuestion("Q", (SurveyQuestionType)0));
    }

    [Theory]
    [InlineData(SurveyQuestionType.SingleChoice)]
    [InlineData(SurveyQuestionType.MultipleChoice)]
    public void AddQuestion_requires_at_least_two_choices_for_a_choice_question(SurveyQuestionType type)
    {
        var survey = NewDraft();

        DomainAssert.Throws(SurveyErrors.ChoicesRequired, () => survey.AddQuestion("Q", type));
        DomainAssert.Throws(SurveyErrors.ChoicesRequired, () => survey.AddQuestion("Q", type, choices: ["Only one"]));
        Assert.Empty(survey.Questions);
    }

    [Theory]
    [InlineData(SurveyQuestionType.Rating)]
    [InlineData(SurveyQuestionType.FreeText)]
    public void AddQuestion_rejects_choices_on_a_non_choice_question(SurveyQuestionType type)
    {
        var survey = NewDraft();

        DomainAssert.Throws(SurveyErrors.ChoicesNotAllowed, () => survey.AddQuestion("Q", type, choices: YesNo));
    }

    [Fact]
    public void AddQuestion_accepts_the_maximum_number_of_choices_and_rejects_more()
    {
        var survey = NewDraft();
        var atLimit = Enumerable.Range(1, SurveyQuestion.MaxChoices).Select(index => $"Choice {index}").ToList();

        survey.AddQuestion("Q", SurveyQuestionType.MultipleChoice, choices: atLimit);
        DomainAssert.Throws(
            SurveyErrors.TooManyChoices,
            () => survey.AddQuestion("Q2", SurveyQuestionType.MultipleChoice, choices: atLimit.Append("One more")));

        Assert.Equal(SurveyQuestion.MaxChoices, Assert.Single(survey.Questions).Choices.Count);
    }

    [Fact]
    public void AddQuestion_rejects_blank_too_long_or_duplicate_choices()
    {
        var survey = NewDraft();

        DomainAssert.Throws(
            SurveyErrors.ChoiceInvalid,
            () => survey.AddQuestion("Q", SurveyQuestionType.SingleChoice, choices: ["Yes", "  "]));
        DomainAssert.Throws(
            SurveyErrors.ChoiceInvalid,
            () => survey.AddQuestion(
                "Q", SurveyQuestionType.SingleChoice, choices: ["Yes", new string('c', SurveyQuestion.ChoiceMaxLength + 1)]));
        DomainAssert.Throws(
            SurveyErrors.ChoiceDuplicate,
            () => survey.AddQuestion("Q", SurveyQuestionType.SingleChoice, choices: ["Yes", " yes "]));
        Assert.Empty(survey.Questions);
    }

    [Fact]
    public void AddQuestion_rejects_more_questions_than_the_maximum()
    {
        var survey = NewDraft();
        for (var index = 0; index < Survey.MaxQuestions; index++)
        {
            survey.AddQuestion($"Question {index}", SurveyQuestionType.FreeText);
        }

        DomainAssert.Throws(SurveyErrors.TooManyQuestions, () => survey.AddQuestion("One more", SurveyQuestionType.FreeText));
        Assert.Equal(Survey.MaxQuestions, survey.Questions.Count);
    }

    // ---- ChangeQuestion / MoveQuestion / RemoveQuestion

    [Fact]
    public void ChangeQuestion_replaces_the_content_and_keeps_identity_and_order()
    {
        var survey = NewDraftWithQuestions("A", "B");
        var id = survey.Questions.Last().Id;

        survey.ChangeQuestion(id, " Would you come back? ", SurveyQuestionType.SingleChoice, isRequired: true, YesNo);

        var question = survey.Questions.Last();
        Assert.Equal(id, question.Id);
        Assert.Equal(2, question.Order);
        Assert.Equal("Would you come back?", question.Text);
        Assert.Equal(SurveyQuestionType.SingleChoice, question.Type);
        Assert.True(question.IsRequired);
        Assert.Equal(YesNo, question.Choices);
    }

    [Fact]
    public void ChangeQuestion_to_a_non_choice_type_drops_the_choices()
    {
        var survey = NewDraft();
        var id = survey.AddQuestion("Would you come back?", SurveyQuestionType.SingleChoice, choices: YesNo);

        survey.ChangeQuestion(id, "Tell us more", SurveyQuestionType.FreeText);

        Assert.Empty(Assert.Single(survey.Questions).Choices);
    }

    [Fact]
    public void ChangeQuestion_rejects_invalid_content_and_leaves_the_question_untouched()
    {
        var survey = NewDraft();
        var id = survey.AddQuestion("Would you come back?", SurveyQuestionType.SingleChoice, choices: YesNo);

        DomainAssert.Throws(SurveyErrors.QuestionTextRequired, () => survey.ChangeQuestion(id, " ", SurveyQuestionType.FreeText));
        DomainAssert.Throws(SurveyErrors.ChoicesRequired, () => survey.ChangeQuestion(id, "Q", SurveyQuestionType.MultipleChoice));
        DomainAssert.Throws(SurveyErrors.ChoicesNotAllowed, () => survey.ChangeQuestion(id, "Q", SurveyQuestionType.Rating, choices: YesNo));

        var question = Assert.Single(survey.Questions);
        Assert.Equal("Would you come back?", question.Text);
        Assert.Equal(SurveyQuestionType.SingleChoice, question.Type);
        Assert.Equal(YesNo, question.Choices);
    }

    [Fact]
    public void ChangeQuestion_rejects_an_unknown_question()
    {
        var survey = NewDraftWithQuestions("A");

        DomainAssert.Throws(
            SurveyErrors.QuestionNotFound,
            () => survey.ChangeQuestion(SurveyQuestionId.New(), "Q", SurveyQuestionType.FreeText));
    }

    [Fact]
    public void MoveQuestion_reorders_the_questions_contiguously()
    {
        var survey = NewDraftWithQuestions("A", "B", "C", "D");

        survey.MoveQuestion(survey.Questions.Last().Id, 1);
        Assert.Equal(["D", "A", "B", "C"], Texts(survey));
        Assert.Equal([1, 2, 3, 4], Orders(survey));

        survey.MoveQuestion(survey.Questions.First().Id, 3);
        Assert.Equal(["A", "B", "D", "C"], Texts(survey));
        Assert.Equal([1, 2, 3, 4], Orders(survey));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    public void MoveQuestion_rejects_a_position_outside_the_survey(int newOrder)
    {
        var survey = NewDraftWithQuestions("A", "B", "C");

        DomainAssert.Throws(
            SurveyErrors.QuestionOrderOutOfRange,
            () => survey.MoveQuestion(survey.Questions.First().Id, newOrder));
        Assert.Equal(["A", "B", "C"], Texts(survey));
    }

    [Fact]
    public void MoveQuestion_rejects_an_unknown_question()
    {
        var survey = NewDraftWithQuestions("A", "B");

        DomainAssert.Throws(SurveyErrors.QuestionNotFound, () => survey.MoveQuestion(SurveyQuestionId.New(), 1));
    }

    [Fact]
    public void MoveQuestion_to_the_current_position_is_a_silent_no_op()
    {
        var survey = NewDraftWithQuestions("A", "B", "C");

        survey.MoveQuestion(survey.Questions.ElementAt(1).Id, 2);

        Assert.Equal(["A", "B", "C"], Texts(survey));
        Assert.Equal([1, 2, 3], Orders(survey));
        DomainAssert.NoEvents(survey);
    }

    [Fact]
    public void Questions_are_exposed_by_order_whatever_the_load_order()
    {
        var survey = LoadedInReverseOrder(NewDraftWithQuestions("A", "B", "C"));

        Assert.Equal(["A", "B", "C"], Texts(survey));
        Assert.Equal([1, 2, 3], Orders(survey));
    }

    [Fact]
    public void MoveQuestion_does_not_depend_on_the_load_order()
    {
        var survey = LoadedInReverseOrder(NewDraftWithQuestions("A", "B", "C", "D"));

        survey.MoveQuestion(survey.Questions.Last().Id, 2);

        Assert.Equal(["A", "D", "B", "C"], Texts(survey));
        Assert.Equal([1, 2, 3, 4], Orders(survey));
    }

    [Fact]
    public void RemoveQuestion_does_not_depend_on_the_load_order()
    {
        var survey = LoadedInReverseOrder(NewDraftWithQuestions("A", "B", "C", "D"));

        survey.RemoveQuestion(survey.Questions.ElementAt(1).Id);

        Assert.Equal(["A", "C", "D"], Texts(survey));
        Assert.Equal([1, 2, 3], Orders(survey));
    }

    [Fact]
    public void AddQuestion_appends_after_the_last_position_whatever_the_load_order()
    {
        var survey = LoadedInReverseOrder(NewDraftWithQuestions("A", "B"));

        survey.AddQuestion("C", SurveyQuestionType.FreeText);

        Assert.Equal(["A", "B", "C"], Texts(survey));
        Assert.Equal([1, 2, 3], Orders(survey));
    }

    [Fact]
    public void RemoveQuestion_removes_the_question_and_closes_the_gap()
    {
        var survey = NewDraftWithQuestions("A", "B", "C");

        survey.RemoveQuestion(survey.Questions.ElementAt(1).Id);

        Assert.Equal(["A", "C"], Texts(survey));
        Assert.Equal([1, 2], Orders(survey));
    }

    [Fact]
    public void RemoveQuestion_rejects_an_unknown_question()
    {
        var survey = NewDraftWithQuestions("A");

        DomainAssert.Throws(SurveyErrors.QuestionNotFound, () => survey.RemoveQuestion(SurveyQuestionId.New()));
        Assert.Single(survey.Questions);
    }

    [Fact]
    public void Questions_cannot_be_modified_from_outside()
    {
        var survey = NewDraftWithQuestions("A");

        var exposed = Assert.IsAssignableFrom<ICollection<SurveyQuestion>>(survey.Questions);

        Assert.True(exposed.IsReadOnly);
        Assert.Throws<NotSupportedException>(exposed.Clear);
    }

    // ---- Publish

    [Fact]
    public void Publish_requires_at_least_one_question()
    {
        var survey = NewDraft();

        DomainAssert.Throws(SurveyErrors.NoQuestion, () => survey.Publish(_clock));

        Assert.Equal(SurveyStatus.Draft, survey.Status);
        Assert.Null(survey.PublishedAtUtc);
        DomainAssert.NoEvents(survey);
    }

    [Fact]
    public void Publish_is_rejected_once_the_only_question_has_been_removed()
    {
        var survey = NewDraftWithQuestions("A");
        survey.RemoveQuestion(survey.Questions.First().Id);

        DomainAssert.Throws(SurveyErrors.NoQuestion, () => survey.Publish(_clock));
    }

    [Fact]
    public void Publish_moves_a_draft_with_one_question_to_published_and_raises_SurveyPublished()
    {
        var survey = NewDraftWithQuestions("A");

        survey.Publish(_clock);

        Assert.Equal(SurveyStatus.Published, survey.Status);
        Assert.Equal(_clock.UtcNow, survey.PublishedAtUtc);
        Assert.Equal(
            new SurveyPublished(Tenant, survey.Id, QuestionCount: 1, _clock.UtcNow),
            DomainAssert.Raised<SurveyPublished>(survey));
    }

    [Fact]
    public void Publish_rejects_an_already_published_survey()
    {
        var survey = NewPublished();
        var publishedAt = survey.PublishedAtUtc;
        _clock.Advance(TimeSpan.FromDays(1));

        DomainAssert.Throws(SurveyErrors.NotDraft, () => survey.Publish(_clock));

        Assert.Equal(publishedAt, survey.PublishedAtUtc);
        DomainAssert.NoEvents(survey);
    }

    [Fact]
    public void Published_survey_rejects_every_edition()
    {
        var survey = NewPublished();
        var questionId = survey.Questions.First().Id;

        DomainAssert.Throws(SurveyErrors.NotDraft, () => survey.Rename("Other"));
        DomainAssert.Throws(SurveyErrors.NotDraft, () => survey.AddQuestion("B", SurveyQuestionType.FreeText));
        DomainAssert.Throws(SurveyErrors.NotDraft, () => survey.ChangeQuestion(questionId, "B", SurveyQuestionType.FreeText));
        DomainAssert.Throws(SurveyErrors.NotDraft, () => survey.MoveQuestion(questionId, 1));
        DomainAssert.Throws(SurveyErrors.NotDraft, () => survey.RemoveQuestion(questionId));

        Assert.Equal("After-dinner survey", survey.Title);
        Assert.Equal(["How was the food?"], Texts(survey));
    }

    // ---- Close

    [Fact]
    public void Close_moves_a_published_survey_to_closed_and_raises_SurveyClosed()
    {
        var survey = NewPublished();
        _clock.Advance(TimeSpan.FromDays(7));

        survey.Close(_clock);

        Assert.Equal(SurveyStatus.Closed, survey.Status);
        Assert.Equal(_clock.UtcNow, survey.ClosedAtUtc);
        Assert.Equal(new SurveyClosed(Tenant, survey.Id, _clock.UtcNow), DomainAssert.Raised<SurveyClosed>(survey));
    }

    [Fact]
    public void Close_rejects_a_draft()
    {
        var survey = NewDraftWithQuestions("A");

        DomainAssert.Throws(SurveyErrors.NotPublished, () => survey.Close(_clock));

        Assert.Equal(SurveyStatus.Draft, survey.Status);
        DomainAssert.NoEvents(survey);
    }

    [Fact]
    public void Close_rejects_an_already_closed_survey()
    {
        var survey = NewClosed();
        var closedAt = survey.ClosedAtUtc;
        _clock.Advance(TimeSpan.FromDays(1));

        DomainAssert.Throws(SurveyErrors.Closed, () => survey.Close(_clock));

        Assert.Equal(closedAt, survey.ClosedAtUtc);
        DomainAssert.NoEvents(survey);
    }

    [Fact]
    public void Closed_survey_rejects_every_mutation()
    {
        var survey = NewClosed();
        var questionId = survey.Questions.First().Id;

        DomainAssert.Throws(SurveyErrors.Closed, () => survey.Rename("Other"));
        DomainAssert.Throws(SurveyErrors.Closed, () => survey.AddQuestion("B", SurveyQuestionType.FreeText));
        DomainAssert.Throws(SurveyErrors.Closed, () => survey.ChangeQuestion(questionId, "B", SurveyQuestionType.FreeText));
        DomainAssert.Throws(SurveyErrors.Closed, () => survey.MoveQuestion(questionId, 1));
        DomainAssert.Throws(SurveyErrors.Closed, () => survey.RemoveQuestion(questionId));
        DomainAssert.Throws(SurveyErrors.Closed, () => survey.Publish(_clock));

        Assert.Equal(SurveyStatus.Closed, survey.Status);
        Assert.Equal(["How was the food?"], Texts(survey));
        DomainAssert.NoEvents(survey);
    }

    // ---- Durcissement : ids vides, horloge

    [Fact]
    public void Create_rejects_an_empty_tenant() =>
        DomainAssert.Throws(CommonErrors.EmptyId, () => Survey.Create(default, "After-dinner survey"));

    [Fact]
    public void Publish_and_Close_stamp_the_state_and_the_event_with_the_same_instant()
    {
        var ticking = new TickingClock(_clock.UtcNow);
        var survey = NewDraft();
        survey.AddQuestion("How was it?", SurveyQuestionType.FreeText);

        survey.Publish(ticking);
        Assert.Equal(survey.PublishedAtUtc, DomainAssert.Raised<SurveyPublished>(survey).OccurredAtUtc);

        survey.Close(ticking);
        Assert.Equal(survey.ClosedAtUtc, DomainAssert.Raised<SurveyClosed>(survey).OccurredAtUtc);
    }

    /// <summary>Avance à chaque lecture : prouve qu'une opération lit l'horloge une seule fois.</summary>
    private sealed class TickingClock(DateTimeOffset start) : IClock
    {
        private DateTimeOffset _now = start;

        public DateTimeOffset UtcNow => _now = _now.AddSeconds(1);
    }
}
