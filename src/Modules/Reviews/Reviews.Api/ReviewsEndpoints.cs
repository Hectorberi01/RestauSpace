using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Reviews.Application;
using Reviews.Application.Commands;
using Reviews.Application.Queries;
using Reviews.Domain;
using SaasRestaurant.BuildingBlocks.Api;

namespace Reviews.Api;

/// <summary>
/// Avis et questionnaires du restaurant sélectionné dans l'access token (ou de la clé d'API). Aucune route ne prend de
/// tenant : ce n'est jamais une valeur de requête. L'hôte chaîne l'autorisation (<c>Tenancy.TenantMemberPolicy</c>) sur
/// le groupe renvoyé. Lire : <see cref="ReviewsPermissions.View"/> (les commentaires sont la parole des clients) ;
/// enregistrer un avis : <see cref="ReviewsPermissions.Submit"/> ; traiter les avis et gérer les questionnaires :
/// <see cref="ReviewsPermissions.Manage"/>.
/// </summary>
public static class ReviewsEndpoints
{
    private const string View = ReviewsPermissions.View;
    private const string Manage = ReviewsPermissions.Manage;
    private const string GetFeedbackRoute = "GetFeedback";
    private const string GetSurveyRoute = "GetSurvey";

    public static RouteGroupBuilder MapReviewsEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/reviews").WithTags("Reviews");
        MapFeedbacks(group);
        MapSurveys(group);
        return group;
    }

    private static void MapFeedbacks(RouteGroupBuilder group)
    {
        group.MapPost("/feedbacks", async (
            SubmitFeedbackRequest request, ICommandHandler<SubmitFeedbackCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(
                new SubmitFeedbackCommand(request.LocationId, request.Rating, request.Comment, request.CustomerId, request.OrderId), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetFeedbackRoute, new { id });
        }).RequirePermission(ReviewsPermissions.Submit);

        // Du plus récent au plus ancien ; période sur la date de soumission [from, to[ ; archivés exclus par défaut.
        group.MapGet("/feedbacks", async (
            Guid? locationId,
            FeedbackStatus? status,
            bool? includeArchived,
            int? minRating,
            int? maxRating,
            DateTimeOffset? from,
            DateTimeOffset? to,
            Guid? customerId,
            Guid? orderId,
            bool? hasComment,
            bool? published,
            int? page,
            int? pageSize,
            IQueryHandler<ListFeedbacksQuery, IReadOnlyList<FeedbackDto>> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await PageRequest.From(page, pageSize).LoadAsync((skip, take) => handler.HandleAsync(
                new ListFeedbacksQuery(
                    new FeedbackFilter(locationId, status, includeArchived ?? false, minRating, maxRating, from, to, customerId, orderId, hasComment, published),
                    skip,
                    take),
                ct))))
            .RequirePermission(View);

        group.MapGet("/feedbacks/summary", async (
            Guid? locationId,
            FeedbackStatus? status,
            bool? includeArchived,
            int? minRating,
            int? maxRating,
            DateTimeOffset? from,
            DateTimeOffset? to,
            IQueryHandler<GetFeedbackSummaryQuery, FeedbackSummaryDto> handler,
            CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(
                new GetFeedbackSummaryQuery(new FeedbackFilter(locationId, status, includeArchived ?? false, minRating, maxRating, from, to)), ct)))
            .RequirePermission(View);

        group.MapGet("/feedbacks/{id:guid}", async (
            Guid id, IQueryHandler<GetFeedbackQuery, FeedbackDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetFeedbackQuery(id), ct)))
            .WithName(GetFeedbackRoute)
            .RequirePermission(View);

        group.MapPost("/feedbacks/{id:guid}/reviewed", (
            Guid id, ICommandHandler<MarkFeedbackReviewedCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new MarkFeedbackReviewedCommand(id), ct)))
            .RequirePermission(Manage);

        group.MapPost("/feedbacks/{id:guid}/publish", (
            Guid id, ICommandHandler<SetFeedbackPublishedCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetFeedbackPublishedCommand(id, true), ct)))
            .RequirePermission(Manage);

        group.MapPost("/feedbacks/{id:guid}/unpublish", (
            Guid id, ICommandHandler<SetFeedbackPublishedCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new SetFeedbackPublishedCommand(id, false), ct)))
            .RequirePermission(Manage);

        group.MapPost("/feedbacks/{id:guid}/archive", (
            Guid id, ICommandHandler<ArchiveFeedbackCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new ArchiveFeedbackCommand(id), ct)))
            .RequirePermission(Manage);
    }

    private static void MapSurveys(RouteGroupBuilder group)
    {
        group.MapGet("/surveys", async (
            SurveyStatus? status, IQueryHandler<ListSurveysQuery, IReadOnlyList<SurveyDto>> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new ListSurveysQuery(status), ct)))
            .RequirePermission(View);

        group.MapGet("/surveys/{id:guid}", async (
            Guid id, IQueryHandler<GetSurveyQuery, SurveyDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetSurveyQuery(id), ct)))
            .WithName(GetSurveyRoute)
            .RequirePermission(View);

        group.MapPost("/surveys", async (
            SurveyTitleRequest request, ICommandHandler<CreateSurveyCommand, Guid> handler, CancellationToken ct) =>
        {
            var id = await handler.HandleAsync(new CreateSurveyCommand(request.Title), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(id), GetSurveyRoute, new { id });
        }).RequirePermission(Manage);

        group.MapPut("/surveys/{id:guid}/title", (
            Guid id, SurveyTitleRequest request, ICommandHandler<RenameSurveyCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RenameSurveyCommand(id, request.Title), ct)))
            .RequirePermission(Manage);

        group.MapPost("/surveys/{id:guid}/questions", async (
            Guid id, SurveyQuestionRequest request, ICommandHandler<AddSurveyQuestionCommand, Guid> handler, CancellationToken ct) =>
        {
            var questionId = await handler.HandleAsync(
                new AddSurveyQuestionCommand(id, request.Text, request.Type, request.IsRequired, request.Choices), ct);
            return TypedResults.CreatedAtRoute(new CreatedResponse(questionId), GetSurveyRoute, new { id });
        }).RequirePermission(Manage);

        group.MapPut("/surveys/{id:guid}/questions/{questionId:guid}", (
            Guid id, Guid questionId, SurveyQuestionRequest request, ICommandHandler<ChangeSurveyQuestionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(
                new ChangeSurveyQuestionCommand(id, questionId, request.Text, request.Type, request.IsRequired, request.Choices), ct)))
            .RequirePermission(Manage);

        group.MapPut("/surveys/{id:guid}/questions/{questionId:guid}/position", (
            Guid id, Guid questionId, QuestionPositionRequest request, ICommandHandler<MoveSurveyQuestionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new MoveSurveyQuestionCommand(id, questionId, request.Position), ct)))
            .RequirePermission(Manage);

        group.MapDelete("/surveys/{id:guid}/questions/{questionId:guid}", (
            Guid id, Guid questionId, ICommandHandler<RemoveSurveyQuestionCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new RemoveSurveyQuestionCommand(id, questionId), ct)))
            .RequirePermission(Manage);

        group.MapPost("/surveys/{id:guid}/publish", (
            Guid id, ICommandHandler<PublishSurveyCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new PublishSurveyCommand(id), ct)))
            .RequirePermission(Manage);

        group.MapPost("/surveys/{id:guid}/close", (
            Guid id, ICommandHandler<CloseSurveyCommand> handler, CancellationToken ct) =>
            Run(handler.HandleAsync(new CloseSurveyCommand(id), ct)))
            .RequirePermission(Manage);

        // Réponses reçues (anonymes) : synthèse par question sur une période facultative.
        group.MapGet("/surveys/{id:guid}/results", async (
            Guid id, DateTimeOffset? from, DateTimeOffset? to, IQueryHandler<GetSurveyResultsQuery, SurveyResultsDto> handler, CancellationToken ct) =>
            TypedResults.Ok(await handler.HandleAsync(new GetSurveyResultsQuery(id, from, to), ct)))
            .RequirePermission(View);
    }

    private static async Task<NoContent> Run(Task command)
    {
        await command;
        return TypedResults.NoContent();
    }
}
