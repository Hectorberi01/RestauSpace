using Loyalty.Domain;

namespace Loyalty.Application.Abstractions;

// Chaque méthode prend le tenant explicitement : il vient du contexte de la requête ou du message traité (jamais de la
// requête elle-même). Une ressource d'un autre tenant n'est tout simplement pas trouvée.

public interface ILoyaltyAccountRepository
{
    /// <summary>Le compte SANS son registre (jamais réhydraté) : le solde stocké suffit aux règles.</summary>
    Task<LoyaltyAccount?> FindAsync(TenantId tenantId, LoyaltyAccountId id, CancellationToken cancellationToken = default);

    /// <summary>Le compte (unique) du client dans ce restaurant.</summary>
    Task<LoyaltyAccount?> FindByCustomerAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<LoyaltyAccount>> ListAsync(
        TenantId tenantId, LoyaltyAccountStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    void Add(LoyaltyAccount account);
}

public interface IRewardRepository
{
    Task<Reward?> FindAsync(TenantId tenantId, RewardId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par coût puis nom.</summary>
    Task<IReadOnlyList<Reward>> ListAsync(TenantId tenantId, bool activeOnly, CancellationToken cancellationToken = default);

    void Add(Reward reward);
}

/// <summary>Le registre d'un compte, en lecture seule (read model), du plus récent au plus ancien.</summary>
public interface ILoyaltyHistory
{
    Task<IReadOnlyList<LoyaltyTransaction>> ListAsync(
        TenantId tenantId, LoyaltyAccountId accountId, int skip, int take, CancellationToken cancellationToken = default);
}

/// <summary>Le barème du restaurant, gardé par ce module.</summary>
public interface ILoyaltyProgramStore
{
    /// <summary><see cref="LoyaltyProgram.Default"/> (désactivé) tant que le restaurant n'a rien réglé.</summary>
    Task<LoyaltyProgram> GetAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Remplace le réglage ; écrit au prochain <c>SaveChangesAsync</c>.</summary>
    Task SetAsync(TenantId tenantId, LoyaltyProgram program, DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default);
}

/// <summary>Un compte dont le solde peut expirer : sa dernière activité (gain ou échange, sinon ouverture).</summary>
public sealed record ExpiryCandidate(LoyaltyAccountId AccountId, DateTimeOffset LastActivityUtc);

public interface ILoyaltyExpiryCandidates
{
    /// <summary>
    /// Comptes non clos du tenant, de solde positif, sans gain ni échange depuis <paramref name="cutoffUtc"/> (exclu),
    /// les plus anciens d'abord, au plus <paramref name="take"/>.
    /// </summary>
    Task<IReadOnlyList<ExpiryCandidate>> ListAsync(TenantId tenantId, DateTimeOffset cutoffUtc, int take, CancellationToken cancellationToken = default);
}

/// <summary>
/// Pour le job d'expiration SEULEMENT (hors requête, tous tenants) : les restaurants dont le barème fait expirer les
/// points. Ne renvoie que des ids : le traitement se fait ensuite tenant par tenant.
/// </summary>
public interface ILoyaltyProgramDirectory
{
    Task<IReadOnlyList<TenantId>> ListTenantsWithExpiryAsync(CancellationToken cancellationToken = default);
}

/// <summary>Ce que Loyalty doit savoir de son tenant, fourni par l'hôte depuis le module Tenants.</summary>
public interface ILoyaltyTenants
{
    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>Le client d'un compte, fourni par l'hôte depuis le module Customers (tenant du contexte seulement).</summary>
public interface ILoyaltyCustomers
{
    Task<CustomerReference?> FindAsync(TenantId tenantId, CustomerId customerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ce que Loyalty doit savoir d'une commande pour y poser une récompense : son client, sa devise, et si elle accepte
/// encore une remise (brouillon). Fourni par l'hôte depuis le module Orders.
/// </summary>
public sealed record LoyaltyOrder(OrderId OrderId, CustomerId? CustomerId, string CurrencyCode, bool AcceptsDiscounts);

public interface ILoyaltyOrders
{
    Task<LoyaltyOrder?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);
}
