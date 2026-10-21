namespace Reviews.Domain;

public static class FeedbackErrors
{
    public static readonly DomainError RatingOutOfRange = new("reviews.rating.out_of_range", "A rating must be between 1 and 5.");
    public static readonly DomainError CommentTooLong = new("reviews.feedback.comment_too_long", "The comment is too long.");
    public static readonly DomainError CustomerAnonymized = new("reviews.feedback.customer_anonymized", "An anonymized customer cannot be the author of a feedback.");
    public static readonly DomainError NotSubmitted = new("reviews.feedback.not_submitted", "Only a submitted feedback can be marked as reviewed.");
    public static readonly DomainError AlreadyArchived = new("reviews.feedback.already_archived", "The feedback is archived and can no longer change.");
}
