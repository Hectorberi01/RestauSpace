using Loyalty.Application.Abstractions;
using Loyalty.Domain;

namespace Loyalty.Application.Queries;

// Lectures du tenant courant, possibles même quand le tenant est suspendu. Une ressource d'un autre tenant n'est jamais
// trouvée.

public sealed record GetLoyaltyAccountQuery(Guid AccountId) : IQuery<LoyaltyAccountDto>;

public sealed class GetLoyaltyAccountHandler(LoyaltyWorkspace workspace) : IQueryHandler<GetLoyaltyAccountQuery, LoyaltyAccountDto>
{
    public async Task<LoyaltyAccountDto> HandleAsync(GetLoyaltyAccountQuery query, CancellationToken cancellationToken = default) =>
        LoyaltyAccountDto.From(await workspace.LoadAccountAsync(query.AccountId, cancellationToken));
}

/// <summary>Le compte d'un client (404 s'il n'en a pas) : au comptoir, à partir de la fiche client.</summary>
public sealed record GetLoyaltyAccountByCustomerQuery(Guid CustomerId) : IQuery<LoyaltyAccountDto>;

public sealed class GetLoyaltyAccountByCustomerHandler(LoyaltyWorkspace workspace)
    : IQueryHandler<GetLoyaltyAccountByCustomerQuery, LoyaltyAccountDto>
{
    public async Task<LoyaltyAccountDto> HandleAsync(GetLoyaltyAccountByCustomerQuery query, CancellationToken cancellationToken = default)
    {
        var account = query.CustomerId == Guid.Empty
            ? null
            : await workspace.Accounts.FindByCustomerAsync(workspace.CurrentTenant, CustomerId.From(query.CustomerId), cancellationToken);
        return LoyaltyAccountDto.From(account ?? throw new NotFoundException(LoyaltyApplicationErrors.AccountNotFound));
    }
}

/// <summary>Comptes du restaurant, du plus récent au plus ancien, filtrés par statut au besoin.</summary>
public sealed record ListLoyaltyAccountsQuery(LoyaltyAccountStatus? Status = null, int Skip = 0, int Take = 50)
    : IQuery<IReadOnlyList<LoyaltyAccountDto>>;

public sealed class ListLoyaltyAccountsHandler(LoyaltyWorkspace workspace) : IQueryHandler<ListLoyaltyAccountsQuery, IReadOnlyList<LoyaltyAccountDto>>
{
    public async Task<IReadOnlyList<LoyaltyAccountDto>> HandleAsync(ListLoyaltyAccountsQuery query, CancellationToken cancellationToken = default)
    {
        LoyaltyWorkspace.EnsurePage(query.Skip, query.Take);
        return (await workspace.Accounts.ListAsync(workspace.CurrentTenant, query.Status, query.Skip, query.Take, cancellationToken))
            .Select(LoyaltyAccountDto.From)
            .ToList();
    }
}

/// <summary>L'historique (registre) d'un compte, du plus récent au plus ancien.</summary>
public sealed record ListLoyaltyTransactionsQuery(Guid AccountId, int Skip = 0, int Take = 50) : IQuery<IReadOnlyList<LoyaltyTransactionDto>>;

public sealed class ListLoyaltyTransactionsHandler(LoyaltyWorkspace workspace, ILoyaltyHistory history)
    : IQueryHandler<ListLoyaltyTransactionsQuery, IReadOnlyList<LoyaltyTransactionDto>>
{
    public async Task<IReadOnlyList<LoyaltyTransactionDto>> HandleAsync(ListLoyaltyTransactionsQuery query, CancellationToken cancellationToken = default)
    {
        LoyaltyWorkspace.EnsurePage(query.Skip, query.Take);
        var account = await workspace.LoadAccountAsync(query.AccountId, cancellationToken);
        return (await history.ListAsync(account.TenantId, account.Id, query.Skip, query.Take, cancellationToken))
            .Select(LoyaltyTransactionDto.From)
            .ToList();
    }
}

public sealed record ListRewardsQuery(bool ActiveOnly = false) : IQuery<IReadOnlyList<RewardDto>>;

public sealed class ListRewardsHandler(LoyaltyWorkspace workspace) : IQueryHandler<ListRewardsQuery, IReadOnlyList<RewardDto>>
{
    public async Task<IReadOnlyList<RewardDto>> HandleAsync(ListRewardsQuery query, CancellationToken cancellationToken = default) =>
        (await workspace.Rewards.ListAsync(workspace.CurrentTenant, query.ActiveOnly, cancellationToken)).Select(RewardDto.From).ToList();
}

public sealed record GetRewardQuery(Guid RewardId) : IQuery<RewardDto>;

public sealed class GetRewardHandler(LoyaltyWorkspace workspace) : IQueryHandler<GetRewardQuery, RewardDto>
{
    public async Task<RewardDto> HandleAsync(GetRewardQuery query, CancellationToken cancellationToken = default) =>
        RewardDto.From(await workspace.LoadRewardAsync(query.RewardId, cancellationToken));
}

public sealed record GetLoyaltyProgramQuery : IQuery<LoyaltyProgramDto>;

public sealed class GetLoyaltyProgramHandler(LoyaltyWorkspace workspace) : IQueryHandler<GetLoyaltyProgramQuery, LoyaltyProgramDto>
{
    public async Task<LoyaltyProgramDto> HandleAsync(GetLoyaltyProgramQuery query, CancellationToken cancellationToken = default) =>
        LoyaltyProgramDto.From(await workspace.Program.GetAsync(workspace.CurrentTenant, cancellationToken));
}
