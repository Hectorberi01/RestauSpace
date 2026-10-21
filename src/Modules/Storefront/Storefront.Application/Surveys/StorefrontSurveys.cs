using Storefront.Application.Abstractions;
using Storefront.Application.Queries;
using Storefront.Application.Checkout;

namespace Storefront.Application.Surveys;

public static class StorefrontSurveyErrors
{
    // Même réponse pour un questionnaire inconnu, en brouillon ou d'un autre restaurant.
    public static readonly DomainError SurveyNotFound = new("storefront.survey.not_found", "This survey does not exist.");

    public static readonly DomainError OrderNotCompleted = new("storefront.survey.order_not_completed", "Answer once your order is completed.");
}

/// <summary>Un questionnaire pour le site : celui demandé, ou (sans id) celui proposé après une commande.</summary>
public sealed record GetStorefrontSurveyQuery(Guid? SurveyId) : IQuery<StorefrontSurvey>;

public sealed class GetStorefrontSurveyHandler(StorefrontWorkspace workspace, IStorefrontClient client, IStorefrontSurveys surveys)
    : IQueryHandler<GetStorefrontSurveyQuery, StorefrontSurvey>
{
    public async Task<StorefrontSurvey> HandleAsync(GetStorefrontSurveyQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        DomainException.ThrowIf(client.AppId is null, StorefrontApplicationErrors.ClientRequired);
        var tenant = workspace.CurrentTenant;
        var survey = query.SurveyId is { } id
            ? id == Guid.Empty ? null : await surveys.FindAsync(tenant, id, cancellationToken)
            : await surveys.ActiveAsync(tenant, cancellationToken);
        return survey ?? throw new NotFoundException(StorefrontSurveyErrors.SurveyNotFound);
    }
}

/// <summary>
/// Réponse d'un client du site. Avec le jeton de suivi d'une commande TERMINÉE, la réponse lui est rattachée (une seule
/// par commande) ; sinon, elle est libre (QR code, lien partagé), éventuellement pour un établissement.
/// </summary>
public sealed record SubmitStorefrontSurveyCommand(Guid SurveyId, string? OrderToken, Guid? LocationId, IReadOnlyList<StorefrontSurveyAnswer>? Answers) : ICommand
{
    public override string ToString() => $"SubmitStorefrontSurveyCommand ({SurveyId}, ***)";
}

public sealed class SubmitStorefrontSurveyHandler(
    StorefrontWorkspace workspace,
    IStorefrontClient client,
    IStorefrontSurveys surveys,
    IStorefrontOrders orders,
    IOnlineOrderRepository onlineOrders,
    OrderTrackingTokens tokens) : ICommandHandler<SubmitStorefrontSurveyCommand>
{
    public async Task HandleAsync(SubmitStorefrontSurveyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var app = await StorefrontReader.AppAsync(workspace, client, cancellationToken);
        var tenant = workspace.CurrentTenant;
        OrderId? order = null;
        var location = command.LocationId;
        if (location is { } requested && !StorefrontReader.Serves(app, client, LocationId.From(requested)))
        {
            throw new NotFoundException(StorefrontApplicationErrors.LocationNotFound);
        }

        if (!string.IsNullOrEmpty(command.OrderToken))
        {
            var (_, view) = await GuestOrders.LoadAsync(workspace, orders, onlineOrders, tokens, command.OrderToken, cancellationToken);
            DomainException.ThrowIf(view.Status != GuestOrderStatus.Completed, StorefrontSurveyErrors.OrderNotCompleted);
            order = view.OrderId;
            location = null;
        }

        await surveys.SubmitAsync(tenant, command.SurveyId, order, location, command.Answers ?? [], cancellationToken);
    }
}
