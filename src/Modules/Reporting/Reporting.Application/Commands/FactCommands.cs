using System.Globalization;
using Reporting.Application.Abstractions;
using Reporting.Application.Metrics;
using Reporting.Application.Platform;

namespace Reporting.Application.Commands;

// Alimentation des projections, par l'hôte (réactions aux événements des autres modules, par l'outbox). Aucune route.

/// <summary>Un incrément : une mesure STOCKÉE du catalogue, une valeur (négative pour une reprise), dimension et devise facultatives.</summary>
public sealed record ReportingFactInput(string Metric, decimal Value, string? Dimension = null, string? Currency = null);

/// <summary>
/// Compte les faits d'un événement pour le restaurant du CONTEXTE (jamais un tenant reçu), au jour LOCAL du restaurant
/// où l'événement s'est produit. <paramref name="SourceKey"/> (module et id du message de l'outbox) rend l'opération
/// idempotente : renvoie faux si la source avait déjà été comptée.
/// </summary>
public sealed record RecordReportingFactsCommand(
    string SourceKey, DateTimeOffset OccurredAtUtc, Guid? LocationId, IReadOnlyList<ReportingFactInput> Facts) : ICommand<bool>;

public sealed class RecordReportingFactsHandler(ReportingWorkspace workspace) : ICommandHandler<RecordReportingFactsCommand, bool>
{
    public const int MaxSourceKeyLength = 120;

    public async Task<bool> HandleAsync(RecordReportingFactsCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var tenant = workspace.CurrentTenant;
        DomainException.ThrowIf(
            string.IsNullOrWhiteSpace(command.SourceKey) || command.SourceKey.Trim().Length > MaxSourceKeyLength,
            ReportingApplicationErrors.SourceKeyInvalid);
        DomainException.ThrowIf(command.Facts is not { Count: > 0 }, ReportingApplicationErrors.FactsEmpty);

        var zone = await workspace.TimeZoneAsync(tenant, cancellationToken);
        var local = zone.ToLocal(command.OccurredAtUtc);
        var hour = local.Hour.ToString("00", CultureInfo.InvariantCulture);
        var facts = command.Facts.Select(fact =>
        {
            var definition = ReportingMetrics.Find(fact.Metric);
            DomainException.ThrowIf(definition is null, ReportingApplicationErrors.MetricUnknown);
            DomainException.ThrowIf(definition!.IsDerived, ReportingApplicationErrors.MetricNotStored);
            return new MetricFact(
                fact.Metric,
                ReportFilterRules.NormalizeDimension(definition.Hourly ? hour : fact.Dimension),
                ReportFilterRules.NormalizeCurrency(fact.Currency),
                fact.Value);
        }).ToList();

        var day = DateOnly.FromDateTime(local.DateTime);
        var location = command.LocationId is { } id && id != Guid.Empty ? LocationId.From(id) : (LocationId?)null;
        return await workspace.Metrics.RecordAsync(tenant, command.SourceKey.Trim(), new DailyFacts(location, day, facts), cancellationToken);
    }
}

/// <summary>PLATEFORME : l'état courant d'un restaurant, recopié depuis Tenants (le dernier reçu fait foi).</summary>
public sealed record RecordTenantSnapshotCommand(Guid TenantId, string Name, PlatformTenantStatus Status, DateTimeOffset CreatedAtUtc) : ICommand;

public sealed class RecordTenantSnapshotHandler(IPlatformDirectory directory, IClock clock) : ICommandHandler<RecordTenantSnapshotCommand>
{
    public const int MaxNameLength = 200;

    public Task HandleAsync(RecordTenantSnapshotCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var name = (command.Name ?? string.Empty).Trim();
        return directory.UpsertTenantAsync(
            new PlatformTenantSnapshot(
                TenantId.From(command.TenantId), name[..Math.Min(name.Length, MaxNameLength)], command.Status, command.CreatedAtUtc, clock.UtcNow),
            cancellationToken);
    }
}

/// <summary>
/// PLATEFORME : l'abonnement courant d'un restaurant, recopié depuis Billing. Le prix du plan est ramené au mois
/// (<paramref name="Yearly"/> : divisé par 12, arrondi au centime).
/// </summary>
public sealed record RecordSubscriptionSnapshotCommand(
    Guid TenantId,
    Guid SubscriptionId,
    Guid PlanId,
    string PlanName,
    PlatformSubscriptionStatus Status,
    decimal PlanPrice,
    string CurrencyCode,
    bool Yearly) : ICommand;

public sealed class RecordSubscriptionSnapshotHandler(IPlatformDirectory directory, IClock clock) : ICommandHandler<RecordSubscriptionSnapshotCommand>
{
    public Task HandleAsync(RecordSubscriptionSnapshotCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var currency = ReportFilterRules.NormalizeCurrency(command.CurrencyCode);
        DomainException.ThrowIf(currency.Length == 0, ReportingApplicationErrors.CurrencyInvalid);
        var monthly = command.Yearly ? Math.Round(command.PlanPrice / 12m, 2, MidpointRounding.AwayFromZero) : command.PlanPrice;
        var plan = (command.PlanName ?? string.Empty).Trim();
        return directory.UpsertSubscriptionAsync(
            new PlatformSubscriptionSnapshot(
                TenantId.From(command.TenantId),
                command.SubscriptionId,
                command.PlanId,
                plan[..Math.Min(plan.Length, RecordTenantSnapshotHandler.MaxNameLength)],
                command.Status,
                monthly,
                currency,
                clock.UtcNow),
            cancellationToken);
    }
}
