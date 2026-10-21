namespace Reviews.Domain;

/// <summary>
/// Avis interne laissé sur une expérience dans un établissement. Le contenu est la parole du client : il n'est
/// jamais modifié, seulement effacé avec le lien vers son auteur quand ce client est anonymisé.
/// </summary>
public sealed class Feedback : AggregateRoot<FeedbackId>, ITenantOwned
{
    public const int CommentMaxLength = 2000;

    private Feedback(
        FeedbackId id,
        TenantId tenantId,
        LocationId locationId,
        CustomerId? customerId,
        OrderId? orderId,
        Rating rating,
        string? comment,
        DateTimeOffset submittedAtUtc) : base(id)
    {
        TenantId = tenantId;
        LocationId = locationId;
        CustomerId = customerId;
        OrderId = orderId;
        Rating = rating;
        Comment = comment;
        Status = FeedbackStatus.Submitted;
        SubmittedAtUtc = submittedAtUtc;
    }

    // Réservé à l'ORM.
    private Feedback()
    {
    }

    public TenantId TenantId { get; private set; }

    public LocationId LocationId { get; private set; }

    public CustomerId? CustomerId { get; private set; }

    public OrderId? OrderId { get; private set; }

    public Rating Rating { get; private set; } = null!;

    public string? Comment { get; private set; }

    public FeedbackStatus Status { get; private set; }

    public DateTimeOffset SubmittedAtUtc { get; private set; }

    public UserId? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAtUtc { get; private set; }

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public DateTimeOffset? CustomerDetachedAtUtc { get; private set; }

    /// <summary>Publié par le restaurant sur son site et son application (note, commentaire et date, jamais l'auteur).</summary>
    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public bool IsPublished => PublishedAtUtc is not null;

    /// <summary>Vrai une fois l'auteur effacé ; faux pour un avis anonyme dès le départ.</summary>
    public bool IsCustomerDetached => CustomerDetachedAtUtc is not null;

    /// <param name="location">Établissement évalué : même tenant ; il n'a plus besoin d'être opérationnel.</param>
    /// <param name="customer">Auteur, s'il est connu. Null pour un avis anonyme.</param>
    /// <param name="order">Commande évaluée, si connue : même tenant. Seul son id est stocké.</param>
    public static Feedback Submit(
        TenantId tenantId,
        LocationReference location,
        Rating rating,
        string? comment,
        IClock clock,
        CustomerReference? customer = null,
        OrderReference? order = null)
    {
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(rating);
        ArgumentNullException.ThrowIfNull(clock);
        Guard.NotEmpty(tenantId);

        DomainException.ThrowIf(location.TenantId != tenantId, CommonErrors.TenantMismatch);
        DomainException.ThrowIf(order is not null && order.TenantId != tenantId, CommonErrors.TenantMismatch);

        if (customer is not null)
        {
            DomainException.ThrowIf(customer.TenantId != tenantId, CommonErrors.TenantMismatch);
            DomainException.ThrowIf(customer.IsAnonymized, FeedbackErrors.CustomerAnonymized);
        }

        var locationId = location.LocationId;
        var customerId = customer?.CustomerId;
        var orderId = order?.OrderId;

        var normalizedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        DomainException.ThrowIf(normalizedComment?.Length > CommentMaxLength, FeedbackErrors.CommentTooLong);

        var feedback = new Feedback(
            FeedbackId.New(), tenantId, locationId, customerId, orderId, rating, normalizedComment, clock.UtcNow);
        feedback.Raise(new FeedbackSubmitted(
            tenantId,
            feedback.Id,
            locationId,
            customerId,
            orderId,
            rating.Value,
            normalizedComment is not null,
            feedback.SubmittedAtUtc));

        return feedback;
    }

    public void MarkReviewed(UserId reviewedBy, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        DomainException.ThrowIf(Status == FeedbackStatus.Archived, FeedbackErrors.AlreadyArchived);
        DomainException.ThrowIf(Status != FeedbackStatus.Submitted, FeedbackErrors.NotSubmitted);
        Guard.NotEmpty(reviewedBy);

        var now = clock.UtcNow;
        Status = FeedbackStatus.Reviewed;
        ReviewedBy = reviewedBy;
        ReviewedAtUtc = now;
        Raise(new FeedbackReviewed(TenantId, Id, reviewedBy, now));
    }

    /// <summary>Permis depuis Submitted (spam, doublon : rien à examiner) et depuis Reviewed.</summary>
    public void Archive(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        DomainException.ThrowIf(Status == FeedbackStatus.Archived, FeedbackErrors.AlreadyArchived);

        var now = clock.UtcNow;
        Status = FeedbackStatus.Archived;
        ArchivedAtUtc = now;
        PublishedAtUtc = null;
        Raise(new FeedbackArchived(TenantId, Id, now));
    }

    /// <summary>
    /// Le restaurant choisit d'afficher cet avis publiquement. Impossible une fois archivé ; déjà publié : sans effet.
    /// L'auteur n'est jamais publié, seulement la note, le commentaire et la date.
    /// </summary>
    public void Publish(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        DomainException.ThrowIf(Status == FeedbackStatus.Archived, FeedbackErrors.AlreadyArchived);
        if (IsPublished)
        {
            return;
        }

        var now = clock.UtcNow;
        PublishedAtUtc = now;
        Raise(new FeedbackPublished(TenantId, Id, now));
    }

    /// <summary>Retire l'avis de l'affichage public ; non publié : sans effet.</summary>
    public void Unpublish(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (!IsPublished)
        {
            return;
        }

        PublishedAtUtc = null;
        Raise(new FeedbackUnpublished(TenantId, Id, clock.UtcNow));
    }

    /// <summary>
    /// Effacement consécutif à l'anonymisation de l'auteur : supprime le lien et le commentaire (un texte libre peut
    /// identifier son auteur). Ce n'est pas un changement métier, il est donc permis dans tous les statuts, Archived
    /// compris. Note, établissement, commande et statut restent : les statistiques demeurent valides. Ne fait rien
    /// s'il n'y a pas (ou plus) de client : un avis qui n'a jamais eu d'auteur garde son commentaire, car rien ne
    /// relie ce texte au client effacé.
    /// </summary>
    public void DetachCustomer(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (CustomerId is null)
        {
            return;
        }

        var now = clock.UtcNow;
        CustomerId = null;
        Comment = null;
        CustomerDetachedAtUtc = now;
        Raise(new FeedbackCustomerDetached(TenantId, Id, now));
    }
}
