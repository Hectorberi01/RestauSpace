using Loyalty.Application.Abstractions;
using Loyalty.Domain;

namespace Loyalty.Application;

/// <summary>
/// Étapes communes aux cas d'usage : le tenant vient du contexte (requête, ou message traité en arrière-plan), une
/// écriture exige un tenant qui les accepte, chaque agrégat est chargé dans ce tenant seulement (404 sinon), puis on
/// sauvegarde. Aucune règle métier ici : elles sont dans le domaine.
/// </summary>
public sealed class LoyaltyWorkspace(
    ITenantContext tenantContext,
    ILoyaltyTenants tenants,
    ILoyaltyAccountRepository accounts,
    IRewardRepository rewards,
    ILoyaltyLedgerLookup ledger,
    ILoyaltyAccountUniquenessChecker uniqueness,
    ILoyaltyProgramStore program,
    ILoyaltyUnitOfWork unitOfWork)
{
    public const int MaxTake = 100;

    public ILoyaltyAccountRepository Accounts => accounts;

    public IRewardRepository Rewards => rewards;

    public ILoyaltyLedgerLookup Ledger => ledger;

    public ILoyaltyAccountUniquenessChecker Uniqueness => uniqueness;

    public ILoyaltyProgramStore Program => program;

    public TenantId CurrentTenant => tenantContext.RequireTenant();

    public Task<bool> AcceptsWritesAsync(CancellationToken cancellationToken) =>
        tenants.AcceptsBusinessWritesAsync(CurrentTenant, cancellationToken);

    public async Task RequireWritableTenantAsync(CancellationToken cancellationToken)
    {
        if (!await AcceptsWritesAsync(cancellationToken))
        {
            throw new DomainException(LoyaltyApplicationErrors.TenantNotWritable);
        }
    }

    public async Task<LoyaltyAccount> LoadAccountAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await accounts.FindAsync(CurrentTenant, LoyaltyAccountId.From(id), cancellationToken))
        ?? throw new NotFoundException(LoyaltyApplicationErrors.AccountNotFound);

    public async Task<Reward> LoadRewardAsync(Guid id, CancellationToken cancellationToken) =>
        (id == Guid.Empty ? null : await rewards.FindAsync(CurrentTenant, RewardId.From(id), cancellationToken))
        ?? throw new NotFoundException(LoyaltyApplicationErrors.RewardNotFound);

    /// <summary>Charge le compte du tenant courant (tenant qui accepte les écritures), applique <paramref name="change"/> et sauvegarde.</summary>
    public async Task EditAccountAsync(Guid id, Func<LoyaltyAccount, Task> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await RequireWritableTenantAsync(cancellationToken);
        await change(await LoadAccountAsync(id, cancellationToken));
        await SaveAsync(cancellationToken);
    }

    /// <inheritdoc cref="EditAccountAsync(Guid, Func{LoyaltyAccount, Task}, CancellationToken)"/>
    public Task EditAccountAsync(Guid id, Action<LoyaltyAccount> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        return EditAccountAsync(id, account =>
        {
            change(account);
            return Task.CompletedTask;
        }, cancellationToken);
    }

    /// <summary>Charge la récompense du tenant courant (tenant qui accepte les écritures), applique <paramref name="change"/> et sauvegarde.</summary>
    public async Task EditRewardAsync(Guid id, Action<Reward> change, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        await RequireWritableTenantAsync(cancellationToken);
        change(await LoadRewardAsync(id, cancellationToken));
        await SaveAsync(cancellationToken);
    }

    public Task SaveAsync(CancellationToken cancellationToken) => unitOfWork.SaveChangesAsync(cancellationToken);

    public static void EnsurePage(int skip, int take) =>
        DomainException.ThrowIf(skip < 0 || take is < 1 or > MaxTake, LoyaltyApplicationErrors.PageInvalid);

    /// <summary>La référence de registre d'une commande : son id, de sorte qu'un gain et un échange se retrouvent et s'annulent.</summary>
    public static TransactionReference OrderReference(Guid orderId) => TransactionReference.Create(orderId.ToString());

    /// <summary>Un texte facultatif (motif) : vide = absent.</summary>
    public static TransactionReference? OptionalReference(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : TransactionReference.Create(value);
}
