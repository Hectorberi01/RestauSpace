using Reviews.Domain;

namespace Reviews.Application.Commands;

// Questionnaires du restaurant, gérés par son équipe (reviews.manage). Un brouillon se modifie librement ; publié, il
// est figé (ses questions gardent leur sens pour les réponses) ; fermé, il ne change plus. Les règles sont celles de
// l'agrégat ; ici, chargement dans le tenant du contexte et écriture.

/// <summary>Crée un questionnaire en brouillon. Renvoie son id.</summary>
public sealed record CreateSurveyCommand(string Title) : ICommand<Guid>;

public sealed class CreateSurveyHandler(ReviewsWorkspace workspace) : ICommandHandler<CreateSurveyCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateSurveyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var survey = Survey.Create(workspace.CurrentTenant, command.Title);
        workspace.Surveys.Add(survey);
        await workspace.SaveAsync(cancellationToken);
        return survey.Id.Value;
    }
}

public sealed record RenameSurveyCommand(Guid SurveyId, string Title) : ICommand;

public sealed class RenameSurveyHandler(ReviewsWorkspace workspace) : ICommandHandler<RenameSurveyCommand>
{
    public async Task HandleAsync(RenameSurveyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var survey = await workspace.LoadSurveyAsync(command.SurveyId, cancellationToken);
        survey.Rename(command.Title);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Ajoute une question à la fin du brouillon. <paramref name="Choices"/> : seulement pour un type à choix (2 à 20).
/// Renvoie l'id de la question.
/// </summary>
public sealed record AddSurveyQuestionCommand(
    Guid SurveyId, string Text, SurveyQuestionType Type, bool IsRequired = false, IReadOnlyList<string>? Choices = null) : ICommand<Guid>;

public sealed class AddSurveyQuestionHandler(ReviewsWorkspace workspace) : ICommandHandler<AddSurveyQuestionCommand, Guid>
{
    public async Task<Guid> HandleAsync(AddSurveyQuestionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var survey = await workspace.LoadSurveyAsync(command.SurveyId, cancellationToken);
        var questionId = survey.AddQuestion(command.Text, command.Type, command.IsRequired, command.Choices);
        await workspace.SaveAsync(cancellationToken);
        return questionId.Value;
    }
}

/// <summary>Remplace le texte, le type, l'obligation et les choix d'une question du brouillon (sa position ne change pas).</summary>
public sealed record ChangeSurveyQuestionCommand(
    Guid SurveyId, Guid QuestionId, string Text, SurveyQuestionType Type, bool IsRequired = false, IReadOnlyList<string>? Choices = null) : ICommand;

public sealed class ChangeSurveyQuestionHandler(ReviewsWorkspace workspace) : ICommandHandler<ChangeSurveyQuestionCommand>
{
    public async Task HandleAsync(ChangeSurveyQuestionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var survey = await workspace.LoadSurveyAsync(command.SurveyId, cancellationToken);
        survey.ChangeQuestion(QuestionIdOf(command.QuestionId), command.Text, command.Type, command.IsRequired, command.Choices);
        await workspace.SaveAsync(cancellationToken);
    }

    /// <summary>Un id vide ne désigne aucune question de ce questionnaire : même erreur qu'un id inconnu.</summary>
    internal static SurveyQuestionId QuestionIdOf(Guid id)
    {
        DomainException.ThrowIf(id == Guid.Empty, SurveyErrors.QuestionNotFound);
        return SurveyQuestionId.From(id);
    }
}

/// <summary>Déplace une question à la position <paramref name="Position"/> (à partir de 1) ; les autres se décalent.</summary>
public sealed record MoveSurveyQuestionCommand(Guid SurveyId, Guid QuestionId, int Position) : ICommand;

public sealed class MoveSurveyQuestionHandler(ReviewsWorkspace workspace) : ICommandHandler<MoveSurveyQuestionCommand>
{
    public async Task HandleAsync(MoveSurveyQuestionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var survey = await workspace.LoadSurveyAsync(command.SurveyId, cancellationToken);
        survey.MoveQuestion(ChangeSurveyQuestionHandler.QuestionIdOf(command.QuestionId), command.Position);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record RemoveSurveyQuestionCommand(Guid SurveyId, Guid QuestionId) : ICommand;

public sealed class RemoveSurveyQuestionHandler(ReviewsWorkspace workspace) : ICommandHandler<RemoveSurveyQuestionCommand>
{
    public async Task HandleAsync(RemoveSurveyQuestionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var survey = await workspace.LoadSurveyAsync(command.SurveyId, cancellationToken);
        survey.RemoveQuestion(ChangeSurveyQuestionHandler.QuestionIdOf(command.QuestionId));
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Publie le brouillon (au moins une question) : ses questions sont désormais figées.</summary>
public sealed record PublishSurveyCommand(Guid SurveyId) : ICommand;

public sealed class PublishSurveyHandler(ReviewsWorkspace workspace, IClock clock) : ICommandHandler<PublishSurveyCommand>
{
    public async Task HandleAsync(PublishSurveyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var survey = await workspace.LoadSurveyAsync(command.SurveyId, cancellationToken);
        survey.Publish(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record CloseSurveyCommand(Guid SurveyId) : ICommand;

public sealed class CloseSurveyHandler(ReviewsWorkspace workspace, IClock clock) : ICommandHandler<CloseSurveyCommand>
{
    public async Task HandleAsync(CloseSurveyCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var survey = await workspace.LoadSurveyAsync(command.SurveyId, cancellationToken);
        survey.Close(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}
