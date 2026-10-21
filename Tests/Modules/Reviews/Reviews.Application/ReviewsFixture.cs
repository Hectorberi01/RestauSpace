using Reviews.Application.Abstractions;
using Reviews.Application.Commands;
using Reviews.Application.Queries;
using Reviews.Domain;

namespace Reviews.Application;

/// <summary>
/// Ports en mémoire du module Reviews. Comme les vrais, ils ne répondent jamais que dans le tenant qui leur est donné.
/// Le tenant de « la requête » est <see cref="CurrentTenantId"/> ; l'utilisateur, <see cref="CurrentUser"/>.
/// </summary>
public sealed class ReviewsFixture
    : ITenantContext, IReviewsUnitOfWork, IReviewTenants, IReviewLocations, IReviewOrders, IReviewCustomers, IReviewsCurrentUser
{
    public ReviewsFixture()
    {
        TenantA = TenantId.New();
        TenantB = TenantId.New();
        WritableTenants.Add(TenantA);
        WritableTenants.Add(TenantB);
        CurrentTenantId = TenantA;
        Location = GivenLocation();
    }

    public TenantId TenantA { get; }

    public TenantId TenantB { get; }

    public TenantId? CurrentTenantId { get; set; }

    /// <summary>Utilisateur du token ; null pour une clé d'API.</summary>
    public UserId? CurrentUser { get; set; } = UserId.New();

    UserId? IReviewsCurrentUser.UserId => CurrentUser;

    /// <summary>Un établissement du tenant A.</summary>
    public Guid Location { get; }

    public HashSet<TenantId> WritableTenants { get; } = [];

    public List<LocationReference> Locations { get; } = [];

    public List<ReviewedOrder> Orders { get; } = [];

    /// <summary>Fiches du faux module Customers : (tenant, anonymisée ?).</summary>
    public Dictionary<CustomerId, (TenantId Tenant, bool IsAnonymized)> Customers { get; } = [];

    public InMemoryFeedbacks Feedbacks { get; } = new();

    public InMemorySurveys Surveys { get; } = new();

    public InMemorySurveyResponses Responses { get; } = new();

    public FakeClock Clock { get; } = new();

    public int SaveCount { get; private set; }

    public ReviewsWorkspace Workspace => new(this, this, Feedbacks, Surveys, this);

    public SubmitFeedbackHandler Submit => new(Workspace, this, this, this, Clock);

    public MarkFeedbackReviewedHandler MarkReviewed => new(Workspace, this, Clock);

    public ArchiveFeedbackHandler Archive => new(Workspace, Clock);

    public DetachCustomerFeedbacksHandler DetachCustomer => new(Workspace, Clock);

    public SetFeedbackPublishedHandler SetPublished => new(Workspace, Clock);

    public GetPublicReviewsHandler PublicReviews => new(Workspace);

    public GetFeedbackHandler GetFeedback => new(Workspace);

    public ListFeedbacksHandler ListFeedbacks => new(Workspace);

    public GetFeedbackSummaryHandler Summary => new(Workspace);

    public CreateSurveyHandler CreateSurvey => new(Workspace);

    public RenameSurveyHandler RenameSurvey => new(Workspace);

    public AddSurveyQuestionHandler AddQuestion => new(Workspace);

    public ChangeSurveyQuestionHandler ChangeQuestion => new(Workspace);

    public MoveSurveyQuestionHandler MoveQuestion => new(Workspace);

    public RemoveSurveyQuestionHandler RemoveQuestion => new(Workspace);

    public PublishSurveyHandler Publish => new(Workspace, Clock);

    public CloseSurveyHandler Close => new(Workspace, Clock);

    public GetSurveyHandler GetSurvey => new(Workspace);

    public ListSurveysHandler ListSurveys => new(Workspace);

    public SubmitSurveyResponseHandler SubmitResponse => new(Workspace, Responses, this, this, Clock);

    public GetSurveyResultsHandler Results => new(Workspace, Responses);

    public GetPublicSurveyHandler PublicSurvey => new(Workspace);

    public GetActiveSurveyHandler ActiveSurvey => new(Workspace);

    public Guid GivenLocation(TenantId? tenantId = null, bool isOperational = true)
    {
        var location = new LocationReference(tenantId ?? TenantA, LocationId.New(), isOperational);
        Locations.Add(location);
        return location.LocationId.Value;
    }

    public Guid GivenCustomer(bool isAnonymized = false, TenantId? tenantId = null)
    {
        var id = CustomerId.New();
        Customers[id] = (tenantId ?? TenantA, isAnonymized);
        return id.Value;
    }

    public Guid GivenOrder(Guid? locationId = null, Guid? customerId = null, TenantId? tenantId = null)
    {
        var order = new ReviewedOrder(
            tenantId ?? TenantA,
            OrderId.New(),
            LocationId.From(locationId ?? Location),
            customerId is { } customer ? CustomerId.From(customer) : (CustomerId?)null);
        Orders.Add(order);
        return order.OrderId.Value;
    }

    /// <summary>Avis enregistré dans le tenant courant, compteur d'écritures remis à zéro.</summary>
    public async Task<Guid> GivenFeedbackAsync(int rating = 4, string? comment = "Très bon accueil", Guid? customerId = null, Guid? orderId = null)
    {
        var id = await Submit.HandleAsync(new SubmitFeedbackCommand(Location, rating, comment, customerId, orderId));
        SaveCount = 0;
        return id;
    }

    /// <summary>Questionnaire en brouillon avec deux questions, compteur d'écritures remis à zéro.</summary>
    public async Task<(Guid Survey, Guid First, Guid Second)> GivenDraftSurveyAsync()
    {
        var survey = await CreateSurvey.HandleAsync(new CreateSurveyCommand("Votre visite"));
        var first = await AddQuestion.HandleAsync(new AddSurveyQuestionCommand(survey, "Note globale ?", SurveyQuestionType.Rating, IsRequired: true));
        var second = await AddQuestion.HandleAsync(
            new AddSurveyQuestionCommand(survey, "Plat préféré ?", SurveyQuestionType.SingleChoice, Choices: ["Poisson", "Poulet"]));
        SaveCount = 0;
        return (survey, first, second);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    Task<bool> IReviewTenants.AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        Task.FromResult(WritableTenants.Contains(tenantId));

    Task<LocationReference?> IReviewLocations.FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken) =>
        Task.FromResult<LocationReference?>(Locations.SingleOrDefault(location => location.TenantId == tenantId && location.LocationId == locationId));

    Task<ReviewedOrder?> IReviewOrders.FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken) =>
        Task.FromResult<ReviewedOrder?>(Orders.SingleOrDefault(order => order.TenantId == tenantId && order.OrderId == orderId));

    Task<CustomerReference?> IReviewCustomers.FindAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken) =>
        Task.FromResult<CustomerReference?>(Customers.TryGetValue(customerId, out var customer) && customer.Tenant == tenantId
            ? new CustomerReference(customerId, tenantId, customer.IsAnonymized)
            : null);

    public sealed class InMemorySurveyResponses : ISurveyResponseRepository
    {
        public List<SurveyResponse> Items { get; } = [];

        public Task<bool> ExistsForOrderAsync(TenantId tenantId, SurveyId surveyId, OrderId orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(response => response.TenantId == tenantId && response.SurveyId == surveyId && response.OrderId == orderId));

        public Task<IReadOnlyList<SurveyResponse>> ListAsync(
            TenantId tenantId, SurveyId surveyId, DateTimeOffset? from, DateTimeOffset? to, int skip, int take, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SurveyResponse>>(Matching(tenantId, surveyId, from, to).OrderByDescending(response => response.SubmittedAtUtc).Skip(skip).Take(take).ToList());

        public Task<int> CountAsync(TenantId tenantId, SurveyId surveyId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken = default) =>
            Task.FromResult(Matching(tenantId, surveyId, from, to).Count());

        public void Add(SurveyResponse response) => Items.Add(response);

        private IEnumerable<SurveyResponse> Matching(TenantId tenantId, SurveyId surveyId, DateTimeOffset? from, DateTimeOffset? to) =>
            Items.Where(response => response.TenantId == tenantId && response.SurveyId == surveyId
                && (from is null || response.SubmittedAtUtc >= from) && (to is null || response.SubmittedAtUtc < to));
    }

    public sealed class InMemoryFeedbacks : IFeedbackRepository
    {
        public List<Feedback> Items { get; } = [];

        public FeedbackSearch? LastSearch { get; private set; }

        public Feedback Stored(Guid id) => Items.Single(feedback => feedback.Id.Value == id);

        public Task<Feedback?> FindAsync(TenantId tenantId, FeedbackId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Feedback?>(Items.SingleOrDefault(feedback => feedback.TenantId == tenantId && feedback.Id == id));

        public Task<bool> ExistsForOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.Any(feedback => feedback.TenantId == tenantId && feedback.OrderId == orderId));

        public Task<IReadOnlyList<Feedback>> SearchAsync(
            TenantId tenantId, FeedbackSearch search, int skip, int take, CancellationToken cancellationToken = default)
        {
            LastSearch = search;
            return Task.FromResult<IReadOnlyList<Feedback>>(Matching(tenantId, search)
                .OrderByDescending(feedback => feedback.SubmittedAtUtc)
                .Skip(skip)
                .Take(take)
                .ToList());
        }

        public Task<IReadOnlyList<FeedbackCount>> CountAsync(TenantId tenantId, FeedbackSearch search, CancellationToken cancellationToken = default)
        {
            LastSearch = search;
            return Task.FromResult<IReadOnlyList<FeedbackCount>>(Matching(tenantId, search)
                .GroupBy(feedback => (feedback.Rating.Value, feedback.Status, feedback.Comment is not null))
                .Select(group => new FeedbackCount(group.Key.Item1, group.Key.Item2, group.Key.Item3, group.Count()))
                .ToList());
        }

        public Task<IReadOnlyList<Feedback>> ListForCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Feedback>>(Items.Where(feedback => feedback.TenantId == tenantId && feedback.CustomerId == customerId).ToList());

        public void Add(Feedback feedback) => Items.Add(feedback);

        private IEnumerable<Feedback> Matching(TenantId tenantId, FeedbackSearch search) => Items
            .Where(feedback => feedback.TenantId == tenantId)
            .Where(feedback => search.LocationId is null || feedback.LocationId == search.LocationId)
            .Where(feedback => search.Status is { } status
                ? feedback.Status == status
                : search.IncludeArchived || feedback.Status != FeedbackStatus.Archived)
            .Where(feedback => search.MinRating is null || feedback.Rating.Value >= search.MinRating)
            .Where(feedback => search.MaxRating is null || feedback.Rating.Value <= search.MaxRating)
            .Where(feedback => search.HasComment is null || (feedback.Comment is not null) == search.HasComment)
            .Where(feedback => search.IsPublished is null || feedback.IsPublished == search.IsPublished)
            .Where(feedback => search.LocationIds is null || search.LocationIds.Contains(feedback.LocationId));
    }

    public sealed class InMemorySurveys : ISurveyRepository
    {
        public List<Survey> Items { get; } = [];

        public Survey Stored(Guid id) => Items.Single(survey => survey.Id.Value == id);

        public Task<Survey?> FindAsync(TenantId tenantId, SurveyId id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Survey?>(Items.SingleOrDefault(survey => survey.TenantId == tenantId && survey.Id == id));

        public Task<IReadOnlyList<Survey>> ListAsync(TenantId tenantId, SurveyStatus? status, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Survey>>(Items
                .Where(survey => survey.TenantId == tenantId && (status == null || survey.Status == status))
                .OrderByDescending(survey => survey.Id.Value)
                .ToList());

        public void Add(Survey survey) => Items.Add(survey);
    }
}
