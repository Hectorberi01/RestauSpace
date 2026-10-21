using Reviews.Domain;

namespace Reviews.Api;

public sealed record CreatedResponse(Guid Id);

/// <summary>Note de 1 à 5 ; commentaire facultatif (2000 caractères au plus) ; client et commande facultatifs.</summary>
public sealed record SubmitFeedbackRequest(Guid LocationId, int Rating, string? Comment = null, Guid? CustomerId = null, Guid? OrderId = null);

public sealed record SurveyTitleRequest(string Title);

/// <summary><paramref name="Choices"/> : seulement pour SingleChoice et MultipleChoice (2 à 20 choix distincts).</summary>
public sealed record SurveyQuestionRequest(string Text, SurveyQuestionType Type, bool IsRequired = false, IReadOnlyList<string>? Choices = null);

/// <summary>Nouvelle position de la question, à partir de 1.</summary>
public sealed record QuestionPositionRequest(int Position);
