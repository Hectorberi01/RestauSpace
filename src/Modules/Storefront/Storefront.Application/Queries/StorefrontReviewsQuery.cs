using Storefront.Application.Abstractions;
using Storefront.Application.Checkout;

namespace Storefront.Application.Queries;

/// <summary>
/// Les avis à afficher : note moyenne, répartition et avis publiés, d'un établissement servi par l'application (404
/// sinon) ou de tous ceux qu'elle sert. <paramref name="Take"/> : de 0 à 50.
/// </summary>
public sealed record GetStorefrontReviewsQuery(Guid? LocationId, int Take = 10) : IQuery<StorefrontReviews>;

public sealed class GetStorefrontReviewsHandler(StorefrontWorkspace workspace, IStorefrontClient client, IStorefrontReviews reviews, IClock clock)
    : IQueryHandler<GetStorefrontReviewsQuery, StorefrontReviews>
{
    public const int MaxTake = 50;

    public static readonly DomainError TakeInvalid = new("storefront.reviews.take_invalid", "take must be between 0 and 50.");

    public async Task<StorefrontReviews> HandleAsync(GetStorefrontReviewsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        DomainException.ThrowIf(query.Take is < 0 or > MaxTake, TakeInvalid);
        var app = await StorefrontReader.AppAsync(workspace, client, cancellationToken);
        var tenant = workspace.CurrentTenant;

        IReadOnlyList<LocationId>? locations;
        if (query.LocationId is { } id)
        {
            var location = id == Guid.Empty ? (LocationId?)null : LocationId.From(id);
            if (location is not { } served || !StorefrontReader.Serves(app, client, served))
            {
                throw new NotFoundException(StorefrontApplicationErrors.LocationNotFound);
            }

            locations = [served];
        }
        else
        {
            locations = app.LocationIds.Count == 0 && client.DeviceLocationId is null
                ? null
                : (await workspace.Locations.ListAsync(tenant, clock.UtcNow, cancellationToken))
                    .Select(location => location.LocationId)
                    .Where(location => StorefrontReader.Serves(app, client, location))
                    .ToList();
        }

        return await reviews.GetAsync(tenant, locations, query.Take, cancellationToken);
    }
}

/// <summary>
/// L'avis d'un client sur sa commande en ligne TERMINÉE, désignée par son jeton de suivi : une note de 1 à 5 et un
/// commentaire facultatif, un seul avis par commande. Le restaurant décide ensuite s'il le publie.
/// </summary>
public sealed record SubmitStorefrontReviewCommand(string? OrderToken, int Rating, string? Comment) : ICommand<Guid>
{
    public override string ToString() => $"SubmitStorefrontReviewCommand({Rating}, ***)";
}

public sealed class SubmitStorefrontReviewHandler(
    StorefrontWorkspace workspace,
    IStorefrontClient client,
    IStorefrontReviews reviews,
    IStorefrontOrders orders,
    IOnlineOrderRepository onlineOrders,
    OrderTrackingTokens tokens) : ICommandHandler<SubmitStorefrontReviewCommand, Guid>
{
    public static readonly DomainError OrderNotCompleted =
        new("storefront.review.order_not_completed", "A review can be left once the order is completed.");

    public async Task<Guid> HandleAsync(SubmitStorefrontReviewCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var app = await StorefrontReader.AppAsync(workspace, client, cancellationToken);
        var (_, view) = await GuestOrders.LoadAsync(workspace, orders, onlineOrders, tokens, command.OrderToken, cancellationToken);
        if (!StorefrontReader.Serves(app, client, view.LocationId))
        {
            throw new NotFoundException(CheckoutErrors.OrderNotFound);
        }

        DomainException.ThrowIf(view.Status != GuestOrderStatus.Completed, OrderNotCompleted);
        return await reviews.SubmitAsync(workspace.CurrentTenant, view.LocationId, view.OrderId, command.Rating, command.Comment, cancellationToken);
    }
}
