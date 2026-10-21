namespace Reviews.Application;

/// <summary>Erreurs levées par les cas d'usage de Reviews (celles des règles métier sont dans Reviews.Domain).</summary>
public static class ReviewsApplicationErrors
{
    public static readonly DomainError ConcurrentUpdate =
        new("reviews.concurrent_update", "The resource was changed by someone else; reload it and try again.");

    // Aussi la réponse pour une ressource d'un autre tenant : on ne révèle jamais qu'elle existe.
    public static readonly DomainError FeedbackNotFound = new("reviews.feedback.not_found", "The feedback does not exist.");
    public static readonly DomainError SurveyNotFound = new("reviews.survey.not_found", "The survey does not exist.");
    public static readonly DomainError LocationNotFound = new("reviews.location.not_found", "The location does not exist.");
    public static readonly DomainError OrderNotFound = new("reviews.order.not_found", "The order does not exist.");
    public static readonly DomainError CustomerNotFound = new("reviews.customer.not_found", "The customer does not exist.");

    public static readonly DomainError TenantNotWritable =
        new("reviews.tenant.not_writable", "The restaurant account is suspended or closed: its feedback can be read, not changed.");

    public static readonly DomainError OrderAlreadyReviewed =
        new("reviews.feedback.order_already_reviewed", "This order already has a feedback.", DomainErrorKind.Conflict);

    public static readonly DomainError OrderLocationMismatch =
        new("reviews.feedback.order_location_mismatch", "The order was not taken at this location.");

    public static readonly DomainError OrderCustomerMismatch =
        new("reviews.feedback.order_customer_mismatch", "The order belongs to another customer.");

    public static readonly DomainError ReviewerRequired =
        new("reviews.feedback.reviewer_required", "Only a signed-in team member can mark a feedback as reviewed.");

    public static readonly DomainError PageInvalid =
        new("reviews.page.invalid", "Skip must be zero or more and take between 1 and 100.");

    public static readonly DomainError PeriodInvalid =
        new("reviews.period.invalid", "The period must start before it ends.");

    public static readonly DomainError RatingFilterInvalid =
        new("reviews.rating_filter.invalid", "Rating filters must be between 1 and 5, the minimum not above the maximum.");
}
