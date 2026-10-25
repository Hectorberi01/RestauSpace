import {api, del, post, put, type Page} from './api';
import type {Tone} from './format';

export type SubscriptionStatus = 'Trialing' | 'Active' | 'PastDue' | 'Suspended' | 'Cancelled';

export interface Entitlement {
  featureCode: string;
  enabled: boolean;
  limit: number | null;
}

export interface Plan {
  id: string;
  name: string;
  price: number;
  currencyCode: string;
  billingInterval: 'Monthly' | 'Yearly';
  status: 'Active' | 'Archived';
  entitlements: Entitlement[];
  kind?: 'Base' | 'AddOn';
}

/** Une ligne de ce que paie une période : le plan ou une option (prix unitaire pour la période). */
export interface ChargeLine {
  planId: string;
  kind: 'Base' | 'AddOn';
  label: string;
  quantity: number;
  unitAmount: number;
  amount: number;
}

export interface Charge {
  currencyCode: string;
  billingInterval: 'Monthly' | 'Yearly';
  lines: ChargeLine[];
  total: number;
  monthlyTotal: number;
}

export interface TenantAddOn {
  id: string;
  addOn: Plan;
  quantity: number;
  status: 'Active' | 'Cancelled';
  activatedAtUtc: string;
}

export interface MyAddOns {
  offered: Plan[];
  active: TenantAddOn[];
}

export interface MySubscription {
  subscription: {
    id: string;
    planId: string;
    status: SubscriptionStatus;
    currentPeriodStartUtc: string;
    currentPeriodEndUtc: string;
    cancelAtPeriodEnd: boolean;
    suspensionReason: 'TrialExpired' | 'PaymentOverdue' | 'PolicyViolation' | 'Other' | null;
    startedAtUtc: string;
    cancelledAtUtc: string | null;
    /** Plan offert par RestauSpace : rien à payer avant la fin de l'offre. */
    isComplimentary?: boolean;
    /** Fin de l'offre ; null quand elle est sans limite. */
    complimentaryUntilUtc?: string | null;
    /** Plan programmé pour la fin de la période payée. */
    scheduledPlanId?: string | null;
    scheduledPlanEffectiveAtUtc?: string | null;
  };
  plan: Plan;
  usage: (Entitlement & {used: number})[];
  /** Au plan programmé s'il y en a un. */
  nextCharge: Charge | null;
  /** Changement de plan programmé : il s'applique à l'échéance. */
  scheduledPlan?: ScheduledPlan | null;
}

export interface ScheduledPlan {
  planId: string;
  planName: string;
  effectiveAtUtc: string;
}

export interface SubscriptionPayment {
  id: string;
  planName: string;
  amount: number;
  currencyCode: string;
  paidFromUtc: string;
  paidUntilUtc: string;
  recordedAtUtc: string;
  lines?: ChargeLine[] | null;
  /** Numéro de facture ; absent pour un paiement enregistré avant la numérotation (reçu). */
  invoiceNumber?: string | null;
  /** Prestataire qui a encaissé en ligne ; absent si le paiement a été enregistré par la plateforme. */
  collectedBy?: string | null;
}

export type CheckoutStatus = 'Pending' | 'Paid' | 'Failed' | 'Expired';

export interface Checkout {
  id: string;
  status: CheckoutStatus;
  amount: number;
  currencyCode: string;
  provider: string;
  checkoutUrl: string | null;
  expiresAtUtc: string;
  paymentId: string | null;
}

export interface OnlinePayment {
  available: boolean;
  due: boolean;
  amount: number | null;
  currencyCode: string | null;
  provider: string | null;
  pending: Checkout | null;
  /** Les prestataires entre lesquels choisir, dans l'ordre de la plateforme (absent : API plus ancienne). */
  options?: PaymentOption[] | null;
}

export interface PaymentOption {
  provider: string;
  name: string;
  methods: string[];
}

/** Libellés des moyens de paiement d'un prestataire. */
export const methodName: Record<string, string> = {Card: 'Carte bancaire', ApplePay: 'Apple Pay', GooglePay: 'Google Pay', PayPal: 'Compte PayPal', MobileMoney: 'Mobile Money', BankTransfer: 'Virement'};

export interface Invoice {
  paymentId: string;
  number: string | null;
  issuedAtUtc: string;
  issuer: {name: string; address: string | null; taxId: string | null; registrationNumber: string | null; email: string | null; footer: string | null};
  customerName: string;
  planName: string;
  periodStartUtc: string;
  periodEndUtc: string;
  lines: ChargeLine[];
  total: number;
  currencyCode: string;
  collectedBy: string | null;
}

export const providerName: Record<string, string> = {fedapay: 'FedaPay', kkiapay: 'KKiaPay', cinetpay: 'CinetPay', mtn_momo: 'MTN Mobile Money', stripe: 'Stripe', paypal: 'PayPal', wave: 'Wave', flutterwave: 'Flutterwave', paystack: 'Paystack', paydunya: 'PayDunya', mollie: 'Mollie', sandbox: 'Sandbox'};

/** Prestataires qui poussent la demande sur le téléphone du payeur : son numéro est demandé. */
export const pushesToPhone = (provider: string | null) => provider === 'mtn_momo';

export const statusLabel: Record<SubscriptionStatus, string> = {
  Trialing: 'Essai gratuit',
  Active: 'Actif',
  PastDue: 'Paiement en retard',
  Suspended: 'Suspendu',
  Cancelled: 'Résilié',
};

export const statusTone: Record<SubscriptionStatus, Tone> = {Trialing: 'blue', Active: 'green', PastDue: 'amber', Suspended: 'red', Cancelled: 'neutral'};

export const suspensionLabel: Record<string, string> = {
  TrialExpired: 'la période d’essai est terminée',
  PaymentOverdue: 'un paiement est en retard',
  PolicyViolation: 'non-respect des conditions d’utilisation',
  Other: 'décision de la plateforme',
};

const features: Record<string, string> = {
  'orders.monthly': 'Commandes par mois',
  'locations.count': 'Établissements',
  'locations.extra': 'Établissements supplémentaires en option',
  'staff.count': 'Membres du personnel',
  'notifications.email.monthly': 'Emails par mois',
  'notifications.sms.monthly': 'SMS par mois',
  'notifications.whatsapp.monthly': 'Messages WhatsApp par mois',
  'notifications.push.monthly': 'Notifications push par mois',
  'notifications.inapp.monthly': 'Notifications dans l’application par mois',
  'notifications.byok': 'Envoi par votre propre compte (sans quota)',
  reservations: 'Réservations',
  delivery: 'Livraison',
  loyalty: 'Fidélité',
  promotions: 'Promotions',
  inventory: 'Stock',
  reviews: 'Avis clients',
  reporting: 'Rapports',
  integrations: 'Intégrations et API',
  'customers.advanced': 'Clients : import, étiquettes et segments',
  'marketing.campaigns': 'Campagnes marketing',
  'marketing.gift_cards': 'Cartes cadeaux',
  automations: 'Automatisations (rapports planifiés, campagnes programmées)',
  'reporting.advanced': 'Exports, comptabilité et rapprochement',
  'reporting.consolidated': 'Rapports tous établissements',
  'storefront.mobile_app': 'Commande en ligne : application mobile',
  'storefront.kiosk': 'Bornes de commande',
  'storefront.api': 'API : votre site de commande et vos clés d’API',
  'integrations.webhooks': 'Webhooks',
  'support.priority': 'Support prioritaire',
};

/** Ordre du comparatif des plans : limites, puis fonctions. */
export const comparedLimits = [
  'locations.count', 'locations.extra', 'staff.count', 'orders.monthly',
  'notifications.email.monthly', 'notifications.sms.monthly', 'notifications.whatsapp.monthly', 'notifications.push.monthly', 'notifications.inapp.monthly',
];
export const comparedFeatures = [
  'loyalty', 'customers.advanced', 'marketing.campaigns', 'marketing.gift_cards', 'automations', 'reporting.advanced', 'reporting.consolidated',
  'storefront.mobile_app', 'storefront.kiosk', 'storefront.api', 'integrations.webhooks', 'notifications.byok', 'support.priority',
];

export const featureLabel = (code: string) => features[code] ?? code;

/** Quota compté sur le mois (remis à zéro chaque mois), par opposition à un nombre d'éléments existants. */
export const isMonthly = (code: string) => code.endsWith('.monthly');

export const loadSubscription = (signal?: AbortSignal) => api<MySubscription>('/billing/subscription', {signal});

export const listPlans = (signal?: AbortSignal) => api<Plan[]>('/billing/plans', {signal});

export const listPayments = (page: number, signal?: AbortSignal) =>
  api<Page<SubscriptionPayment>>('/billing/payments', {query: {page, pageSize: 12}, signal});

export const subscribe = (planId: string) => post<{id: string}>('/billing/subscription', {planId});

export const changePlan = (planId: string) => put('/billing/subscription/plan', {planId});

export const unschedulePlanChange = () => del('/billing/subscription/scheduled-plan');

export const scheduleCancellation = () => post('/billing/subscription/cancellation');

export const unscheduleCancellation = () => del('/billing/subscription/cancellation');

export const listAddOns = (signal?: AbortSignal) => api<MyAddOns>('/billing/add-ons', {signal});

export const subscribeAddOn = (planId: string, quantity = 1) => post<{id: string}>('/billing/add-ons', {planId, quantity});

export const changeAddOnQuantity = (id: string, quantity: number) => put(`/billing/add-ons/${id}/quantity`, {quantity});

export const cancelAddOn = (id: string) => post(`/billing/add-ons/${id}/cancel`);

export const loadOnlinePayment = (signal?: AbortSignal) => api<OnlinePayment>('/billing/online-payment', {signal});

export const startCheckout = (payerPhone: string | null, provider?: string | null) => post<Checkout>('/billing/checkouts', {payerPhone, provider: provider ?? null});

export const refreshCheckout = (id: string) => post<Checkout>(`/billing/checkouts/${id}/refresh`);

export const loadInvoice = (paymentId: string) => api<Invoice>(`/billing/payments/${paymentId}/invoice`);

/** Une option dont la quantité compte (ex. nombre de bornes) : un de ses droits porte une limite. */
export const isCounted = (plan: Plan) => plan.entitlements.some(item => item.enabled && item.limit !== null);

/** Le plan donne-t-il déjà tout ce que donne l'option (même règle que l'API) ? */
export function includedInPlan(plan: Plan, addOn: Plan): boolean {
  const granted = addOn.entitlements.filter(item => item.enabled);
  return granted.length > 0 && granted.every(item => {
    const own = plan.entitlements.find(entry => entry.featureCode === item.featureCode);
    return !!own?.enabled && (own.limit === null || (item.limit !== null && own.limit >= item.limit));
  });
}

/** L'option Établissement supplémentaire exige `locations.extra` dans le plan. */
export function addOnUnavailable(plan: Plan, addOn: Plan): boolean {
  const extraLocation = addOn.entitlements.some(item => item.enabled && item.featureCode === 'locations.count');
  return extraLocation && !plan.entitlements.some(item => item.featureCode === 'locations.extra' && item.enabled);
}

export interface PlanLoss {
  code: string;
  /** Limite actuelle (null : illimité ou simple fonction). */
  from: number | null;
  /** Nouvelle limite ; null quand le droit disparaît. */
  to: number | null;
  lost: boolean;
}

/** Ce que le plan cible retire au plan actuel : droits perdus et limites plus basses. */
export function downgradeLosses(current: Plan, target: Plan): PlanLoss[] {
  return current.entitlements.flatMap<PlanLoss>(item => {
    if (!item.enabled) return [];
    const next = target.entitlements.find(entry => entry.featureCode === item.featureCode);
    if (!next?.enabled) return [{code: item.featureCode, from: item.limit, to: null, lost: true}];
    if (next.limit !== null && (item.limit === null || next.limit < item.limit)) return [{code: item.featureCode, from: item.limit, to: next.limit, lost: false}];
    return [];
  });
}
