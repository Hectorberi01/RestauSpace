namespace Reviews.Domain;

public class FeedbackTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly LocationId Location = LocationId.New();
    private static readonly LocationReference Place = new(Tenant, Location, IsOperational: true);
    private static readonly UserId Manager = UserId.New();

    private readonly FakeClock _clock = new();

    private static CustomerReference NewCustomer(bool isAnonymized = false) =>
        new(CustomerId.New(), Tenant, isAnonymized);

    private static OrderReference? OrderOf(OrderId? orderId) =>
        orderId is null ? null : new OrderReference(Tenant, orderId.Value);

    private Feedback NewFeedbackOf(CustomerReference customer, string? comment = "Ask for Ada at table 4", OrderId? orderId = null)
    {
        var feedback = Feedback.Submit(Tenant, Place, Rating.Create(2), comment, _clock, customer, OrderOf(orderId));
        feedback.ClearDomainEvents();
        return feedback;
    }

    private Feedback NewFeedback(int rating = 4, string? comment = "Great service")
    {
        var feedback = Feedback.Submit(Tenant, Place, Rating.Create(rating), comment, _clock);
        feedback.ClearDomainEvents();
        return feedback;
    }

    private Feedback NewReviewedFeedback()
    {
        var feedback = NewFeedback();
        feedback.MarkReviewed(Manager, _clock);
        feedback.ClearDomainEvents();
        return feedback;
    }

    private Feedback NewArchivedFeedback()
    {
        var feedback = NewFeedback();
        feedback.Archive(_clock);
        feedback.ClearDomainEvents();
        return feedback;
    }

    // ---- Submit

    [Fact]
    public void Submit_creates_a_submitted_feedback_linked_to_its_experience()
    {
        var customer = NewCustomer();
        var orderId = OrderId.New();

        var feedback = Feedback.Submit(Tenant, Place, Rating.Create(5), "  Perfect!  ", _clock, customer, OrderOf(orderId));

        Assert.Equal(Tenant, feedback.TenantId);
        Assert.Equal(Location, feedback.LocationId);
        Assert.Equal(customer.CustomerId, feedback.CustomerId);
        Assert.False(feedback.IsCustomerDetached);
        Assert.Null(feedback.CustomerDetachedAtUtc);
        Assert.Equal(orderId, feedback.OrderId);
        Assert.Equal(Rating.Create(5), feedback.Rating);
        Assert.Equal("Perfect!", feedback.Comment);
        Assert.Equal(FeedbackStatus.Submitted, feedback.Status);
        Assert.Equal(_clock.UtcNow, feedback.SubmittedAtUtc);
        Assert.Null(feedback.ReviewedBy);
        Assert.Null(feedback.ReviewedAtUtc);
        Assert.Null(feedback.ArchivedAtUtc);
    }

    [Fact]
    public void Submit_raises_FeedbackSubmitted()
    {
        var customer = NewCustomer();
        var orderId = OrderId.New();

        var feedback = Feedback.Submit(Tenant, Place, Rating.Create(2), "Cold", _clock, customer, OrderOf(orderId));

        Assert.Equal(
            new FeedbackSubmitted(Tenant, feedback.Id, Location, customer.CustomerId, orderId, 2, HasComment: true, _clock.UtcNow),
            DomainAssert.Raised<FeedbackSubmitted>(feedback));
    }

    [Fact]
    public void Submit_rejects_a_customer_of_another_tenant() =>
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => Feedback.Submit(
                Tenant,
                Place,
                Rating.Create(4),
                null,
                _clock,
                new CustomerReference(CustomerId.New(), TenantId.New(), isAnonymized: false)));

    [Fact]
    public void Submit_rejects_an_anonymized_customer() =>
        DomainAssert.Throws(
            FeedbackErrors.CustomerAnonymized,
            () => Feedback.Submit(Tenant, Place, Rating.Create(4), null, _clock, NewCustomer(isAnonymized: true)));

    [Fact]
    public void CustomerReference_is_compared_by_value()
    {
        var customerId = CustomerId.New();

        Assert.Equal(new CustomerReference(customerId, Tenant, false), new CustomerReference(customerId, Tenant, false));
        Assert.NotEqual(new CustomerReference(customerId, Tenant, false), new CustomerReference(customerId, Tenant, true));
    }

    [Fact]
    public void Submit_accepts_an_anonymous_feedback_without_order()
    {
        var feedback = Feedback.Submit(Tenant, Place, Rating.Create(3), null, _clock);

        Assert.Null(feedback.CustomerId);
        Assert.Null(feedback.OrderId);
        Assert.Null(feedback.Comment);
        Assert.False(DomainAssert.Raised<FeedbackSubmitted>(feedback).HasComment);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void Submit_accepts_the_rating_bounds(int rating) =>
        Assert.Equal(rating, NewFeedback(rating).Rating.Value);

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void Submit_cannot_receive_a_rating_outside_1_to_5(int rating) =>
        DomainAssert.Throws(
            FeedbackErrors.RatingOutOfRange,
            () => Feedback.Submit(Tenant, Place, Rating.Create(rating), null, _clock));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Submit_stores_a_blank_comment_as_no_comment(string comment) =>
        Assert.Null(NewFeedback(comment: comment).Comment);

    [Fact]
    public void Submit_accepts_a_comment_at_the_limit_and_rejects_a_longer_one()
    {
        var atLimit = NewFeedback(comment: $"  {new string('c', Feedback.CommentMaxLength)}  ");

        Assert.Equal(Feedback.CommentMaxLength, atLimit.Comment!.Length);
        DomainAssert.Throws(
            FeedbackErrors.CommentTooLong,
            () => NewFeedback(comment: new string('c', Feedback.CommentMaxLength + 1)));
    }

    // ---- MarkReviewed

    [Fact]
    public void MarkReviewed_moves_a_submitted_feedback_to_reviewed()
    {
        var feedback = NewFeedback();
        _clock.Advance(TimeSpan.FromHours(2));

        feedback.MarkReviewed(Manager, _clock);

        Assert.Equal(FeedbackStatus.Reviewed, feedback.Status);
        Assert.Equal(Manager, feedback.ReviewedBy);
        Assert.Equal(_clock.UtcNow, feedback.ReviewedAtUtc);
        Assert.Equal(
            new FeedbackReviewed(Tenant, feedback.Id, Manager, _clock.UtcNow),
            DomainAssert.Raised<FeedbackReviewed>(feedback));
    }

    [Fact]
    public void MarkReviewed_rejects_an_already_reviewed_feedback()
    {
        var feedback = NewReviewedFeedback();
        var reviewedAt = feedback.ReviewedAtUtc;
        _clock.Advance(TimeSpan.FromHours(1));

        DomainAssert.Throws(FeedbackErrors.NotSubmitted, () => feedback.MarkReviewed(UserId.New(), _clock));

        Assert.Equal(Manager, feedback.ReviewedBy);
        Assert.Equal(reviewedAt, feedback.ReviewedAtUtc);
        DomainAssert.NoEvents(feedback);
    }

    [Fact]
    public void MarkReviewed_rejects_an_archived_feedback()
    {
        var feedback = NewArchivedFeedback();

        DomainAssert.Throws(FeedbackErrors.AlreadyArchived, () => feedback.MarkReviewed(Manager, _clock));

        Assert.Equal(FeedbackStatus.Archived, feedback.Status);
        Assert.Null(feedback.ReviewedBy);
        DomainAssert.NoEvents(feedback);
    }

    // ---- Archive

    [Fact]
    public void Archive_moves_a_reviewed_feedback_to_archived()
    {
        var feedback = NewReviewedFeedback();
        _clock.Advance(TimeSpan.FromDays(1));

        feedback.Archive(_clock);

        Assert.Equal(FeedbackStatus.Archived, feedback.Status);
        Assert.Equal(_clock.UtcNow, feedback.ArchivedAtUtc);
        Assert.Equal(Manager, feedback.ReviewedBy);
        Assert.Equal(new FeedbackArchived(Tenant, feedback.Id, _clock.UtcNow), DomainAssert.Raised<FeedbackArchived>(feedback));
    }

    [Fact]
    public void Archive_is_allowed_directly_from_submitted()
    {
        var feedback = NewFeedback();

        feedback.Archive(_clock);

        Assert.Equal(FeedbackStatus.Archived, feedback.Status);
        Assert.Null(feedback.ReviewedAtUtc);
        DomainAssert.Raised<FeedbackArchived>(feedback);
    }

    [Fact]
    public void Archive_rejects_an_already_archived_feedback()
    {
        var feedback = NewArchivedFeedback();
        var archivedAt = feedback.ArchivedAtUtc;
        _clock.Advance(TimeSpan.FromDays(1));

        DomainAssert.Throws(FeedbackErrors.AlreadyArchived, () => feedback.Archive(_clock));

        Assert.Equal(archivedAt, feedback.ArchivedAtUtc);
        DomainAssert.NoEvents(feedback);
    }

    [Fact]
    public void Rating_and_comment_never_change_through_the_lifecycle()
    {
        var feedback = NewFeedback(rating: 2, comment: "Too slow");

        feedback.MarkReviewed(Manager, _clock);
        feedback.Archive(_clock);

        Assert.Equal(2, feedback.Rating.Value);
        Assert.Equal("Too slow", feedback.Comment);
        Assert.All(
            [nameof(Feedback.Rating), nameof(Feedback.Comment)],
            name => Assert.True(typeof(Feedback).GetProperty(name)!.SetMethod!.IsPrivate));
    }

    // ---- DetachCustomer

    [Fact]
    public void DetachCustomer_erases_the_author_and_the_comment_and_keeps_the_rest()
    {
        var orderId = OrderId.New();
        var feedback = NewFeedbackOf(NewCustomer(), orderId: orderId);
        var submittedAt = feedback.SubmittedAtUtc;
        _clock.Advance(TimeSpan.FromDays(30));

        feedback.DetachCustomer(_clock);

        Assert.Null(feedback.CustomerId);
        Assert.Null(feedback.Comment);
        Assert.True(feedback.IsCustomerDetached);
        Assert.Equal(_clock.UtcNow, feedback.CustomerDetachedAtUtc);
        Assert.Equal(2, feedback.Rating.Value);
        Assert.Equal(Location, feedback.LocationId);
        Assert.Equal(orderId, feedback.OrderId);
        Assert.Equal(FeedbackStatus.Submitted, feedback.Status);
        Assert.Equal(submittedAt, feedback.SubmittedAtUtc);
    }

    [Fact]
    public void DetachCustomer_raises_FeedbackCustomerDetached_with_ids_only()
    {
        var feedback = NewFeedbackOf(NewCustomer());

        feedback.DetachCustomer(_clock);

        Assert.Equal(
            new FeedbackCustomerDetached(Tenant, feedback.Id, _clock.UtcNow),
            DomainAssert.Raised<FeedbackCustomerDetached>(feedback));
        Assert.Equal(
            [nameof(FeedbackCustomerDetached.FeedbackId), nameof(FeedbackCustomerDetached.OccurredAtUtc), nameof(FeedbackCustomerDetached.TenantId)],
            typeof(FeedbackCustomerDetached).GetProperties().Select(property => property.Name).Order());
    }

    [Fact]
    public void DetachCustomer_is_allowed_on_a_reviewed_feedback_and_keeps_its_review()
    {
        var feedback = NewFeedbackOf(NewCustomer());
        feedback.MarkReviewed(Manager, _clock);
        var reviewedAt = feedback.ReviewedAtUtc;
        feedback.ClearDomainEvents();

        feedback.DetachCustomer(_clock);

        Assert.Null(feedback.CustomerId);
        Assert.Null(feedback.Comment);
        Assert.Equal(FeedbackStatus.Reviewed, feedback.Status);
        Assert.Equal(Manager, feedback.ReviewedBy);
        Assert.Equal(reviewedAt, feedback.ReviewedAtUtc);
        DomainAssert.Raised<FeedbackCustomerDetached>(feedback);
    }

    [Fact]
    public void DetachCustomer_is_allowed_on_an_archived_feedback()
    {
        var feedback = NewFeedbackOf(NewCustomer());
        feedback.Archive(_clock);
        var archivedAt = feedback.ArchivedAtUtc;
        feedback.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromDays(1));

        feedback.DetachCustomer(_clock);

        Assert.Null(feedback.CustomerId);
        Assert.Null(feedback.Comment);
        Assert.True(feedback.IsCustomerDetached);
        Assert.Equal(FeedbackStatus.Archived, feedback.Status);
        Assert.Equal(archivedAt, feedback.ArchivedAtUtc);
        DomainAssert.Raised<FeedbackCustomerDetached>(feedback);
    }

    [Fact]
    public void DetachCustomer_twice_is_a_silent_no_op()
    {
        var feedback = NewFeedbackOf(NewCustomer());
        feedback.DetachCustomer(_clock);
        var detachedAt = feedback.CustomerDetachedAtUtc;
        feedback.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromDays(1));

        feedback.DetachCustomer(_clock);

        Assert.Equal(detachedAt, feedback.CustomerDetachedAtUtc);
        DomainAssert.NoEvents(feedback);
    }

    [Fact]
    public void DetachCustomer_leaves_an_anonymous_feedback_untouched()
    {
        var feedback = NewFeedback(comment: "Great service");

        feedback.DetachCustomer(_clock);

        Assert.Equal("Great service", feedback.Comment);
        Assert.False(feedback.IsCustomerDetached);
        DomainAssert.NoEvents(feedback);
    }

    [Fact]
    public void A_detached_feedback_still_follows_its_lifecycle()
    {
        var feedback = NewFeedbackOf(NewCustomer());
        feedback.DetachCustomer(_clock);

        feedback.MarkReviewed(Manager, _clock);
        feedback.Archive(_clock);

        Assert.Equal(FeedbackStatus.Archived, feedback.Status);
        Assert.Null(feedback.CustomerId);
    }

    // ---- Durcissement : ids vides, références, horloge

    [Fact]
    public void Submit_rejects_an_empty_tenant() =>
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => Feedback.Submit(default, Place, Rating.Create(4), null, _clock));

    [Fact]
    public void References_reject_empty_ids()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(default, Location, true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(Tenant, default, true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new OrderReference(default, OrderId.New()));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new OrderReference(Tenant, default));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new CustomerReference(default, Tenant, false));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new CustomerReference(CustomerId.New(), default, false));
    }

    [Fact]
    public void Submit_rejects_a_location_of_another_tenant() =>
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => Feedback.Submit(
                Tenant, new LocationReference(TenantId.New(), Location, true), Rating.Create(4), null, _clock));

    [Fact]
    public void Submit_accepts_a_location_that_is_no_longer_operational()
    {
        var closed = new LocationReference(Tenant, Location, IsOperational: false);

        var feedback = Feedback.Submit(Tenant, closed, Rating.Create(4), null, _clock);

        Assert.Equal(Location, feedback.LocationId);
    }

    [Fact]
    public void Submit_rejects_an_order_of_another_tenant() =>
        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => Feedback.Submit(
                Tenant, Place, Rating.Create(4), null, _clock, order: new OrderReference(TenantId.New(), OrderId.New())));

    [Fact]
    public void MarkReviewed_rejects_an_empty_reviewer()
    {
        var feedback = NewFeedback();

        DomainAssert.Throws(CommonErrors.EmptyId, () => feedback.MarkReviewed(default, _clock));
        Assert.Equal(FeedbackStatus.Submitted, feedback.Status);
        DomainAssert.NoEvents(feedback);
    }

    [Fact]
    public void State_timestamps_equal_the_event_instant()
    {
        var ticking = new TickingClock(_clock.UtcNow);

        var reviewed = NewFeedbackOf(NewCustomer());
        reviewed.MarkReviewed(Manager, ticking);
        Assert.Equal(reviewed.ReviewedAtUtc, DomainAssert.Raised<FeedbackReviewed>(reviewed).OccurredAtUtc);

        reviewed.Archive(ticking);
        Assert.Equal(reviewed.ArchivedAtUtc, DomainAssert.Raised<FeedbackArchived>(reviewed).OccurredAtUtc);

        reviewed.DetachCustomer(ticking);
        Assert.Equal(reviewed.CustomerDetachedAtUtc, DomainAssert.Raised<FeedbackCustomerDetached>(reviewed).OccurredAtUtc);
    }

    // ---- Publication

    [Fact]
    public void Publish_shows_the_feedback_and_is_idempotent()
    {
        var feedback = NewFeedback();

        feedback.Publish(_clock);
        feedback.Publish(_clock);

        Assert.True(feedback.IsPublished);
        Assert.Equal(_clock.UtcNow, feedback.PublishedAtUtc);
        Assert.Single(feedback.DomainEvents.OfType<FeedbackPublished>());
    }

    [Fact]
    public void Unpublish_hides_the_feedback_and_is_idempotent()
    {
        var feedback = NewReviewedFeedback();
        feedback.Publish(_clock);
        feedback.ClearDomainEvents();

        feedback.Unpublish(_clock);
        feedback.Unpublish(_clock);

        Assert.False(feedback.IsPublished);
        Assert.Single(feedback.DomainEvents.OfType<FeedbackUnpublished>());
    }

    [Fact]
    public void An_archived_feedback_cannot_be_published_and_archiving_unpublishes()
    {
        var published = NewFeedback();
        published.Publish(_clock);

        published.Archive(_clock);

        Assert.False(published.IsPublished);
        DomainAssert.Throws(FeedbackErrors.AlreadyArchived, () => NewArchivedFeedback().Publish(_clock));
    }

    [Fact]
    public void A_published_feedback_stays_published_without_its_comment_when_the_author_is_erased()
    {
        var feedback = NewFeedbackOf(NewCustomer());
        feedback.Publish(_clock);

        feedback.DetachCustomer(_clock);

        Assert.True(feedback.IsPublished);
        Assert.Null(feedback.Comment);
    }

    /// <summary>Avance à chaque lecture : prouve qu'une opération ne lit l'horloge qu'une fois.</summary>
    private sealed class TickingClock(DateTimeOffset start) : IClock
    {
        private DateTimeOffset _now = start;

        public DateTimeOffset UtcNow => _now = _now.AddSeconds(1);
    }
}
