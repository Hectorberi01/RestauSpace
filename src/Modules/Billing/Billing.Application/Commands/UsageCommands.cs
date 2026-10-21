using Billing.Domain;

namespace Billing.Application.Commands;

/// <summary>
/// Consomme <paramref name="Amount"/> unités d'une fonctionnalité de CONSOMMATION pour le restaurant du contexte (appelé
/// par l'hôte au nom d'un autre module). Le compteur est ouvert à la première consommation et passe seul au mois suivant.
/// <paramref name="Blocking"/> : refuser au-delà du droit (<c>quota_exceeded</c>, <c>feature_disabled</c>) ; sinon
/// seulement mesurer. Renvoie la consommation du mois après l'opération.
/// </summary>
public sealed record ConsumeFeatureCommand(string FeatureCode, long Amount = 1, bool Blocking = true) : ICommand<long>;

public sealed class ConsumeFeatureHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<ConsumeFeatureCommand, long>
{
    public async Task<long> HandleAsync(ConsumeFeatureCommand command, CancellationToken cancellationToken = default)
    {
        var feature = Domain.FeatureCode.Create(command.FeatureCode);
        var tenant = workspace.CurrentTenant;
        var entitlement = await workspace.EntitlementAsync(tenant, feature, cancellationToken);
        var period = BillingFeatures.ConsumptionPeriod(clock.UtcNow);

        var counter = await workspace.Counters.FindAsync(tenant, feature, cancellationToken);
        if (counter is null)
        {
            counter = UsageCounter.Open(tenant, feature, period, await workspace.CounterUniqueness.IsUniqueAsync(tenant, feature, cancellationToken));
            workspace.Counters.Add(counter);
        }
        else if (counter.Period.EndUtc <= period.StartUtc)
        {
            // Mois terminé : le compteur passe au mois courant (un mois sans consommation est simplement sauté).
            counter.ResetForNewPeriod(period, clock);
        }

        counter.Increment(command.Amount, entitlement, command.Blocking, clock);
        await workspace.SaveAsync(cancellationToken);
        return counter.Used;
    }
}
