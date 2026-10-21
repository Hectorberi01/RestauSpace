using Loyalty.Domain;

namespace Loyalty.Application.Commands;

// Le catalogue de récompenses du restaurant et son barème. Une récompense n'est jamais supprimée : on la désactive.

/// <summary>Crée une récompense active : son coût en points et la remise qu'elle accorde (dans la devise des commandes). Renvoie l'id.</summary>
public sealed record CreateRewardCommand(string? Name, int PointCost, decimal DiscountAmount, string? CurrencyCode) : ICommand<Guid>;

public sealed class CreateRewardHandler(LoyaltyWorkspace workspace, IClock clock) : ICommandHandler<CreateRewardCommand, Guid>
{
    public async Task<Guid> HandleAsync(CreateRewardCommand command, CancellationToken cancellationToken = default)
    {
        var cost = Points.Of(command.PointCost);
        var value = Money.Of(command.DiscountAmount, Currency.FromCode(command.CurrencyCode));
        await workspace.RequireWritableTenantAsync(cancellationToken);

        var reward = Reward.Create(workspace.CurrentTenant, command.Name ?? string.Empty, cost, value, clock);
        workspace.Rewards.Add(reward);
        await workspace.SaveAsync(cancellationToken);
        return reward.Id.Value;
    }
}

/// <summary>Nouveau coût : les échanges passés gardent les points débités à l'époque.</summary>
public sealed record ChangeRewardCostCommand(Guid RewardId, int PointCost) : ICommand;

public sealed class ChangeRewardCostHandler(LoyaltyWorkspace workspace, IClock clock) : ICommandHandler<ChangeRewardCostCommand>
{
    public Task HandleAsync(ChangeRewardCostCommand command, CancellationToken cancellationToken = default)
    {
        var cost = Points.Of(command.PointCost);
        return workspace.EditRewardAsync(command.RewardId, reward => reward.ChangeCost(cost, clock), cancellationToken);
    }
}

public sealed record ChangeRewardDiscountValueCommand(Guid RewardId, decimal DiscountAmount, string? CurrencyCode) : ICommand;

public sealed class ChangeRewardDiscountValueHandler(LoyaltyWorkspace workspace, IClock clock) : ICommandHandler<ChangeRewardDiscountValueCommand>
{
    public Task HandleAsync(ChangeRewardDiscountValueCommand command, CancellationToken cancellationToken = default)
    {
        var value = Money.Of(command.DiscountAmount, Currency.FromCode(command.CurrencyCode));
        return workspace.EditRewardAsync(command.RewardId, reward => reward.ChangeDiscountValue(value, clock), cancellationToken);
    }
}

public sealed record ActivateRewardCommand(Guid RewardId) : ICommand;

public sealed class ActivateRewardHandler(LoyaltyWorkspace workspace, IClock clock) : ICommandHandler<ActivateRewardCommand>
{
    public Task HandleAsync(ActivateRewardCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditRewardAsync(command.RewardId, reward => reward.Activate(clock), cancellationToken);
}

public sealed record DeactivateRewardCommand(Guid RewardId) : ICommand;

public sealed class DeactivateRewardHandler(LoyaltyWorkspace workspace, IClock clock) : ICommandHandler<DeactivateRewardCommand>
{
    public Task HandleAsync(DeactivateRewardCommand command, CancellationToken cancellationToken = default) =>
        workspace.EditRewardAsync(command.RewardId, reward => reward.Deactivate(clock), cancellationToken);
}

/// <summary>Remplace le barème du restaurant (voir <see cref="LoyaltyProgram"/>). Vaut pour les commandes terminées ensuite.</summary>
public sealed record SetLoyaltyProgramCommand(bool IsEnabled, decimal PointsPerCurrencyUnit, bool AutoEnroll, int? InactivityExpiryDays) : ICommand;

public sealed class SetLoyaltyProgramHandler(LoyaltyWorkspace workspace, IClock clock) : ICommandHandler<SetLoyaltyProgramCommand>
{
    public async Task HandleAsync(SetLoyaltyProgramCommand command, CancellationToken cancellationToken = default)
    {
        var program = LoyaltyProgram.Create(command.IsEnabled, command.PointsPerCurrencyUnit, command.AutoEnroll, command.InactivityExpiryDays);
        await workspace.RequireWritableTenantAsync(cancellationToken);
        await workspace.Program.SetAsync(workspace.CurrentTenant, program, clock.UtcNow, cancellationToken);
        await workspace.SaveAsync(cancellationToken);
    }
}
