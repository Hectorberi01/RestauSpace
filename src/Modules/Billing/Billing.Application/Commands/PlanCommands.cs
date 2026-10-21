using Billing.Domain;

namespace Billing.Application.Commands;

// Le catalogue des plans : PLATEFORME seulement (routes d'administration). Un plan n'est jamais supprimé : il est
// archivé, et les abonnements existants le gardent.

/// <summary>Ce qu'un plan accorde pour une fonctionnalité : activé ou non, limite facultative (null = illimité).</summary>
public sealed record EntitlementInput(string? FeatureCode, bool Enabled, long? Limit = null)
{
    public Entitlement ToEntitlement() => Entitlement.Create(Domain.FeatureCode.Create(FeatureCode), Enabled, Limit);
}

/// <summary>Crée un plan actif (prix >= 0, gratuit permis). Renvoie l'id.</summary>
public sealed record CreatePlanCommand(
    string? Name,
    decimal Price,
    string? CurrencyCode,
    BillingInterval BillingInterval,
    IReadOnlyList<EntitlementInput>? Entitlements = null,
    PlanKind Kind = PlanKind.Base)
    : ICommand<Guid>;

public sealed class CreatePlanHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<CreatePlanCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreatePlanCommand command, CancellationToken cancellationToken = default)
    {
        var plan = Plan.Create(
            command.Name ?? string.Empty,
            Money.Of(command.Price, Currency.FromCode(command.CurrencyCode)),
            command.BillingInterval,
            (command.Entitlements ?? Array.Empty<EntitlementInput>()).Select(input => input.ToEntitlement()),
            clock,
            command.Kind);
        workspace.Plans.Add(plan);
        await workspace.SaveAsync(cancellationToken);
        return plan.Id.Value;
    }
}

/// <summary>Nouveau prix catalogue, dans la devise du plan : il vaut pour les périodes facturées ensuite.</summary>
public sealed record ChangePlanPriceCommand(Guid PlanId, decimal Price) : ICommand;

public sealed class ChangePlanPriceHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<ChangePlanPriceCommand>
{
    public async Task HandleAsync(ChangePlanPriceCommand command, CancellationToken cancellationToken = default)
    {
        var plan = await workspace.LoadPlanAsync(command.PlanId, cancellationToken);
        plan.ChangePriceForFuture(Money.Of(command.Price, plan.Price.Currency), clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

/// <summary>Ajoute ou remplace le droit du plan pour une fonctionnalité (pour la retirer : <c>Enabled = false</c>).</summary>
public sealed record SetPlanEntitlementCommand(Guid PlanId, EntitlementInput Entitlement) : ICommand;

public sealed class SetPlanEntitlementHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<SetPlanEntitlementCommand>
{
    public async Task HandleAsync(SetPlanEntitlementCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command.Entitlement);
        var entitlement = command.Entitlement.ToEntitlement();
        var plan = await workspace.LoadPlanAsync(command.PlanId, cancellationToken);
        plan.SetEntitlement(entitlement, clock);
        await workspace.SaveAsync(cancellationToken);
    }
}

public sealed record ArchivePlanCommand(Guid PlanId) : ICommand;

public sealed class ArchivePlanHandler(BillingWorkspace workspace, IClock clock) : ICommandHandler<ArchivePlanCommand>
{
    public async Task HandleAsync(ArchivePlanCommand command, CancellationToken cancellationToken = default)
    {
        (await workspace.LoadPlanAsync(command.PlanId, cancellationToken)).Archive(clock);
        await workspace.SaveAsync(cancellationToken);
    }
}
