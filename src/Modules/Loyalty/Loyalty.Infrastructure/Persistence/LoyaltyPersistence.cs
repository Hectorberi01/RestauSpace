using Loyalty.Application.Abstractions;
using Loyalty.Domain;

namespace Loyalty.Infrastructure.Persistence;

// Chaque requête filtre sur le tenant reçu, en plus du query filter du contexte : deux verrous sur la même porte.
// Un compte est chargé SANS son registre (pas d'Include) ; les lectures du registre se font sans suivi, pour qu'aucune
// écriture stockée ne se greffe à la collection d'un compte chargé.

internal sealed class LoyaltyAccountRepository(LoyaltyDbContext context) : ILoyaltyAccountRepository
{
    public Task<LoyaltyAccount?> FindAsync(TenantId tenantId, LoyaltyAccountId id, CancellationToken cancellationToken = default) =>
        context.Accounts.SingleOrDefaultAsync(account => account.TenantId == tenantId && account.Id == id, cancellationToken);

    public async Task<LoyaltyAccount?> FindByCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default) =>
        // Un compte ouvert dans cette unité de travail (inscription à la première commande) compte déjà.
        context.Accounts.Local.SingleOrDefault(account => account.TenantId == tenantId && account.CustomerId == customerId)
        ?? await context.Accounts.SingleOrDefaultAsync(
            account => account.TenantId == tenantId && account.CustomerId == customerId, cancellationToken);

    public async Task<IReadOnlyList<LoyaltyAccount>> ListAsync(
        TenantId tenantId, LoyaltyAccountStatus? status, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.Accounts.AsNoTracking().Where(account => account.TenantId == tenantId);
        if (status is { } wanted)
        {
            query = query.Where(account => account.Status == wanted);
        }

        return await query
            .OrderByDescending(account => account.OpenedAtUtc)
            .ThenBy(account => account.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public void Add(LoyaltyAccount account) => context.Accounts.Add(account);
}

internal sealed class RewardRepository(LoyaltyDbContext context) : IRewardRepository
{
    public Task<Reward?> FindAsync(TenantId tenantId, RewardId id, CancellationToken cancellationToken = default) =>
        context.Rewards.SingleOrDefaultAsync(reward => reward.TenantId == tenantId && reward.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Reward>> ListAsync(TenantId tenantId, bool activeOnly, CancellationToken cancellationToken = default)
    {
        var query = context.Rewards.AsNoTracking().Where(reward => reward.TenantId == tenantId);
        if (activeOnly)
        {
            query = query.Where(reward => reward.Status == RewardStatus.Active);
        }

        // Tri en mémoire par coût : le coût est un objet valeur converti, le catalogue d'un restaurant reste petit.
        return (await query.ToListAsync(cancellationToken))
            .OrderBy(reward => reward.PointCost.Value)
            .ThenBy(reward => reward.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(reward => reward.Id.Value)
            .ToList();
    }

    public void Add(Reward reward) => context.Rewards.Add(reward);
}

internal sealed class StoredValueCardRepository(LoyaltyDbContext context) : IStoredValueCardRepository
{
    public Task<StoredValueCard?> FindAsync(TenantId tenantId, StoredValueCardId id, CancellationToken cancellationToken = default) =>
        context.StoredValueCards.SingleOrDefaultAsync(card => card.TenantId == tenantId && card.Id == id, cancellationToken);

    public Task<StoredValueCard?> FindByCodeHashAsync(TenantId tenantId, string codeHash, CancellationToken cancellationToken = default) =>
        context.StoredValueCards.SingleOrDefaultAsync(card => card.TenantId == tenantId && card.CodeHash == codeHash, cancellationToken);

    public async Task<IReadOnlyList<StoredValueCard>> ListAsync(
        TenantId tenantId, StoredValueKind? kind, CustomerId? customerId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = context.StoredValueCards.AsNoTracking().Where(card => card.TenantId == tenantId);
        if (kind is { } wanted)
        {
            query = query.Where(card => card.Kind == wanted);
        }

        if (customerId is { } customer)
        {
            query = query.Where(card => card.CustomerId == customer);
        }

        return await query
            .OrderByDescending(card => card.IssuedAtUtc)
            .ThenBy(card => card.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public void Add(StoredValueCard card) => context.StoredValueCards.Add(card);
}

internal sealed class LoyaltyHistory(LoyaltyDbContext context) : ILoyaltyHistory
{
    public async Task<IReadOnlyList<LoyaltyTransaction>> ListAsync(
        TenantId tenantId, LoyaltyAccountId accountId, int skip, int take, CancellationToken cancellationToken = default) =>
        await context.Transactions
            .AsNoTracking()
            .Where(entry => entry.TenantId == tenantId && entry.AccountId == accountId)
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
}

internal sealed class LoyaltyProgramStore(LoyaltyDbContext context) : ILoyaltyProgramStore
{
    public async Task<LoyaltyProgram> GetAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        (await FindAsync(tenantId, cancellationToken))?.ToProgram() ?? LoyaltyProgram.Default;

    public async Task SetAsync(TenantId tenantId, LoyaltyProgram program, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(program);
        var settings = await FindAsync(tenantId, cancellationToken);
        if (settings is null)
        {
            settings = new LoyaltyProgramSettings
            {
                TenantId = tenantId,
                IsEnabled = program.IsEnabled,
                PointsPerCurrencyUnit = program.PointsPerCurrencyUnit,
                AutoEnroll = program.AutoEnroll,
                InactivityExpiryDays = program.InactivityExpiryDays,
                UpdatedAtUtc = updatedAtUtc,
            };
            context.Programs.Add(settings);
            return;
        }

        _ = settings.Apply(program, updatedAtUtc);
    }

    // Suivi : la même ligne sert à lire puis à modifier dans la requête ; une ligne ajoutée et pas encore écrite compte.
    private async Task<LoyaltyProgramSettings?> FindAsync(TenantId tenantId, CancellationToken cancellationToken) =>
        context.Programs.Local.SingleOrDefault(settings => settings.TenantId == tenantId)
        ?? await context.Programs.SingleOrDefaultAsync(settings => settings.TenantId == tenantId, cancellationToken);
}

internal sealed class LoyaltyExpiryCandidates(LoyaltyDbContext context) : ILoyaltyExpiryCandidates
{
    public async Task<IReadOnlyList<ExpiryCandidate>> ListAsync(
        TenantId tenantId, DateTimeOffset cutoffUtc, int take, CancellationToken cancellationToken = default)
    {
        // Dernière activité = dernier gain ou échange du client ; à défaut, l'ouverture du compte.
        var rows = await context.Accounts
            .AsNoTracking()
            .Where(account => account.TenantId == tenantId && account.Status != LoyaltyAccountStatus.Closed && account.Balance > 0)
            .Select(account => new
            {
                account.Id,
                LastActivity = context.Transactions
                    .Where(entry => entry.AccountId == account.Id
                        && (entry.Type == LoyaltyTransactionType.Earn || entry.Type == LoyaltyTransactionType.Redeem))
                    .Max(entry => (DateTimeOffset?)entry.OccurredAtUtc) ?? account.OpenedAtUtc,
            })
            .Where(row => row.LastActivity < cutoffUtc)
            .OrderBy(row => row.LastActivity)
            .ThenBy(row => row.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        return rows.Select(row => new ExpiryCandidate(row.Id, row.LastActivity)).ToList();
    }
}

/// <summary>Tous tenants (job hors requête) : seuls des ids de tenant sortent d'ici.</summary>
internal sealed class LoyaltyProgramDirectory(LoyaltyDbContext context) : ILoyaltyProgramDirectory
{
    public async Task<IReadOnlyList<TenantId>> ListTenantsWithExpiryAsync(CancellationToken cancellationToken = default) =>
        await context.Programs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(settings => settings.InactivityExpiryDays != null)
            .Select(settings => settings.TenantId)
            .ToListAsync(cancellationToken);
}
