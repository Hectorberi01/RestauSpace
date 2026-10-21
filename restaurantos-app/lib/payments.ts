import {api, post, put} from './api';

export type PaymentMethod =
  | 'Card' | 'ApplePay' | 'GooglePay' | 'PayPal' | 'MobileMoney' | 'BankTransfer' | 'Cash' | 'CardOnSite' | 'MobileMoneyOnSite' | 'MealVoucher' | 'GiftCard' | 'DeliveryPlatform' | 'CashOnDelivery';
export type PaymentStatus = 'Pending' | 'Authorized' | 'Captured' | 'Failed' | 'Cancelled' | 'PartiallyRefunded' | 'Refunded';
export type ProviderType = 'OnSite' | 'Online';
export type PaymentMode = 'PlatformManaged' | 'TenantManaged';

export interface Refund {
  id: string;
  amount: number;
  reason: string;
  status: 'Requested' | 'Succeeded' | 'Failed';
  providerReference: string | null;
  requestedAtUtc: string;
  completedAtUtc: string | null;
}

export interface Attempt {
  id: string;
  providerReference: string;
  status: 'Pending' | 'Succeeded' | 'Failed';
  startedAtUtc: string;
  failureReason: string | null;
}

export interface Payment {
  id: string;
  orderId: string;
  provider: string;
  method: PaymentMethod;
  isOffline: boolean;
  status: PaymentStatus;
  currencyCode: string;
  amount: number;
  capturedAmount: number;
  refundedAmount: number;
  pendingRefundAmount: number;
  refundableAmount: number;
  settledAmount: number;
  hasUnexpectedCapture: boolean;
  attempts: Attempt[];
  refunds: Refund[];
  createdAtUtc: string;
  capturedAtUtc: string | null;
  cancelledAtUtc: string | null;
  cancellationReason: string | null;
  /** Lignes de la commande réglées par ce paiement (addition partagée par article). */
  coveredItems?: {itemId: string; quantity: number}[];
}

export interface OrderPayments {
  orderId: string;
  currencyCode: string;
  total: number;
  settledAmount: number;
  amountDue: number;
  payments: Payment[];
}

export interface Provider {
  id: string;
  code: string;
  type: ProviderType;
  displayName: string;
  isEnabled: boolean;
  currencies: string[];
  capability: {methods: PaymentMethod[]; supportsRefund: boolean; supportsPartialRefund: boolean};
  credentialFields: string[];
  hasIntegration: boolean;
}

export interface Configuration {
  id: string;
  providerId: string;
  provider: string;
  displayName: string;
  type: ProviderType;
  mode: PaymentMode;
  status: 'AwaitingCredentials' | 'Configured';
  isEnabled: boolean;
  isAvailable: boolean;
  hasCredentials: boolean;
  enabledMethods: PaymentMethod[];
  currencies: string[];
  requiredCredentialFields: string[];
  isGlobal: boolean;
  assignments: Assignment[];
  webhookPath: string | null;
}

/** Un moyen proposé dans un établissement : rang (plus petit = proposé d'abord) et bornes de montant. */
export interface Assignment {
  locationId: string;
  method: PaymentMethod;
  isEnabled: boolean;
  priority: number;
  minAmount: number | null;
  maxAmount: number | null;
  currency: string | null;
}

export interface OnlineStarted {
  paymentId: string;
  providerReference: string;
  checkoutUrl: string | null;
}

export const methodLabel: Record<PaymentMethod, string> = {
  Cash: 'Espèces',
  CardOnSite: 'Carte (terminal)',
  MobileMoneyOnSite: 'Mobile money',
  MealVoucher: 'Titre-restaurant',
  GiftCard: 'Carte cadeau / avoir',
  DeliveryPlatform: 'Payé à la plateforme',
  CashOnDelivery: 'Espèces (livreur)',
  BankTransfer: 'Virement',
  Card: 'Carte en ligne',
  ApplePay: 'Apple Pay',
  GooglePay: 'Google Pay',
  PayPal: 'PayPal',
  MobileMoney: 'Mobile money en ligne',
};

export const statusLabel: Record<PaymentStatus, string> = {
  Pending: 'En attente',
  Authorized: 'Autorisé',
  Captured: 'Encaissé',
  Failed: 'Échoué',
  Cancelled: 'Annulé',
  PartiallyRefunded: 'Remboursé en partie',
  Refunded: 'Remboursé',
};

export const statusTone: Record<PaymentStatus, 'neutral' | 'brand' | 'blue' | 'amber' | 'green' | 'red'> = {
  Pending: 'amber',
  Authorized: 'blue',
  Captured: 'green',
  Failed: 'red',
  Cancelled: 'neutral',
  PartiallyRefunded: 'amber',
  Refunded: 'neutral',
};

/** Un moyen proposé à la caisse : une configuration acceptée et l'un de ses moyens. */
export interface Tender {
  key: string;
  provider: string;
  method: PaymentMethod;
  type: ProviderType;
  label: string;
}

export function tendersOf(configurations: Configuration[], currency: string): Tender[] {
  return configurations
    .filter(configuration => configuration.isEnabled && configuration.isAvailable && configuration.status === 'Configured')
    .filter(configuration => configuration.currencies.length === 0 || configuration.currencies.includes(currency))
    .flatMap(configuration => configuration.enabledMethods.filter(method => method !== 'CashOnDelivery').map(method => ({
      key: `${configuration.provider}:${method}`,
      provider: configuration.provider,
      method,
      type: configuration.type,
      label: configuration.enabledMethods.length > 1 ? `${configuration.displayName} · ${methodLabel[method]}` : configuration.displayName,
    })))
    .sort((a, b) => Number(a.type === 'Online') - Number(b.type === 'Online'));
}

interface AvailableMethod {
  configurationId: string;
  provider: string;
  displayName: string;
  type: ProviderType;
  method: PaymentMethod;
  priority: number;
}

/**
 * Les moyens que Payments propose pour CETTE commande : établissement, montant restant, devise et pays ; au comptoir,
 * moyens sur place et en ligne.
 */
export async function availableTenders(locationId: string, amount: number, currency: string, signal?: AbortSignal): Promise<Tender[]> {
  // Les espèces du livreur s'enregistrent à la livraison, jamais au comptoir.
  const methods = (await api<AvailableMethod[]>('/payments/payment-methods/available', {query: {locationId, amount, currency, channel: 'InPerson'}, signal}))
    .filter(item => item.method !== 'CashOnDelivery');
  const perProvider = new Map<string, number>();
  methods.forEach(item => perProvider.set(item.provider, (perProvider.get(item.provider) ?? 0) + 1));
  return methods.map(item => ({
    key: `${item.provider}:${item.method}`,
    provider: item.provider,
    method: item.method,
    type: item.type,
    label: (perProvider.get(item.provider) ?? 0) > 1 ? `${item.displayName} · ${methodLabel[item.method]}` : item.displayName,
  }));
}

export const loadOrderPayments = (orderId: string, signal?: AbortSignal) => api<OrderPayments>('/payments', {query: {orderId}, signal});

export const loadConfigurations = (signal?: AbortSignal) => api<Configuration[]>('/payments/configurations', {signal});

export const loadProviders = (signal?: AbortSignal) => api<Provider[]>('/payments/providers', {signal});

/** Référence de caisse générée quand le caissier n'en saisit pas (ticket, n° de transaction…). */
export function counterReference(orderNumber: string, method: PaymentMethod): string {
  const now = new Date();
  const stamp = [now.getHours(), now.getMinutes(), now.getSeconds()].map(part => String(part).padStart(2, '0')).join('');
  return `${method === 'Cash' ? 'ESP' : 'CAISSE'}-${orderNumber}-${stamp}`;
}

export interface ItemShare {
  itemId: string;
  quantity: number;
}

/** Montant : un montant libre, ou (addition par article) les lignes choisies, dont le serveur calcule le montant. */
export const recordOffline = (body: {orderId: string; provider: string; reference: string; amount?: number; items?: ItemShare[]; paymentMethod: PaymentMethod}) =>
  post<{id: string}>('/payments/offline', body);

export const startOnline = (body: {orderId: string; provider: string; amount?: number; items?: ItemShare[]; paymentMethod: PaymentMethod; payerPhone?: string}) =>
  post<OnlineStarted>('/payments/online', body);

/** Unités de chaque ligne déjà réglées (ou en cours) par les paiements qui comptent encore. */
export function coveredUnits(payments: Payment[]): Map<string, number> {
  const covered = new Map<string, number>();
  payments
    .filter(payment => payment.status !== 'Cancelled' && payment.status !== 'Refunded')
    .forEach(payment => (payment.coveredItems ?? []).forEach(item => covered.set(item.itemId, (covered.get(item.itemId) ?? 0) + item.quantity)));
  return covered;
}

/** Part de <quantity> unités d'une ligne (même calcul que le serveur : les dernières unités prennent le reste). */
export function itemShare(lineTotal: number, lineQuantity: number, already: number, quantity: number, digits: number): number {
  const factor = 10 ** digits;
  const round = (value: number) => Math.round(value * factor) / factor;
  const before = round((lineTotal * already) / lineQuantity);
  const after = already + quantity === lineQuantity ? lineTotal : round((lineTotal * (already + quantity)) / lineQuantity);
  return round(after - before);
}

/** Prestataires qui poussent la demande sur le téléphone du payeur : son numéro est exigé. */
export const pushProviders = new Set(['mtn_momo']);

export const cancelPayment = (id: string, reason: string) => post(`/payments/${id}/cancel`, {reason});

export const retryPayment = (id: string, payerPhone?: string) => post<OnlineStarted>(`/payments/${id}/retry`, payerPhone ? {payerPhone} : undefined);

export const refund = (id: string, reason: string, amount: number | null) => post<{id: string}>(`/payments/${id}/refunds`, {reason, amount});

/** Rembourse en avoir : le code de l'avoir n'est renvoyé qu'ici, une seule fois. */
export const refundAsStoreCredit = (id: string, reason: string, amount: number | null) =>
  post<{refundId: string; cardId: string; code: string; codeHint: string; amount: number; currencyCode: string}>(`/payments/${id}/refunds/store-credit`, {reason, amount});

export const createConfiguration = (provider: string, mode: PaymentMode | null) =>
  post<{id: string}>('/payments/configurations', {provider, mode});

export const enableConfiguration = (id: string) => post(`/payments/configurations/${id}/enable`);

export const disableConfiguration = (id: string) => post(`/payments/configurations/${id}/disable`);

export const setMethod = (id: string, method: PaymentMethod, isActive: boolean) => put(`/payments/configurations/${id}/methods`, {method, isActive});

export const setCredentials = (id: string, credentials: Record<string, string>) => put(`/payments/configurations/${id}/credentials`, {credentials});

export const assignLocation = (
  id: string,
  locationId: string,
  body: {method: PaymentMethod; priority: number; minAmount: number | null; maxAmount: number | null; currency: string; isEnabled: boolean},
) => put(`/payments/configurations/${id}/locations/${locationId}`, body);

export const unassignLocation = (id: string, locationId: string, method?: PaymentMethod) =>
  api(`/payments/configurations/${id}/locations/${locationId}`, {method: 'DELETE', query: method ? {method} : {}});

/** Les prestataires qui remboursent par l'API ; les autres, depuis leur propre tableau de bord. */
export const refundsByApi = new Set(['stripe', 'paypal', 'sandbox']);

export const setMode = (id: string, mode: PaymentMode) => put(`/payments/configurations/${id}/mode`, {mode});

/** Un champ d'identifiants d'un prestataire : libellé, secret (masqué) ou non, choix possibles. */
export interface CredentialField {
  label: string;
  secret: boolean;
  hint?: string;
  options?: [string, string][];
}

const environments: [string, string][] = [['sandbox', 'Test (sandbox)'], ['live', 'Production']];

const mtnEnvironments: [string, string][] = [
  ['sandbox', 'Test (sandbox)'], ['mtnbenin', 'Production · Bénin'], ['mtnivorycoast', 'Production · Côte d’Ivoire'],
  ['mtncameroon', 'Production · Cameroun'], ['mtnguineaconakry', 'Production · Guinée'], ['mtncongo', 'Production · Congo'],
  ['mtnghana', 'Production · Ghana'], ['mtnuganda', 'Production · Ouganda'], ['mtnzambia', 'Production · Zambie'],
];

const credentialFields: Record<string, Record<string, CredentialField>> = {
  fedapay: {
    secret_key: {label: 'Clé secrète', secret: true, hint: 'sk_sandbox_… pour les tests, sk_live_… en production (Paramètres › Clés API).'},
    webhook_secret: {label: 'Clé secrète du webhook', secret: true, hint: 'Affichée à la création du webhook dans FedaPay.'},
  },
  kkiapay: {
    public_key: {label: 'Clé publique', secret: false},
    private_key: {label: 'Clé privée', secret: true},
    secret_key: {label: 'Clé secrète', secret: true},
    webhook_secret: {label: 'Secret du webhook', secret: true, hint: 'Le « hash secret » choisi en déclarant le webhook dans KKiaPay.'},
    environment: {label: 'Environnement', secret: false, options: environments},
  },
  cinetpay: {
    api_key: {label: 'Clé API', secret: true},
    site_id: {label: 'Identifiant du site (site ID)', secret: false},
  },
  mtn_momo: {
    api_user: {label: 'Utilisateur API (API user)', secret: false},
    api_key: {label: 'Clé API', secret: true},
    subscription_key: {label: 'Clé d’abonnement Collection', secret: true, hint: 'Primary key du produit Collection (Ocp-Apim-Subscription-Key).'},
    environment: {label: 'Environnement', secret: false, options: mtnEnvironments},
  },
  stripe: {
    secret_key: {label: 'Clé secrète', secret: true, hint: 'sk_test_… pour les tests, sk_live_… en production.'},
    webhook_secret: {label: 'Secret de signature du webhook', secret: true, hint: 'whsec_…, affiché dans Développeurs › Webhooks.'},
  },
  paypal: {
    client_id: {label: 'Client ID', secret: false},
    client_secret: {label: 'Secret', secret: true},
    webhook_id: {label: 'ID du webhook', secret: false, hint: 'Visible dans votre application PayPal, section Webhooks.'},
    environment: {label: 'Environnement', secret: false, options: environments},
  },
  wave: {
    api_key: {label: 'Clé d’API', secret: true, hint: 'wave_sn_prod_… (Wave Business › Développeurs).'},
    webhook_secret: {label: 'Secret du webhook', secret: true},
  },
  flutterwave: {
    secret_key: {label: 'Clé secrète', secret: true, hint: 'FLWSECK_TEST-… pour les tests, FLWSECK-… en production.'},
    webhook_hash: {label: 'Empreinte secrète des webhooks', secret: true, hint: 'Le « Secret hash » choisi dans Paramètres › Webhooks.'},
    account_email: {label: 'E-mail du compte marchand', secret: false, hint: 'Transmis à Flutterwave à la place de celui du client.'},
  },
  paystack: {
    secret_key: {label: 'Clé secrète', secret: true, hint: 'sk_test_… pour les tests, sk_live_… en production.'},
    account_email: {label: 'E-mail du compte marchand', secret: false, hint: 'Transmis à Paystack à la place de celui du client.'},
  },
  paydunya: {
    master_key: {label: 'Clé principale (master key)', secret: true},
    private_key: {label: 'Clé privée', secret: true, hint: 'test_private_… ou live_private_…, selon l’environnement.'},
    token: {label: 'Jeton (token)', secret: true},
    environment: {label: 'Environnement', secret: false, options: environments},
  },
  mollie: {
    api_key: {label: 'Clé d’API', secret: true, hint: 'test_… pour les tests, live_… en production.'},
  },
};

/** Description d'un champ d'identifiants ; un champ inconnu est traité comme un secret. */
export function credentialField(provider: string, field: string): CredentialField {
  return credentialFields[provider]?.[field] ?? {label: field.replace(/_/g, ' ').replace(/^\w/, letter => letter.toUpperCase()), secret: true};
}

/** Où trouver les identifiants, et si le webhook se déclare à la main chez le prestataire. */
export const providerGuide: Record<string, {where: string; webhookEvents?: string}> = {
  fedapay: {where: 'Tableau de bord FedaPay › Paramètres › Clés API.', webhookEvents: 'Événements : transaction.approved, transaction.declined, transaction.canceled.'},
  kkiapay: {where: 'Tableau de bord KKiaPay › Développeurs › Clés API.', webhookEvents: 'Événements : transaction.success et transaction.failed.'},
  cinetpay: {where: 'Back-office CinetPay › Intégrations : API Key et Site ID. L’adresse de notification est transmise à chaque paiement.'},
  mtn_momo: {where: 'Portail MTN MoMo (momodeveloper.mtn.com ou portail partenaire) : produit Collection. L’adresse de rappel est transmise à chaque paiement.'},
  stripe: {where: 'Tableau de bord Stripe › Développeurs › Clés API.', webhookEvents: 'Événements : checkout.session.completed, checkout.session.async_payment_succeeded, checkout.session.async_payment_failed, checkout.session.expired, refund.updated.'},
  paypal: {where: 'developer.paypal.com › Apps & Credentials : votre application REST.', webhookEvents: 'Événements : Checkout order approved, Payment capture completed, Payment capture denied, Payment capture refunded.'},
  wave: {where: 'Wave Business › Développeurs : clé d’API et webhook.', webhookEvents: 'Événements : checkout.session.completed, checkout.session.payment_failed.'},
  flutterwave: {where: 'Tableau de bord Flutterwave › Paramètres › API Keys ; Webhooks pour l’empreinte secrète. Les remboursements se font depuis ce tableau de bord.', webhookEvents: 'Cocher l’envoi des paiements réussis et échoués.'},
  paystack: {where: 'Tableau de bord Paystack › Settings › API Keys & Webhooks. Les remboursements se font depuis ce tableau de bord.'},
  paydunya: {where: 'Tableau de bord PayDunya › Intégrez nos API : votre application. L’adresse de notification est transmise à chaque paiement ; les remboursements se font depuis ce tableau de bord.'},
  mollie: {where: 'Tableau de bord Mollie › Développeurs › Clés d’API. L’adresse de notification est transmise à chaque paiement.'},
};

export type PaymentTestOutcome =
  | 'verified' | 'credentials_rejected' | 'credentials_missing' | 'credentials_incomplete' | 'credentials_unreadable'
  | 'provider_unavailable' | 'provider_error' | 'not_verifiable';

/** Résultat du test d'un compte marchand : aucun identifiant ni message brut du prestataire. */
export interface PaymentTest {
  outcome: PaymentTestOutcome;
  succeeded: boolean;
  isSandbox: boolean | null;
  providerStatus: number | null;
  warnings: string[];
  testedAtUtc: string;
}

export const testConfiguration = (id: string) => post<PaymentTest>(`/payments/configurations/${id}/test`);

/** Ce qu'on dit au restaurateur après un test : ton, constat, et que faire. */
export function paymentTestSummary(result: PaymentTest, provider: string): {tone: 'green' | 'amber' | 'red'; title: string; detail?: string} {
  switch (result.outcome) {
    case 'verified':
      return {tone: 'green', title: `${provider} a accepté les identifiants.`};
    case 'credentials_rejected':
      return {tone: 'red', title: `${provider} a refusé les identifiants.`, detail: 'Vérifiez les clés copiées et l’environnement choisi (test ou production), puis remplacez-les.'};
    case 'credentials_missing':
      return {tone: 'amber', title: 'Aucun identifiant enregistré.', detail: 'Saisissez d’abord les identifiants de votre compte marchand.'};
    case 'credentials_incomplete':
      return {tone: 'red', title: 'Identifiants incomplets ou invalides.', detail: 'Saisissez-les à nouveau.'};
    case 'credentials_unreadable':
      return {tone: 'red', title: 'Les identifiants enregistrés ne sont plus lisibles.', detail: 'Saisissez-les à nouveau.'};
    case 'provider_unavailable':
      return {tone: 'amber', title: `${provider} ne répond pas pour le moment.`, detail: 'Réessayez dans quelques minutes.'};
    case 'provider_error':
      return {tone: 'red', title: `${provider} a répondu par une erreur${result.providerStatus ? ` (HTTP ${result.providerStatus})` : ''}.`, detail: 'Vérifiez votre compte chez le prestataire, puis réessayez.'};
    default:
      return {tone: 'amber', title: 'Ce prestataire ne permet pas de vérifier un compte sans paiement.', detail: 'Faites un petit paiement réel pour le vérifier.'};
  }
}

const testWarnings: Record<string, string> = {
  checkout_return_url_missing: 'La page de retour après paiement n’est pas réglée sur la plateforme : les paiements échoueront. Contactez le support.',
  public_api_url_missing: 'L’adresse publique de l’API n’est pas réglée sur la plateforme : le prestataire ne pourra pas confirmer les paiements. Contactez le support.',
  public_api_url_not_https: 'L’adresse publique de l’API n’est pas en HTTPS : MTN n’enverra pas ses confirmations. Contactez le support.',
  webhook_id_unknown: 'PayPal ne connaît pas l’ID de webhook saisi pour ce compte : vérifiez-le dans votre tableau de bord développeur PayPal.',
};

export const paymentTestWarning = (code: string) => testWarnings[code] ?? `Réglage à vérifier : ${code}.`;

/** Prestataires dont le secret de webhook n'est vérifié qu'à la réception d'une notification signée. */
export const signedWebhookProviders = new Set(['fedapay', 'kkiapay', 'stripe']);
