using Reviews.Application.Abstractions;
using Reviews.Domain;

namespace Reviews.Application.Commands;

/// <summary>
/// Enregistre l'avis d'un client sur un établissement (tablette en salle, application du restaurant par clé d'API).
/// <paramref name="CustomerId"/> et <paramref name="OrderId"/> sont facultatifs : sans client, l'avis est anonyme dès le
/// départ. Établissement, commande et client sont chargés dans le tenant du CONTEXTE (404 sinon) ; la commande doit
/// avoir été prise dans cet établissement, pour ce client s'il est donné et si elle en a un, et n'a qu'un avis. Le
/// commentaire n'est jamais journalisé. <paramref name="AuthorIsOrderCustomer"/> (avis laissé sur le site après une
/// commande) : l'auteur est le client de la commande, s'il en a un et qu'il n'est pas anonymisé. Renvoie l'id de l'avis.
/// </summary>
public sealed record SubmitFeedbackCommand(
    Guid LocationId,
    int Rating,
    string? Comment = null,
    Guid? CustomerId = null,
    Guid? OrderId = null,
    bool AuthorIsOrderCustomer = false) : ICommand<Guid>
{
    public override string ToString() => $"SubmitFeedbackCommand({LocationId}, {Rating}, ***)";
}

public sealed class SubmitFeedbackHandler(
    ReviewsWorkspace workspace,
    IReviewLocations locations,
    IReviewOrders orders,
    IReviewCustomers customers,
    IClock clock) : ICommandHandler<SubmitFeedbackCommand, Guid>
{
    public async Task<Guid> HandleAsync(SubmitFeedbackCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var tenant = workspace.CurrentTenant;
        // La note d'abord : la règle la moins chère, sans aller voir les autres modules.
        var rating = Rating.Create(command.Rating);

        var location = (command.LocationId == Guid.Empty
                ? null
                : await locations.FindAsync(tenant, LocationId.From(command.LocationId), cancellationToken))
            ?? throw new NotFoundException(ReviewsApplicationErrors.LocationNotFound);

        CustomerReference? customer = null;
        if (command.CustomerId is { } customerId)
        {
            customer = (customerId == Guid.Empty ? null : await customers.FindAsync(tenant, CustomerId.From(customerId), cancellationToken))
                ?? throw new NotFoundException(ReviewsApplicationErrors.CustomerNotFound);
        }

        ReviewedOrder? order = null;
        if (command.OrderId is { } orderId)
        {
            order = (orderId == Guid.Empty ? null : await orders.FindAsync(tenant, OrderId.From(orderId), cancellationToken))
                ?? throw new NotFoundException(ReviewsApplicationErrors.OrderNotFound);
            DomainException.ThrowIf(order.LocationId != location.LocationId, ReviewsApplicationErrors.OrderLocationMismatch);
            DomainException.ThrowIf(
                customer is not null && order.CustomerId is { } orderCustomer && orderCustomer != customer.CustomerId,
                ReviewsApplicationErrors.OrderCustomerMismatch);
            if (command.AuthorIsOrderCustomer && customer is null && order.CustomerId is { } author
                && await customers.FindAsync(tenant, author, cancellationToken) is { IsAnonymized: false } reference)
            {
                customer = reference;
            }

            // Contrôle avant d'écrire ; l'index unique (tenant, commande) tranche entre deux envois simultanés.
            DomainException.ThrowIf(
                await workspace.Feedbacks.ExistsForOrderAsync(tenant, order.OrderId, cancellationToken),
                ReviewsApplicationErrors.OrderAlreadyReviewed);
        }

        var feedback = Feedback.Submit(tenant, location, rating, command.Comment, clock, customer, order?.ToReference());
        workspace.Feedbacks.Add(feedback);
        await workspace.SaveAsync(cancellationToken);
        return feedback.Id.Value;
    }
}

/// <summary>Un membre de l'équipe a lu et traité l'avis. L'auteur de l'examen est l'utilisateur du token (jamais une clé d'API).</summary>
public sealed record MarkFeedbackReviewedCommand(Guid FeedbackId) : ICommand;

public sealed class MarkFeedbackReviewedHandler(ReviewsWorkspace workspace, IReviewsCurrentUser currentUser, IClock clock)
    : ICommandHandler<MarkFeedbackReviewedCommand>
{
    public async Task HandleAsync(MarkFeedbackReviewedCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reviewer = currentUser.UserId ?? throw new ForbiddenException(ReviewsApplicationErrors.ReviewerRequired);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var feedback = await workspace.LoadFeedbackAsync(command.FeedbackId, cancellationToken);
        feedback.MarkReviewed(reviewer, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Archive l'avis (traité, spam, doublon) : il sort des listes et des statistiques par défaut.</summary>
public sealed record ArchiveFeedbackCommand(Guid FeedbackId) : ICommand;

public sealed class ArchiveFeedbackHandler(ReviewsWorkspace workspace, IClock clock) : ICommandHandler<ArchiveFeedbackCommand>
{
    public async Task HandleAsync(ArchiveFeedbackCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var feedback = await workspace.LoadFeedbackAsync(command.FeedbackId, cancellationToken);
        feedback.Archive(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Publie (ou retire) l'avis sur le site et l'application du restaurant : note, commentaire et date, sans l'auteur.</summary>
public sealed record SetFeedbackPublishedCommand(Guid FeedbackId, bool Published) : ICommand;

public sealed class SetFeedbackPublishedHandler(ReviewsWorkspace workspace, IClock clock) : ICommandHandler<SetFeedbackPublishedCommand>
{
    public async Task HandleAsync(SetFeedbackPublishedCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var feedback = await workspace.LoadFeedbackAsync(command.FeedbackId, cancellationToken);
        if (command.Published)
        {
            feedback.Publish(clock);
        }
        else
        {
            feedback.Unpublish(clock);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Le client a été anonymisé (Customers, événement <c>CustomerAnonymized</c>) : retire le lien ET le commentaire de TOUS
/// ses avis, archivés compris. Idempotent (rejouable sans effet) et possible même pour un restaurant suspendu : une
/// obligation légale ne dépend pas de l'abonnement. Renvoie le nombre d'avis détachés.
/// </summary>
public sealed record DetachCustomerFeedbacksCommand(Guid CustomerId) : ICommand<int>;

public sealed class DetachCustomerFeedbacksHandler(ReviewsWorkspace workspace, IClock clock) : ICommandHandler<DetachCustomerFeedbacksCommand, int>
{
    public async Task<int> HandleAsync(DetachCustomerFeedbacksCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CustomerId == Guid.Empty)
        {
            return 0;
        }

        var feedbacks = await workspace.Feedbacks.ListForCustomerAsync(workspace.CurrentTenant, CustomerId.From(command.CustomerId), cancellationToken);
        if (feedbacks.Count == 0)
        {
            return 0;
        }

        foreach (var feedback in feedbacks)
        {
            feedback.DetachCustomer(clock);
        }

        await workspace.SaveAsync(cancellationToken);
        return feedbacks.Count;
    }
}
