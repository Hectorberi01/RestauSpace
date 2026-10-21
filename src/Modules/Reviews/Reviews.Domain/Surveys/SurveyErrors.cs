namespace Reviews.Domain;

public static class SurveyErrors
{
    public static readonly DomainError TitleRequired = new("reviews.survey.title_required", "Survey title is required.");
    public static readonly DomainError TitleTooLong = new("reviews.survey.title_too_long", "Survey title is too long.");
    public static readonly DomainError NotDraft = new("reviews.survey.not_draft", "Only a draft survey can be edited or published.");
    public static readonly DomainError NotPublished = new("reviews.survey.not_published", "Only a published survey can be closed.");
    public static readonly DomainError Closed = new("reviews.survey.closed", "The survey is closed and can no longer change.");
    public static readonly DomainError NoQuestion = new("reviews.survey.no_question", "A survey needs at least one question to be published.");
    public static readonly DomainError TooManyQuestions = new("reviews.survey.too_many_questions", "The survey has reached its maximum number of questions.");
    public static readonly DomainError QuestionNotFound = new("reviews.survey.question_not_found", "The question does not belong to this survey.");
    public static readonly DomainError QuestionOrderOutOfRange = new("reviews.survey.question_order_out_of_range", "The question position is outside the survey.");

    public static readonly DomainError QuestionTextRequired = new("reviews.question.text_required", "Question text is required.");
    public static readonly DomainError QuestionTextTooLong = new("reviews.question.text_too_long", "Question text is too long.");
    public static readonly DomainError QuestionTypeUnknown = new("reviews.question.type_unknown", "Question type is not supported.");
    public static readonly DomainError ChoicesRequired = new("reviews.question.choices_required", "A choice question needs at least two choices.");
    public static readonly DomainError ChoicesNotAllowed = new("reviews.question.choices_not_allowed", "Only choice questions can have choices.");
    public static readonly DomainError TooManyChoices = new("reviews.question.too_many_choices", "The question has too many choices.");
    public static readonly DomainError ChoiceInvalid = new("reviews.question.choice_invalid", "A choice must be a non-empty text of at most 200 characters.");
    public static readonly DomainError ChoiceDuplicate = new("reviews.question.choice_duplicate", "Choices of a question must be distinct.");
}
