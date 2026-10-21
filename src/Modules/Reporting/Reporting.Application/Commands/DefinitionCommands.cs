using System.Globalization;
using Reporting.Application.Metrics;
using Reporting.Application.Scheduling;
using Reporting.Domain;

namespace Reporting.Application.Commands;

// Rapports enregistrés du restaurant du contexte : création, modification, planification. Écritures refusées pour un
// restaurant suspendu ou fermé ; la liste blanche est le catalogue des projections (ReportingMetrics.Catalog).

/// <summary>Un critère de filtre saisi : champ du catalogue, opérateur, valeur (liste séparée par des virgules pour « In »).</summary>
public sealed record ReportFilterInput(string? Field, FilterOperator Operator, string? Value)
{
    public ReportFilter ToFilter() => ReportFilter.Create(Field, Operator, Value);

    /// <summary>Critères du domaine, revérifiés contre les règles de lecture (opérateur pris en charge, valeur bien formée).</summary>
    public static IReadOnlyList<ReportFilter> ToFilters(IEnumerable<ReportFilterInput>? inputs)
    {
        var filters = (inputs ?? Enumerable.Empty<ReportFilterInput>()).Select(input => input.ToFilter()).ToList();
        ReportFilterRules.Validate(filters);
        return filters;
    }
}

public sealed record CreateReportDefinitionCommand(string? Name, IReadOnlyList<string>? Metrics, IReadOnlyList<ReportFilterInput>? Filters = null)
    : ICommand<Guid>;

public sealed class CreateReportDefinitionHandler(ReportingWorkspace workspace, IClock clock) : ICommandHandler<CreateReportDefinitionCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateReportDefinitionCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var definition = ReportDefinition.Create(
            workspace.CurrentTenant,
            command.Name,
            command.Metrics ?? Array.Empty<string>(),
            ReportFilterInput.ToFilters(command.Filters),
            ReportingMetrics.Catalog,
            clock);
        workspace.Definitions.Add(definition);
        await workspace.SaveAsync(cancellationToken);
        return definition.Id.Value;
    }
}

public sealed record RenameReportDefinitionCommand(Guid DefinitionId, string? Name) : ICommand;

public sealed class RenameReportDefinitionHandler(ReportingWorkspace workspace, IClock clock) : ICommandHandler<RenameReportDefinitionCommand>
{
    public async Task HandleAsync(RenameReportDefinitionCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        (await workspace.LoadDefinitionAsync(command.DefinitionId, cancellationToken)).Rename(command.Name, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Remplace tout le jeu de filtres (liste vide : aucun filtre).</summary>
public sealed record ChangeReportFiltersCommand(Guid DefinitionId, IReadOnlyList<ReportFilterInput>? Filters) : ICommand;

public sealed class ChangeReportFiltersHandler(ReportingWorkspace workspace, IClock clock) : ICommandHandler<ChangeReportFiltersCommand>
{
    public async Task HandleAsync(ChangeReportFiltersCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var filters = ReportFilterInput.ToFilters(command.Filters);
        (await workspace.LoadDefinitionAsync(command.DefinitionId, cancellationToken)).ChangeFilters(filters, ReportingMetrics.Catalog, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>
/// Planifie l'envoi d'un rapport par e-mail. Heure LOCALE (« HH:mm ») dans le fuseau du restaurant ; l'envoi couvre la
/// veille, les 7 derniers jours ou le mois précédent. L'export est fait au nom de la personne qui planifie.
/// </summary>
public sealed record ScheduleReportCommand(
    Guid DefinitionId,
    ReportFrequency Frequency,
    DayOfWeek? DayOfWeek,
    int? DayOfMonth,
    string? LocalTime,
    IReadOnlyList<string>? Recipients) : ICommand;

public sealed class ScheduleReportHandler(ReportingWorkspace workspace, IClock clock) : ICommandHandler<ScheduleReportCommand>
{
    public async Task HandleAsync(ScheduleReportCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var user = workspace.RequireRequester();
        DomainException.ThrowIf(
            !TimeOnly.TryParseExact(command.LocalTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var localTime),
            ReportingApplicationErrors.TimeOfDayInvalid);

        var definition = await workspace.LoadDefinitionAsync(command.DefinitionId, cancellationToken);
        var schedule = ReportSchedule.Create(
            command.Frequency,
            command.DayOfWeek,
            command.DayOfMonth,
            localTime,
            await workspace.TimeZoneAsync(workspace.CurrentTenant, cancellationToken),
            (command.Recipients ?? Array.Empty<string>()).Select(Email.Create));
        definition.ScheduleDelivery(schedule, ReportingMetrics.Catalog, clock);

        var next = ReportOccurrences.NextAfter(schedule, clock.UtcNow);
        if (await workspace.Deliveries.FindAsync(definition.TenantId, definition.Id, cancellationToken) is { } delivery)
        {
            delivery.Reschedule(user, next);
        }
        else
        {
            workspace.Deliveries.Add(ReportDelivery.Create(definition, user, next));
        }

        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record UnscheduleReportCommand(Guid DefinitionId) : ICommand;

public sealed class UnscheduleReportHandler(ReportingWorkspace workspace, IClock clock) : ICommandHandler<UnscheduleReportCommand>
{
    public async Task HandleAsync(UnscheduleReportCommand command, CancellationToken cancellationToken = default)
    {
        await workspace.RequireWritableTenantAsync(cancellationToken);
        var definition = await workspace.LoadDefinitionAsync(command.DefinitionId, cancellationToken);
        definition.Unschedule(clock);
        if (await workspace.Deliveries.FindAsync(definition.TenantId, definition.Id, cancellationToken) is { } delivery)
        {
            workspace.Deliveries.Remove(delivery);
        }

        await workspace.SaveAsync(cancellationToken);
    }
}
