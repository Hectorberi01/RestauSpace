using Reviews.Application.Abstractions;
using Reviews.Domain;

namespace Reviews.Application;

/// <summary>
/// Étapes communes aux cas d'usage : le tenant vient du contexte de la requête (jamais de la requête), une écriture
/// exige un tenant qui les accepte, chaque avis ou questionnaire est chargé dans ce tenant seulement (404 sinon).
/// Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class ReviewsWorkspace(
    ITenantContext tenantContext,
    IReviewTenants tenants,
    IFeedbackRepository feedbacks,
    ISurveyRepository surveys,
    IReviewsUnitOfWork unitOfWork)
{
    public const int MaxTake = 100;

    public IFeedbackRepository Feedbacks => feedbacks;

    public ISurveyRepository Surveys => surveys;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    /// <summary>Refuse l'écriture pour un tenant suspendu, fermé ou inconnu.</summary>
    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken))
        {
            throw new DomainException(ReviewsApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<Feedback> LoadFeedbackAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await feedbacks.FindAsync(CurrentTenant, FeedbackId.From(id), cancellationToken))
        ?? throw new NotFoundException(ReviewsApplicationErrors.FeedbackNotFound);

    public async Task<Survey> LoadSurveyAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await surveys.FindAsync(CurrentTenant, SurveyId.From(id), cancellationToken))
        ?? throw new NotFoundException(ReviewsApplicationErrors.SurveyNotFound);

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);

    public static void EnsurePage(int skip, int take) =>
        DomainException.ThrowIf(skip < 0 || take is < 1 or > MaxTake, ReviewsApplicationErrors.PageInvalid);
}
