using Billing.Domain;

namespace Billing.Application.Abstractions;

/// <summary>Le catalogue des plans : GLOBAL à la plateforme, sans tenant.</summary>
public interface IPlanRepository
{
    Task<Plan?> FindAsync(PlanId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par nom.</summary>
    Task<IReadOnlyList<Plan>> ListAsync(bool activeOnly, CancellationToken cancellationToken = default);

    void Add(Plan plan);
}

/// <summary>Les abonnements d'un restaurant : le tenant est toujours donné explicitement (contexte de la requête ou du job).</summary>
public interface ISubscriptionRepository
{
    /// <summary>L'abonnement non résilié du tenant (au plus un).</summary>
    Task<SaasSubscription?> FindLiveAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    Task<SaasSubscription?> FindAsync(TenantId tenantId, SubscriptionId id, CancellationToken cancellationToken = default);

    /// <summary>Le tenant a-t-il un autre abonnement que <paramref name="except"/>, résiliés compris ?</summary>
    Task<bool> HasOtherAsync(TenantId tenantId, SubscriptionId? except, CancellationToken cancellationToken = default);

    void Add(SaasSubscription subscription);
}

/// <summary>Un abonnement dont la période courante est arrivée à échéance (ou l'impayé au bout du délai de grâce).</summary>
/// <summary>
/// Les options souscrites. Le tenant est toujours passé explicitement : celui du contexte côté restaurant, celui désigné
/// par l'administrateur côté plateforme.
/// </summary>
public interface ITenantAddOnRepository
{
    Task<TenantAddOn?> FindAsync(TenantId tenantId, TenantAddOnId id, CancellationToken cancellationToken = default);

    Task<TenantAddOn?> FindActiveAsync(TenantId tenantId, PlanId planId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantAddOn>> ListAsync(TenantId tenantId, bool activeOnly, CancellationToken cancellationToken = default);

    /// <summary>
    /// Plateforme SEULEMENT : les options actives de tous les restaurants, des plus récentes aux plus anciennes.
    /// Lecture seule. Jamais utilisé par une route d'un restaurant.
    /// </summary>
    Task<IReadOnlyList<TenantAddOn>> ListActiveForPlatformAsync(int skip, int take, CancellationToken cancellationToken = default);

    void Add(TenantAddOn addOn);
}

public sealed record DueSubscription(TenantId TenantId, SubscriptionId SubscriptionId);

/// <summary>
/// Pour le back-office de la plateforme et le job d'échéance SEULEMENT : tous les tenants. Jamais utilisé par une route
/// d'un restaurant.
/// </summary>
public interface IPlatformSubscriptions
{
    Task<SaasSubscription?> FindAsync(SubscriptionId id, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<SaasSubscription>> ListAsync(
        TenantId? tenantId, SubscriptionStatus? status, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Abonnements non résiliés dont la période est terminée à <paramref name="nowUtc"/> et qui appellent une décision :
    /// résiliation programmée, essai fini, période payée échue, impayé au-delà du délai de grâce.
    /// </summary>
    Task<IReadOnlyList<DueSubscription>> ListDueAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Essais et offres limitées qui se terminent dans <paramref name="window"/> et dont le restaurant n'a pas encore été
    /// prévenu pour cette échéance.
    /// </summary>
    Task<IReadOnlyList<DueSubscription>> ListEndingAsync(DateTimeOffset nowUtc, TimeSpan window, int take, CancellationToken cancellationToken = default);
}

/// <summary>L'historique des paiements d'abonnement. Lectures seules, du plus récent au plus ancien.</summary>
public interface ISubscriptionPaymentRepository
{
    void Add(SubscriptionPayment payment);

    Task<SubscriptionPayment?> FindAsync(TenantId tenantId, SubscriptionPaymentId id, CancellationToken cancellationToken = default);

    /// <summary>Les paiements d'un restaurant (le tenant est toujours donné).</summary>
    Task<IReadOnlyList<SubscriptionPayment>> ListAsync(TenantId tenantId, int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Plateforme SEULEMENT : tous les tenants, filtrés au besoin. <paramref name="fromUtc"/> inclus,
    /// <paramref name="toUtc"/> exclu, sur la date d'enregistrement.
    /// </summary>
    Task<IReadOnlyList<SubscriptionPayment>> ListAllAsync(
        TenantId? tenantId, SubscriptionId? subscriptionId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int skip, int take,
        CancellationToken cancellationToken = default);
}

public interface IUsageCounterRepository
{
    Task<UsageCounter?> FindAsync(TenantId tenantId, FeatureCode featureCode, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule.</summary>
    Task<IReadOnlyList<UsageCounter>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    void Add(UsageCounter counter);
}

/// <summary>Ce que Billing doit savoir d'un tenant, fourni par l'hôte depuis le module Tenants.</summary>
public interface IBillingTenants
{
    /// <summary>Le tenant existe (quel que soit son statut).</summary>
    Task<bool> ExistsAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>La devise par défaut du tenant (celle de ses plans) ; null pour un tenant inconnu.</summary>
    Task<Currency?> DefaultCurrencyAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Le nom du restaurant (client des factures de la plateforme) ; null s'il est inconnu.</summary>
    Task<string?> NameAsync(TenantId tenantId, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
}

/// <summary>Les règlements en ligne des abonnements. Le tenant est toujours donné, sauf pour le job (tous les tenants).</summary>
public interface ISubscriptionCheckoutRepository
{
    Task<SubscriptionCheckout?> FindAsync(TenantId tenantId, SubscriptionCheckoutId id, CancellationToken cancellationToken = default);

    /// <summary>Le dernier règlement en attente du tenant, s'il y en a un.</summary>
    Task<SubscriptionCheckout?> FindPendingAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    /// <summary>Job SEULEMENT : les règlements en attente de tous les tenants, les plus anciens d'abord.</summary>
    Task<IReadOnlyList<(TenantId TenantId, SubscriptionCheckoutId Id)>> ListPendingAsync(int take, CancellationToken cancellationToken = default);

    void Add(SubscriptionCheckout checkout);
}

/// <summary>Le prestataire choisi par la plateforme pour chaque devise (back-office et paiement en ligne).</summary>
public interface IOnlinePaymentRouteRepository
{
    Task<OnlinePaymentRoute?> FindAsync(string currencyCode, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par devise.</summary>
    Task<IReadOnlyList<OnlinePaymentRoute>> ListAsync(CancellationToken cancellationToken = default);

    void Add(OnlinePaymentRoute route);

    void Remove(OnlinePaymentRoute route);
}

/// <summary>Un encaissement d'abonnement, pour les indicateurs de la plateforme.</summary>
public sealed record OverviewPayment(Guid TenantId, Guid SubscriptionId, decimal Amount, string CurrencyCode, DateTimeOffset RecordedAtUtc);

/// <summary>Un règlement en ligne refusé ou expiré, pour les indicateurs de la plateforme.</summary>
public sealed record OverviewFailedCheckout(Guid TenantId, decimal Amount, string CurrencyCode, string Provider, SubscriptionCheckoutStatus Status, DateTimeOffset AtUtc);

/// <summary>
/// Plateforme SEULEMENT (tableau de bord du back-office) : lectures tous tenants, sans suivi. Jamais utilisé par une
/// route d'un restaurant.
/// </summary>
public interface IBillingOverviewReader
{
    /// <summary>Les abonnements non résiliés.</summary>
    Task<IReadOnlyList<SaasSubscription>> ListLiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Les abonnements démarrés depuis cette date (résiliés compris).</summary>
    Task<IReadOnlyList<SaasSubscription>> ListStartedSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);

    Task<int> CountCancelledSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OverviewPayment>> ListPaymentsSinceAsync(DateTimeOffset sinceUtc, CancellationToken cancellationToken = default);

    /// <summary>Les règlements refusés ou expirés depuis cette date, les plus récents d'abord.</summary>
    Task<IReadOnlyList<OverviewFailedCheckout>> ListFailedCheckoutsSinceAsync(DateTimeOffset sinceUtc, int take, CancellationToken cancellationToken = default);

    /// <summary>Les options actives de tous les restaurants, avec leur plan.</summary>
    Task<IReadOnlyList<(TenantAddOn AddOn, Plan Plan)>> ListActiveAddOnsAsync(CancellationToken cancellationToken = default);
}

/// <param name="Missing">Codes stables : disabled, currency, integration, credentials, credentials_unreadable.</param>
/// <param name="Methods">Les moyens de paiement du prestataire (Card, MobileMoney, PayPal…).</param>
public sealed record CollectorCandidate(string Provider, string Name, bool Ready, IReadOnlyList<string> Missing, IReadOnlyList<string>? Methods = null);

public enum CollectionState
{
    Pending = 1,
    Paid = 2,
    Failed = 3,
}

public sealed record CollectionStart(string Provider, string Reference, Uri? CheckoutUrl);

/// <param name="Amount">Montant encaissé selon le prestataire, si connu.</param>
public sealed record CollectionStatus(CollectionState State, decimal? Amount);

/// <summary>
/// L'encaissement en ligne des abonnements sur le compte marchand de la PLATEFORME (fourni par l'hôte depuis Payments).
/// Le prestataire fait foi : l'état est relu chez lui.
/// </summary>
public interface ISubscriptionCollector
{
    /// <summary>Le prestataire du réglage de l'hôte, pour une devise sans choix de la plateforme ; null si aucun.</summary>
    string? DefaultProvider { get; }

    /// <summary>Le prestataire peut encaisser dans cette devise : inscrit, actif, en ligne, identifiants de la plateforme rangés.</summary>
    Task<bool> IsReadyAsync(string provider, Currency currency, CancellationToken cancellationToken = default);

    /// <summary>Les prestataires en ligne et ce qui manque à chacun pour encaisser dans cette devise.</summary>
    Task<IReadOnlyList<CollectorCandidate>> CandidatesAsync(Currency currency, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CollectorCandidate>>([]);

    /// <param name="payerPhone">Numéro du payeur pour un paiement poussé sur son téléphone. Jamais conservé.</param>
    Task<CollectionStart> StartAsync(
        string provider, TenantId tenantId, SubscriptionCheckoutId checkoutId, Money amount, string label, string? payerPhone,
        CancellationToken cancellationToken = default);

    Task<CollectionStatus> StatusAsync(string provider, string reference, CancellationToken cancellationToken = default);
}

/// <summary>Ce que la plateforme écrit sur ses factures (réglages de l'hôte).</summary>
public sealed record InvoiceIssuer(string Name, string? Address, string? TaxId, string? RegistrationNumber, string? Email, string? Footer);

public interface IInvoiceIssuer
{
    InvoiceIssuer Current { get; }
}
