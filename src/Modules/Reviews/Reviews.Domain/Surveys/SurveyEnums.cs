namespace Reviews.Domain;

public enum SurveyStatus
{
    Draft = 1,
    Published = 2,
    Closed = 3,
}

public enum SurveyQuestionType
{
    Rating = 1,
    FreeText = 2,
    SingleChoice = 3,
    MultipleChoice = 4,
}
