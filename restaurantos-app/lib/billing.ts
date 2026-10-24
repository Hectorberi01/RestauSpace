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
  'staff.count': 'Membres du personnel',
  'notifications.email.monthly': 'Emails par mois',
  'notifications.sms.monthly': 'SMS par mois',
  'notifications.whatsapp.monthly': 'Messages WhatsApp par mois',
  'notifications.push.monthly': 'Notifications push par mois',
  'notifications.inapp.monthly': 'Notifications dans l’application par mois',
  reservations: 'Réservations',
  delivery: 'Livraison',
  loyalty: 'Fidélité',
  promotions: 'Promotions',
  inventory: 'Stock',
  reviews: 'Avis clients',
  reporting: 'Rapports',
  integrations: 'Intégrations et API',
  'storefront.web': 'Commande en ligne : site web',
  'storefront.mobile_app': 'Commande en ligne : application mobile',
  'storefront.kiosk': 'Bornes de commande',
  'storefront.api': 'API de vente en ligne',
};

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
