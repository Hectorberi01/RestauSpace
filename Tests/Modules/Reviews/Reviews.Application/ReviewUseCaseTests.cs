using Reviews.Application.Commands;
using Reviews.Application.Queries;
using Reviews.Domain;

namespace Reviews.Application;

public sealed class FeedbackUseCaseTests
{
    private readonly ReviewsFixture _fixture = new();

    [Fact]
    public async Task A_feedback_is_submitted_in_the_tenant_of_the_context_with_its_customer_and_order()
    {
        var customer = _fixture.GivenCustomer();
        var order = _fixture.GivenOrder(customerId: customer);

        var id = await _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 5, "  Excellent  ", customer, order));

        var feedback = _fixture.Feedbacks.Stored(id);
        Assert.Equal(_fixture.TenantA, feedback.TenantId);
        Assert.Equal((5, "Excellent"), (feedback.Rating.Value, feedback.Comment));
        Assert.Equal(CustomerId.From(customer), feedback.CustomerId);
        Assert.Equal(OrderId.From(order), feedback.OrderId);
        Assert.Equal(FeedbackStatus.Submitted, feedback.Status);
        Assert.Equal(1, _fixture.SaveCount);
        var submitted = DomainAssert.Raised<FeedbackSubmitted>(feedback);
        Assert.True(submitted.HasComment);
    }

    [Fact]
    public async Task An_anonymous_feedback_on_a_closed_location_is_accepted()
    {
        var closed = _fixture.GivenLocation(isOperational: false);

        var id = await _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(closed, 2));

        var feedback = _fixture.Feedbacks.Stored(id);
        Assert.Null(feedback.CustomerId);
        Assert.Null(feedback.Comment);
    }

    [Fact]
    public async Task References_of_another_restaurant_are_not_found()
    {
        var otherLocation = _fixture.GivenLocation(_fixture.TenantB);
        var otherCustomer = _fixture.GivenCustomer(tenantId: _fixture.TenantB);
        var otherOrder = _fixture.GivenOrder(tenantId: _fixture.TenantB);

        var location = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(otherLocation, 4)));
        var customer = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 4, CustomerId: otherCustomer)));
        var order = await Assert.ThrowsAsync<NotFoundException>(
            () => _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 4, OrderId: otherOrder)));
        var empty = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(Guid.Empty, 4)));

        Assert.Equal(ReviewsApplicationErrors.LocationNotFound, location.Error);
        Assert.Equal(ReviewsApplicationErrors.CustomerNotFound, customer.Error);
        Assert.Equal(ReviewsApplicationErrors.OrderNotFound, order.Error);
        Assert.Equal(ReviewsApplicationErrors.LocationNotFound, empty.Error);
        Assert.Empty(_fixture.Feedbacks.Items);
    }

    [Fact]
    public async Task The_order_must_match_the_location_and_the_customer_and_have_a_single_feedback()
    {
        var customer = _fixture.GivenCustomer();
        var elsewhere = _fixture.GivenOrder(locationId: _fixture.GivenLocation());
        var someoneElse = _fixture.GivenOrder(customerId: _fixture.GivenCustomer());
        var order = _fixture.GivenOrder();
        await _fixture.GivenFeedbackAsync(orderId: order);

        var location = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 4, OrderId: elsewhere)));
        var owner = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 4, CustomerId: customer, OrderId: someoneElse)));
        var twice = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 1, OrderId: order)));

        Assert.Equal(ReviewsApplicationErrors.OrderLocationMismatch, location.Error);
        Assert.Equal(ReviewsApplicationErrors.OrderCustomerMismatch, owner.Error);
        Assert.Equal(ReviewsApplicationErrors.OrderAlreadyReviewed, twice.Error);
        Assert.Single(_fixture.Feedbacks.Items);
    }

    [Fact]
    public async Task Domain_rules_still_apply_rating_comment_and_anonymized_author()
    {
        var anonymized = _fixture.GivenCustomer(isAnonymized: true);

        var rating = await Assert.ThrowsAsync<DomainException>(() => _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 6)));
        var comment = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 3, new string('x', Feedback.CommentMaxLength + 1))));
        var author = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 3, CustomerId: anonymized)));

        Assert.Equal(FeedbackErrors.RatingOutOfRange, rating.Error);
        Assert.Equal(FeedbackErrors.CommentTooLong, comment.Error);
        Assert.Equal(FeedbackErrors.CustomerAnonymized, author.Error);
    }

    [Fact]
    public async Task A_suspended_restaurant_keeps_reading_but_cannot_write()
    {
        var id = await _fixture.GivenFeedbackAsync();
        _fixture.WritableTenants.Remove(_fixture.TenantA);

        var submit = await Assert.ThrowsAsync<DomainException>(() => _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 4)));
        var archive = await Assert.ThrowsAsync<DomainException>(() => _fixture.Archive.HandleAsync(new ArchiveFeedbackCommand(id)));

        Assert.Equal(ReviewsApplicationErrors.TenantNotWritable, submit.Error);
        Assert.Equal(ReviewsApplicationErrors.TenantNotWritable, archive.Error);
        Assert.Equal(id, (await _fixture.GetFeedback.HandleAsync(new GetFeedbackQuery(id))).Id);
    }

    [Fact]
    public async Task The_reviewer_is_the_signed_in_user_never_an_api_key()
    {
        var id = await _fixture.GivenFeedbackAsync();
        var reviewer = _fixture.CurrentUser;
        var keyFeedback = await _fixture.GivenFeedbackAsync();

        await _fixture.MarkReviewed.HandleAsync(new MarkFeedbackReviewedCommand(id));
        _fixture.CurrentUser = null;
        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.MarkReviewed.HandleAsync(new MarkFeedbackReviewedCommand(keyFeedback)));

        var read = await _fixture.GetFeedback.HandleAsync(new GetFeedbackQuery(id));
        Assert.Equal(FeedbackStatus.Reviewed, read.Status);
        Assert.Equal(reviewer?.Value, read.ReviewedBy);
        Assert.Equal(_fixture.Clock.UtcNow, read.ReviewedAtUtc);
        Assert.Equal(ReviewsApplicationErrors.ReviewerRequired, refused.Error);
        Assert.Equal(FeedbackStatus.Submitted, _fixture.Feedbacks.Stored(keyFeedback).Status);
    }

    [Fact]
    public async Task A_feedback_of_another_restaurant_cannot_be_read_or_changed()
    {
        var id = await _fixture.GivenFeedbackAsync();
        _fixture.CurrentTenantId = _fixture.TenantB;

        var read = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.GetFeedback.HandleAsync(new GetFeedbackQuery(id)));
        var archive = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Archive.HandleAsync(new ArchiveFeedbackCommand(id)));

        Assert.Equal(ReviewsApplicationErrors.FeedbackNotFound, read.Error);
        Assert.Equal(ReviewsApplicationErrors.FeedbackNotFound, archive.Error);
        Assert.Empty(await _fixture.ListFeedbacks.HandleAsync(new ListFeedbacksQuery(new FeedbackFilter())));
        Assert.Equal(FeedbackStatus.Submitted, _fixture.Feedbacks.Stored(id).Status);
    }

    [Fact]
    public async Task Without_a_selected_restaurant_nothing_is_read()
    {
        _fixture.CurrentTenantId = null;

        var refused = await Assert.ThrowsAsync<ForbiddenException>(() => _fixture.ListFeedbacks.HandleAsync(new ListFeedbacksQuery(new FeedbackFilter())));

        Assert.Equal(TenancyErrors.TenantRequired, refused.Error);
    }

    [Fact]
    public async Task Anonymizing_a_customer_detaches_all_its_feedback_even_when_suspended_and_can_be_replayed()
    {
        var customer = _fixture.GivenCustomer();
        var first = await _fixture.GivenFeedbackAsync(customerId: customer);
        var second = await _fixture.GivenFeedbackAsync(customerId: customer);
        await _fixture.Archive.HandleAsync(new ArchiveFeedbackCommand(second));
        var anonymous = await _fixture.GivenFeedbackAsync();
        _fixture.WritableTenants.Remove(_fixture.TenantA);

        var detached = await _fixture.DetachCustomer.HandleAsync(new DetachCustomerFeedbacksCommand(customer));
        var replayed = await _fixture.DetachCustomer.HandleAsync(new DetachCustomerFeedbacksCommand(customer));

        Assert.Equal((2, 0), (detached, replayed));
        Assert.All(new[] { first, second }, id =>
        {
            var feedback = _fixture.Feedbacks.Stored(id);
            Assert.Null(feedback.CustomerId);
            Assert.Null(feedback.Comment);
            Assert.True(feedback.IsCustomerDetached);
        });
        // Un avis anonyme dès le départ garde son commentaire : rien ne le relie au client effacé.
        Assert.NotNull(_fixture.Feedbacks.Stored(anonymous).Comment);
        Assert.Equal(0, await _fixture.DetachCustomer.HandleAsync(new DetachCustomerFeedbacksCommand(Guid.Empty)));
    }

    [Fact]
    public async Task Lists_hide_archived_feedback_unless_asked_and_filter_by_rating()
    {
        var low = await _fixture.GivenFeedbackAsync(rating: 1);
        await _fixture.GivenFeedbackAsync(rating: 5, comment: null);
        var archived = await _fixture.GivenFeedbackAsync(rating: 2);
        await _fixture.Archive.HandleAsync(new ArchiveFeedbackCommand(archived));

        var visible = await _fixture.ListFeedbacks.HandleAsync(new ListFeedbacksQuery(new FeedbackFilter()));
        var all = await _fixture.ListFeedbacks.HandleAsync(new ListFeedbacksQuery(new FeedbackFilter(IncludeArchived: true)));
        var bad = await _fixture.ListFeedbacks.HandleAsync(new ListFeedbacksQuery(new FeedbackFilter(MaxRating: 2)));
        var onlyArchived = await _fixture.ListFeedbacks.HandleAsync(new ListFeedbacksQuery(new FeedbackFilter(Status: FeedbackStatus.Archived)));

        Assert.Equal(2, visible.Count);
        Assert.Equal(3, all.Count);
        Assert.Equal(low, Assert.Single(bad).Id);
        Assert.Equal(archived, Assert.Single(onlyArchived).Id);
    }

    [Fact]
    public async Task Invalid_filters_and_pages_are_refused_and_an_empty_id_finds_nothing()
    {
        await _fixture.GivenFeedbackAsync();

        var page = await Assert.ThrowsAsync<DomainException>(() => _fixture.ListFeedbacks.HandleAsync(new ListFeedbacksQuery(new FeedbackFilter(), Take: 101)));
        var rating = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.ListFeedbacks.HandleAsync(new ListFeedbacksQuery(new FeedbackFilter(MinRating: 4, MaxRating: 2))));
        var period = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.Summary.HandleAsync(new GetFeedbackSummaryQuery(new FeedbackFilter(FromUtc: _fixture.Clock.UtcNow, ToUtc: _fixture.Clock.UtcNow))));

        Assert.Equal(ReviewsApplicationErrors.PageInvalid, page.Error);
        Assert.Equal(ReviewsApplicationErrors.RatingFilterInvalid, rating.Error);
        Assert.Equal(ReviewsApplicationErrors.PeriodInvalid, period.Error);
        Assert.Empty(await _fixture.ListFeedbacks.HandleAsync(new ListFeedbacksQuery(new FeedbackFilter(LocationId: Guid.Empty))));
        Assert.Equal(0, (await _fixture.Summary.HandleAsync(new GetFeedbackSummaryQuery(new FeedbackFilter(CustomerId: Guid.Empty)))).Count);
    }

    [Fact]
    public async Task The_summary_counts_ratings_comments_and_statuses()
    {
        await _fixture.GivenFeedbackAsync(rating: 5);
        await _fixture.GivenFeedbackAsync(rating: 4, comment: null);
        var reviewed = await _fixture.GivenFeedbackAsync(rating: 2);
        await _fixture.MarkReviewed.HandleAsync(new MarkFeedbackReviewedCommand(reviewed));
        var archived = await _fixture.GivenFeedbackAsync(rating: 1);
        await _fixture.Archive.HandleAsync(new ArchiveFeedbackCommand(archived));

        var summary = await _fixture.Summary.HandleAsync(new GetFeedbackSummaryQuery(new FeedbackFilter()));
        var withArchived = await _fixture.Summary.HandleAsync(new GetFeedbackSummaryQuery(new FeedbackFilter(IncludeArchived: true)));

        Assert.Equal(3, summary.Count);
        Assert.Equal(3.67m, summary.AverageRating);
        Assert.Equal(new[] { 0, 1, 0, 1, 1 }, summary.CountByRating.Select(count => count.Count));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, summary.CountByRating.Select(count => count.Rating));
        Assert.Equal((2, 2, 1, 0), (summary.WithComment, summary.Submitted, summary.Reviewed, summary.Archived));
        Assert.Equal((4, 3m, 1), (withArchived.Count, withArchived.AverageRating!.Value, withArchived.Archived));
    }

    [Fact]
    public void The_summary_of_nothing_has_no_average()
    {
        var summary = GetFeedbackSummaryHandler.Summarize([]);

        Assert.Equal(0, summary.Count);
        Assert.Null(summary.AverageRating);
        Assert.Equal(5, summary.CountByRating.Count);
    }
}

public sealed class SurveyUseCaseTests
{
    private readonly ReviewsFixture _fixture = new();

    [Fact]
    public async Task A_draft_is_built_question_by_question_then_published_and_closed()
    {
        var (survey, first, second) = await _fixture.GivenDraftSurveyAsync();

        await _fixture.RenameSurvey.HandleAsync(new RenameSurveyCommand(survey, " Votre dîner "));
        await _fixture.ChangeQuestion.HandleAsync(
            new ChangeSurveyQuestionCommand(survey, second, "Dessert préféré ?", SurveyQuestionType.MultipleChoice, false, ["Glace", "Fruit", "Gâteau"]));
        await _fixture.MoveQuestion.HandleAsync(new MoveSurveyQuestionCommand(survey, second, 1));
        await _fixture.Publish.HandleAsync(new PublishSurveyCommand(survey));

        var read = await _fixture.GetSurvey.HandleAsync(new GetSurveyQuery(survey));
        Assert.Equal(("Votre dîner", SurveyStatus.Published), (read.Title, read.Status));
        Assert.Equal(new[] { second, first }, read.Questions.Select(question => question.Id));
        Assert.Equal(new[] { 1, 2 }, read.Questions.Select(question => question.Order));
        Assert.Equal(new[] { "Glace", "Fruit", "Gâteau" }, read.Questions[0].Choices);
        Assert.Equal(_fixture.Clock.UtcNow, read.PublishedAtUtc);
        Assert.Equal(4, _fixture.SaveCount);

        await _fixture.Close.HandleAsync(new CloseSurveyCommand(survey));
        Assert.Equal(SurveyStatus.Closed, (await _fixture.GetSurvey.HandleAsync(new GetSurveyQuery(survey))).Status);
        DomainAssert.Raised<SurveyClosed>(_fixture.Surveys.Stored(survey));
    }

    [Fact]
    public async Task A_published_survey_is_frozen()
    {
        var (survey, first, _) = await _fixture.GivenDraftSurveyAsync();
        await _fixture.Publish.HandleAsync(new PublishSurveyCommand(survey));

        var add = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.AddQuestion.HandleAsync(new AddSurveyQuestionCommand(survey, "Encore ?", SurveyQuestionType.FreeText)));
        var remove = await Assert.ThrowsAsync<DomainException>(() => _fixture.RemoveQuestion.HandleAsync(new RemoveSurveyQuestionCommand(survey, first)));

        Assert.Equal(SurveyErrors.NotDraft, add.Error);
        Assert.Equal(SurveyErrors.NotDraft, remove.Error);
        Assert.Equal(2, _fixture.Surveys.Stored(survey).Questions.Count);
    }

    [Fact]
    public async Task Removing_a_question_renumbers_and_an_unknown_question_is_refused()
    {
        var (survey, first, second) = await _fixture.GivenDraftSurveyAsync();

        await _fixture.RemoveQuestion.HandleAsync(new RemoveSurveyQuestionCommand(survey, first));
        var unknown = await Assert.ThrowsAsync<DomainException>(
            () => _fixture.MoveQuestion.HandleAsync(new MoveSurveyQuestionCommand(survey, Guid.NewGuid(), 1)));
        var empty = await Assert.ThrowsAsync<DomainException>(() => _fixture.RemoveQuestion.HandleAsync(new RemoveSurveyQuestionCommand(survey, Guid.Empty)));

        var remaining = Assert.Single((await _fixture.GetSurvey.HandleAsync(new GetSurveyQuery(survey))).Questions);
        Assert.Equal((second, 1), (remaining.Id, remaining.Order));
        Assert.Equal(SurveyErrors.QuestionNotFound, unknown.Error);
        Assert.Equal(SurveyErrors.QuestionNotFound, empty.Error);
    }

    [Fact]
    public async Task A_survey_of_another_restaurant_is_not_found_and_lists_stay_in_the_tenant()
    {
        var (survey, _, _) = await _fixture.GivenDraftSurveyAsync();
        _fixture.CurrentTenantId = _fixture.TenantB;
        var mine = await _fixture.CreateSurvey.HandleAsync(new CreateSurveyCommand("Autre"));

        var publish = await Assert.ThrowsAsync<NotFoundException>(() => _fixture.Publish.HandleAsync(new PublishSurveyCommand(survey)));

        Assert.Equal(ReviewsApplicationErrors.SurveyNotFound, publish.Error);
        Assert.Equal(mine, Assert.Single(await _fixture.ListSurveys.HandleAsync(new ListSurveysQuery())).Id);
        Assert.Empty(await _fixture.ListSurveys.HandleAsync(new ListSurveysQuery(SurveyStatus.Published)));
        Assert.Equal(SurveyStatus.Draft, _fixture.Surveys.Stored(survey).Status);
    }

    [Fact]
    public async Task Public_reviews_give_the_overall_rating_and_only_the_published_feedbacks_without_author()
    {
        var customer = _fixture.GivenCustomer();
        var shown = await _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 5, "Délicieux", customer));
        await _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 3, "Un peu long"));
        var spam = await _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 1, "Spam"));
        await _fixture.Archive.HandleAsync(new ArchiveFeedbackCommand(spam));

        await _fixture.SetPublished.HandleAsync(new SetFeedbackPublishedCommand(shown, true));
        var result = await _fixture.PublicReviews.HandleAsync(new GetPublicReviewsQuery(null));

        Assert.Equal((2, 4m), (result.Count, result.AverageRating));
        var review = Assert.Single(result.Reviews);
        Assert.Equal((shown, 5, "Délicieux"), (review.Id, review.Rating, review.Comment));
    }

    [Fact]
    public async Task Public_reviews_keep_to_the_requested_locations_and_the_tenant_of_the_context()
    {
        var other = _fixture.GivenLocation();
        var here = await _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 4));
        var there = await _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(other, 2));
        await _fixture.SetPublished.HandleAsync(new SetFeedbackPublishedCommand(here, true));
        await _fixture.SetPublished.HandleAsync(new SetFeedbackPublishedCommand(there, true));

        var result = await _fixture.PublicReviews.HandleAsync(new GetPublicReviewsQuery([_fixture.Location]));
        _fixture.CurrentTenantId = _fixture.TenantB;
        var otherTenant = await _fixture.PublicReviews.HandleAsync(new GetPublicReviewsQuery(null));

        Assert.Equal(here, Assert.Single(result.Reviews).Id);
        Assert.Equal(1, result.Count);
        Assert.Equal(0, otherTenant.Count);
        Assert.Empty(otherTenant.Reviews);
    }

    [Fact]
    public async Task Publishing_a_feedback_of_another_restaurant_is_not_found()
    {
        var id = await _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 4));
        _fixture.CurrentTenantId = _fixture.TenantB;

        await Assert.ThrowsAsync<NotFoundException>(() => _fixture.SetPublished.HandleAsync(new SetFeedbackPublishedCommand(id, true)));
    }

    [Fact]
    public async Task A_review_left_on_the_site_is_attributed_to_the_customer_of_the_order_unless_anonymized()
    {
        var customer = _fixture.GivenCustomer();
        var order = _fixture.GivenOrder(customerId: customer);
        var erased = _fixture.GivenCustomer();
        var erasedOrder = _fixture.GivenOrder(customerId: erased);
        _fixture.Customers[CustomerId.From(erased)] = (_fixture.TenantA, true);

        var attributed = await _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 5, "Bon", OrderId: order, AuthorIsOrderCustomer: true));
        var anonymous = await _fixture.Submit.HandleAsync(new SubmitFeedbackCommand(_fixture.Location, 4, OrderId: erasedOrder, AuthorIsOrderCustomer: true));

        Assert.Equal(CustomerId.From(customer), _fixture.Feedbacks.Stored(attributed).CustomerId);
        Assert.Null(_fixture.Feedbacks.Stored(anonymous).CustomerId);
    }
}
