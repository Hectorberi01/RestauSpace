namespace Reviews.Domain;

public sealed record SurveyPublished(
    TenantId TenantId,
    SurveyId SurveyId,
    int QuestionCount,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);

public sealed record SurveyClosed(
    TenantId TenantId,
    SurveyId SurveyId,
    DateTimeOffset OccurredAtUtc) : DomainEvent(OccurredAtUtc);
