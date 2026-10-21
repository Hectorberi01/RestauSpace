using Payments.Domain;

namespace Payments.Application.Abstractions;

// Chaque méthode prend le tenant explicitement : il vient du contexte de la requête ou du message traité (jamais de la
// requête elle-même). Une ressource d'un autre tenant n'est tout simplement pas trouvée.

public interface IPaymentRepository
{
    Task<Payment?> FindAsync(TenantId tenantId, PaymentId id, CancellationToken cancellationToken = default);

    /// <summary>Le paiement dont une tentative porte cette référence chez ce prestataire (rapprochement des webhooks).</summary>
    Task<Payment?> FindByProviderReferenceAsync(
        TenantId tenantId, PaymentProviderCode provider, ProviderReference reference, CancellationToken cancellationToken = default);

    /// <summary>Paiements d'une commande, les plus anciens d'abord.</summary>
    Task<IReadOnlyList<Payment>> ListByOrderAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);

    /// <summary>Acomptes d'une réservation (déduits d'une addition compris), les plus anciens d'abord.</summary>
    Task<IReadOnlyList<Payment>> ListByReservationAsync(TenantId tenantId, Guid reservationId, CancellationToken cancellationToken = default);

    void Add(Payment payment);
}

/// <summary>
/// Sérialise, jusqu'à la sauvegarde, les créations de paiement d'une même commande : deux encaissements simultanés
/// respecteraient chacun le reste à payer et le dépasseraient ensemble. À appeler AVANT de lire les paiements existants.
/// </summary>
public interface IPaymentOrderLock
{
    Task LockAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);
}

/// <summary>Ce que Payments doit savoir de son tenant, fourni par l'hôte depuis le module Tenants.</summary>
public interface IPaymentTenants
{
    /// <summary>Faux pour un tenant suspendu, fermé ou inconnu.</summary>
    Task<bool> AcceptsBusinessWritesAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Un établissement du tenant, fourni par l'hôte depuis Locations, avec son pays ; null s'il n'existe pas dans CE tenant.
/// </summary>
public interface IPaymentLocations
{
    Task<LocationReference?> FindAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Les cartes cadeaux et avoirs du restaurant, fournis par l'hôte depuis Loyalty (tenant du contexte seulement). Le code
/// n'est transmis qu'à <see cref="RedeemAsync"/> : il n'est jamais gardé ici, ni journalisé, ni renvoyé.
/// Erreurs métier (carte inconnue, solde insuffisant, expirée...) : <see cref="DomainException"/> ou NotFoundException.
/// </summary>
public interface IPaymentStoredValue
{
    Task<StoredValueDebit> RedeemAsync(TenantId tenantId, string code, Money amount, string reference, CancellationToken cancellationToken = default);

    Task CreditAsync(TenantId tenantId, Guid cardId, Money amount, string reference, CancellationToken cancellationToken = default);

    /// <summary>Annule le débit ou le crédit de cette référence ; sans effet s'il n'existe pas.</summary>
    Task ReverseAsync(TenantId tenantId, Guid cardId, string reference, CancellationToken cancellationToken = default);

    /// <summary>Émet un avoir de ce montant ; son code complet n'est renvoyé qu'ici, pour être remis au client.</summary>
    Task<IssuedStoreCredit> IssueStoreCreditAsync(TenantId tenantId, Money amount, string note, CancellationToken cancellationToken = default);

    /// <summary>Retire un avoir émis par erreur (compensation).</summary>
    Task CancelAsync(TenantId tenantId, Guid cardId, string reason, CancellationToken cancellationToken = default);
}

public sealed record StoredValueDebit(Guid CardId, string CodeHint);

public sealed record IssuedStoreCredit(Guid CardId, string Code, string CodeHint);

/// <summary>L'acompte d'une réservation du tenant courant, fourni par l'hôte depuis Reservations ; null sans acompte.</summary>
public interface IPaymentReservations
{
    Task<PaymentDeposit?> FindDepositAsync(TenantId tenantId, Guid reservationId, CancellationToken cancellationToken = default);
}

/// <param name="Amount">Montant de l'acompte fixé à la réservation (règle de l'établissement).</param>
/// <param name="AcceptsPayment">Un paiement est attendu (acompte pas encore payé, réservation non terminée).</param>
public sealed record PaymentDeposit(Guid ReservationId, LocationId LocationId, Money Amount, bool AcceptsPayment);

/// <summary>Une commande du tenant courant vue par la caisse, fournie par l'hôte depuis Orders ; null si elle n'existe pas.</summary>
public interface IPaymentOrders
{
    Task<PaymentOrder?> FindAsync(TenantId tenantId, OrderId orderId, CancellationToken cancellationToken = default);
}

/// <param name="LocationId">Établissement où la commande a été passée.</param>
/// <param name="Total">Total à payer, dans la devise de la commande.</param>
/// <param name="AcceptsPayments">Passée (ou plus loin), ni terminée ni annulée.</param>
/// <param name="Lines">Les lignes de la commande, pour une addition partagée par article ; vide si inconnues.</param>
public sealed record PaymentOrder(OrderId OrderId, LocationId LocationId, Money Total, bool AcceptsPayments, IReadOnlyList<PaymentOrderLine>? Lines = null);

/// <param name="Amount">Montant de la ligne, remise comprise, TTC.</param>
public sealed record PaymentOrderLine(Guid ItemId, int Quantity, Money Amount);

/// <summary>Le catalogue des prestataires de la plateforme (pas d'un tenant).</summary>
public interface IPaymentProviderRepository
{
    Task<PaymentProvider?> FindAsync(PaymentProviderId id, CancellationToken cancellationToken = default);

    Task<PaymentProvider?> FindByCodeAsync(PaymentProviderCode code, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, par nom affiché.</summary>
    Task<IReadOnlyList<PaymentProvider>> ListAsync(CancellationToken cancellationToken = default);

    void Add(PaymentProvider provider);
}

/// <summary>Les configurations de paiement d'un restaurant (une par prestataire).</summary>
public interface IRestaurantPaymentConfigurationRepository
{
    Task<RestaurantPaymentConfiguration?> FindAsync(
        TenantId tenantId, RestaurantPaymentConfigurationId id, CancellationToken cancellationToken = default);

    Task<RestaurantPaymentConfiguration?> FindByProviderAsync(
        TenantId tenantId, PaymentProviderCode provider, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule.</summary>
    Task<IReadOnlyList<RestaurantPaymentConfiguration>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);

    void Add(RestaurantPaymentConfiguration configuration);
}

/// <summary>
/// Le coffre des identifiants marchands (clés API, secrets, jetons, secrets de webhook). Seule l'Infrastructure voit
/// les valeurs ; le domaine et la base des agrégats ne gardent que la <see cref="PaymentCredentialReference"/>.
/// Implémentation par défaut chiffrée dans une table à part ; un Vault ou le gestionnaire de secrets d'un cloud peut la
/// remplacer sans rien changer d'autre. Les valeurs ne doivent JAMAIS apparaître dans un log, un événement, une
/// exception ou une réponse.
/// </summary>
public interface IPaymentSecretStore
{
    /// <summary>Range (ou remplace) les identifiants à cette référence.</summary>
    Task StoreAsync(PaymentCredentialReference reference, IReadOnlyDictionary<string, string> credentials, CancellationToken cancellationToken = default);

    /// <summary>Null si rien n'est rangé à cette référence.</summary>
    /// <exception cref="InvalidOperationException">Identifiants illisibles (altérés, ou clé du coffre changée).</exception>
    Task<IReadOnlyDictionary<string, string>?> ReadAsync(PaymentCredentialReference reference, CancellationToken cancellationToken = default);
}

/// <summary>
/// Les intégrations de prestataires en ligne présentes dans le code (une par prestataire : appels sortants et lecture
/// de ses webhooks). La plateforme ne peut proposer un prestataire en ligne que si son intégration existe.
/// </summary>
public interface IPaymentIntegrations
{
    IReadOnlyList<PaymentIntegration> All { get; }

    PaymentIntegration? Find(PaymentProviderCode provider);

    /// <summary>Le prestataire, branché sur le compte marchand décrit par <paramref name="credentials"/> (en clair).</summary>
    IPaymentGateway CreateGateway(PaymentProviderCode provider, IReadOnlyDictionary<string, string> credentials);
}

/// <param name="CredentialFields">Champs que le restaurant doit saisir (ex. <c>secret_key</c>, <c>webhook_secret</c>).</param>
public sealed record PaymentIntegration(PaymentProviderCode Provider, string Name, IReadOnlyList<string> CredentialFields);

/// <summary>
/// Un prestataire de paiement en ligne. Ses résultats (autorisation, capture, échec, remboursement) n'arrivent PAS par
/// ces appels mais par ses webhooks signés, vérifiés par l'Infrastructure puis appliqués par
/// <c>ApplyProviderResultCommand</c>.
/// </summary>
public interface IPaymentGateway
{
    PaymentProviderCode Provider { get; }

    /// <summary>Crée l'intention de paiement chez le prestataire ; renvoie sa référence et, le cas échéant, la page à ouvrir.</summary>
    /// <exception cref="PaymentGatewayException">Prestataire injoignable ou demande refusée.</exception>
    Task<GatewayCheckout> StartAsync(GatewayPaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Annule l'intention ou l'autorisation encore ouverte. Sans effet si elle n'existe plus.</summary>
    Task CancelAsync(ProviderReference reference, CancellationToken cancellationToken = default);

    /// <summary>Demande le remboursement ; <see cref="GatewayRefundRequest.RefundId"/> sert de clé d'idempotence.</summary>
    Task RequestRefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Vérifie le compte marchand par un appel sans effet chez le prestataire (solde, jeton, transaction inexistante).
    /// Un refus d'authentification est RENVOYÉ (<see cref="GatewayCredentialStatus.Rejected"/>), jamais levé.
    /// </summary>
    /// <exception cref="PaymentGatewayException">Prestataire injoignable ou réponse inattendue.</exception>
    Task<GatewayCredentialCheck> CheckCredentialsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(GatewayCredentialCheck.NotSupported);

    /// <summary>
    /// Relit l'état d'une intention chez le prestataire, qui fait foi. Null : ce prestataire ne permet pas de la relire
    /// par la référence que nous gardons.
    /// </summary>
    /// <exception cref="PaymentGatewayException">Prestataire injoignable ou réponse inattendue.</exception>
    Task<GatewayTransactionStatus?> FetchStatusAsync(ProviderReference reference, CancellationToken cancellationToken = default) =>
        Task.FromResult<GatewayTransactionStatus?>(null);
}

public enum GatewayTransactionState
{
    Pending = 1,
    Captured = 2,
    Failed = 3,
}

/// <param name="Amount">Montant encaissé selon le prestataire (unités de la devise), si connu.</param>
public sealed record GatewayTransactionStatus(GatewayTransactionState State, decimal? Amount, string? Currency);

public enum GatewayCredentialStatus
{
    /// <summary>Le prestataire a accepté les identifiants.</summary>
    Verified = 1,

    /// <summary>Le prestataire a refusé les identifiants (clé erronée, révoquée, mauvais environnement).</summary>
    Rejected = 2,

    /// <summary>Ce prestataire n'offre aucun appel sans effet pour vérifier un compte.</summary>
    NotSupported = 3,
}

/// <param name="IsSandbox">Compte de test du prestataire ; null quand on ne peut pas le savoir.</param>
/// <param name="Warnings">Codes stables de ce qui empêchera d'encaisser malgré des identifiants valides (ex. <c>checkout_return_url_missing</c>).</param>
public sealed record GatewayCredentialCheck(GatewayCredentialStatus Status, bool? IsSandbox, IReadOnlyList<string> Warnings)
{
    public static GatewayCredentialCheck NotSupported { get; } = new(GatewayCredentialStatus.NotSupported, null, []);
}

/// <param name="ConfigurationId">La configuration du restaurant : l'adresse de ses webhooks en dépend.</param>
/// <param name="PayerPhone">Numéro du payeur au format international, pour un paiement poussé sur son téléphone (mobile money). Jamais conservé.</param>
/// <param name="ReturnUrl">Page de retour du client propre à cet encaissement (application de vente en ligne) ; null = celle de la plateforme.</param>
/// <param name="Label">Libellé montré au payeur, sans donnée personnelle ; null = « Commande » et le début de son id.</param>
public sealed record GatewayPaymentRequest(
    TenantId TenantId,
    PaymentId PaymentId,
    OrderId OrderId,
    Money Amount,
    Guid ConfigurationId = default,
    string? PayerPhone = null,
    Uri? ReturnUrl = null,
    string? Label = null);

public sealed record GatewayCheckout(ProviderReference Reference, Uri? CheckoutUrl);

/// <param name="CapturedReference">Référence de la tentative qui a encaissé l'argent.</param>
public sealed record GatewayRefundRequest(TenantId TenantId, PaymentId PaymentId, RefundId RefundId, ProviderReference CapturedReference, Money Amount);

/// <summary>Échec d'un appel au prestataire. <see cref="IsPermanent"/> : refus définitif (inutile de réessayer).</summary>
public sealed class PaymentGatewayException : Exception
{
    public PaymentGatewayException()
    {
    }

    public PaymentGatewayException(string message)
        : base(message)
    {
    }

    public PaymentGatewayException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public PaymentGatewayException(string message, bool isPermanent, Exception? innerException = null)
        : base(message, innerException) => IsPermanent = isPermanent;

    public bool IsPermanent { get; }

    /// <summary>Statut HTTP renvoyé par le prestataire ; null sans réponse.</summary>
    public int? StatusCode { get; init; }

    /// <summary>Code d'erreur court renvoyé par le prestataire (sans donnée), ou null.</summary>
    public string? ProviderErrorCode { get; init; }
}

/// <summary>Les sessions de caisse des établissements du tenant.</summary>
public interface ICashSessionRepository
{
    Task<CashSession?> FindAsync(TenantId tenantId, CashSessionId id, CancellationToken cancellationToken = default);

    /// <summary>La session ouverte de cet établissement, s'il y en a une.</summary>
    Task<CashSession?> FindOpenAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    /// <summary>Plus grand numéro déjà attribué dans cet établissement ; 0 s'il n'y en a pas.</summary>
    Task<int> LastNumberAsync(TenantId tenantId, LocationId locationId, CancellationToken cancellationToken = default);

    /// <summary>Lecture seule, les plus récentes d'abord ; sessions ouvertes dans la période si elle est donnée.</summary>
    Task<IReadOnlyList<CashSession>> ListAsync(
        TenantId tenantId, LocationId? locationId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, int limit, CancellationToken cancellationToken = default);

    void Add(CashSession session);
}

public enum PaymentJournalEntryKind
{
    Capture = 1,
    Refund = 2,
}

/// <summary>
/// Un mouvement d'argent : une capture (montant capturé du paiement) ou un remboursement réussi (montant positif, à
/// retrancher). Les captures reçues après annulation n'y figurent pas : elles ne paient rien.
/// </summary>
/// <param name="Reference">Référence de la tentative encaissée ou du remboursement chez le prestataire (ticket de caisse...).</param>
public sealed record PaymentJournalEntry(
    PaymentId PaymentId,
    OrderId OrderId,
    LocationId LocationId,
    PaymentJournalEntryKind Kind,
    PaymentProviderCode Provider,
    PaymentMethodType Method,
    Money Amount,
    DateTimeOffset AtUtc,
    string? Reference);

/// <summary>Les mouvements d'argent du tenant sur une période : journal des encaissements et clôture de caisse.</summary>
public interface IPaymentJournal
{
    /// <summary>Mouvements de la période [début, fin[, les plus récents d'abord, <paramref name="limit"/> au plus.</summary>
    Task<IReadOnlyList<PaymentJournalEntry>> ListAsync(
        TenantId tenantId, LocationId? locationId, DateTimeOffset fromUtc, DateTimeOffset toUtc, int limit, CancellationToken cancellationToken = default);

    /// <summary>Encaissé et remboursé par moyen sur la période [début, fin[ (calculé en base, sans limite).</summary>
    Task<IReadOnlyList<CashSessionTakings>> TakingsAsync(
        TenantId tenantId, LocationId locationId, Currency currency, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default);
}

/// <summary>Devise du tenant, fournie par l'hôte depuis Tenants ; null si le tenant est inconnu.</summary>
public interface IPaymentTenantCurrency
{
    Task<Currency?> FindAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>L'utilisateur authentifié qui agit sur la caisse ; null hors requête.</summary>
public interface ICashier
{
    Guid? CurrentUserId { get; }
}
