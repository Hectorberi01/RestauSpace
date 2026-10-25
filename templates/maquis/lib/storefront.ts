import {config} from './config';
import {humanCheckToken, resetHumanCheck, setHumanCheckKey} from './human-check';

// Client de l'API de vente en ligne (/v1/storefront), appelé depuis le navigateur avec la clé publique (pk_live_…).
// Cette clé n'est pas un secret : elle ne marche que depuis les origines déclarées dans la console RestauSpace.

export type ServiceMode = 'DineIn' | 'Takeaway' | 'Delivery';
export type PaymentMode = 'Counter' | 'Online';
export type GuestOrderStatus = 'Received' | 'Accepted' | 'Preparing' | 'Ready' | 'Completed' | 'Cancelled';
export type GuestPaymentState = 'AtCounter' | 'AwaitingPayment' | 'Processing' | 'Paid' | 'Refunded';
export type Weekday = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday';

export interface HoursRange {
  start: string;
  end: string;
  endsNextDay: boolean;
}

export interface DeliveryZone {
  id: string;
  name: string;
  fee: number;
  minimumOrder: number;
  estimatedMinutes: number;
}

export interface StorefrontLocation {
  id: string;
  name: string;
  street: string;
  city: string;
  timeZoneId: string;
  isOpenNow: boolean;
  nextOpeningUtc: string | null;
  weeklyHours?: (HoursRange & {day: Weekday})[];
  isPaused: boolean;
  acceptsOrdersNow: boolean;
  serviceModes: ServiceMode[];
  preparationMinutes: number;
  minimumOrderAmount: number | null;
  payment: {payOnline: boolean; payAtCounter: boolean; counterLimit: number | null};
  delivery: {leadMinutes: number; zones: DeliveryZone[]} | null;
  slotCapacity: number | null;
  place?: {
    postalCode: string | null;
    countryCode: string | null;
    coordinates: {latitude: number; longitude: number} | null;
    contact: {phone: string | null; whatsApp: string | null; email: string | null};
  } | null;
  specialDays?: {date: string; isClosed: boolean; ranges: HoursRange[]; reason: string | null}[];
  closureReason?: string | null;
}

export interface Storefront {
  restaurant: {
    name: string;
    logoUrl: string | null;
    primaryColor: string | null;
    currencyCode: string;
    pricesIncludeTax: boolean;
    tagline?: string | null;
    description?: string | null;
    coverImageUrl?: string | null;
    links?: {network: string; url: string}[];
  };
  app: {id: string; name: string; kind: string; humanCheckSiteKey?: string | null};
  orderingEnabled: boolean;
  locations: StorefrontLocation[];
}

export interface Slots {
  timeZoneId: string;
  slotMinutes: number;
  slots: {startUtc: string; isAvailable: boolean}[];
}

export interface CatalogModifier {
  id: string;
  name: string;
  priceDelta: number;
  isAvailable: boolean;
}

export interface CatalogModifierGroup {
  id: string;
  name: string;
  minSelections: number;
  maxSelections: number;
  modifiers: CatalogModifier[];
}

export interface CatalogFormulaChoice {
  productId: string;
  variantId: string | null;
  name: string;
  variantName: string | null;
  supplement: number;
  isAvailable: boolean;
  modifierGroupIds: string[];
  allergens: string[];
}

export interface CatalogFormulaStep {
  id: string;
  name: string;
  minChoices: number;
  maxChoices: number;
  choices: CatalogFormulaChoice[];
}

export interface CatalogProduct {
  id: string;
  name: string;
  description: string | null;
  price: number;
  isAvailable: boolean;
  variants: {id: string; name: string; price: number; isAvailable: boolean}[];
  modifierGroupIds: string[];
  allergens: string[];
  images: string[];
  thumbnails?: (string | null)[];
  formulaSteps?: CatalogFormulaStep[];
}

export interface Catalog {
  locationId: string;
  currencyCode: string;
  pricesIncludeTax: boolean;
  menus: {id: string; name: string; sections: {id: string; name: string; productIds: string[]}[]}[];
  products: CatalogProduct[];
  modifierGroups: CatalogModifierGroup[];
  version: string;
}

/** Un produit choisi dans une formule, avec ses options. */
export interface CartComponent {
  stepId: string;
  productId: string;
  variantId: string | null;
  modifierIds: string[];
}

export interface CartLine {
  key: string;
  productId: string;
  variantId: string | null;
  modifierIds: string[];
  quantity: number;
  note: string;
  components?: CartComponent[];
}

export interface QuoteComponent {
  stepName: string;
  name: string;
  variantName: string | null;
  supplement: number;
  modifiers: {id: string; name: string; priceDelta: number}[];
}

export interface QuoteLine {
  productId: string;
  variantId: string | null;
  name: string;
  variantName: string | null;
  quantity: number;
  unitPrice: number;
  modifiers: {id: string; name: string; priceDelta: number}[];
  note: string | null;
  lineTotal: number;
  components?: QuoteComponent[] | null;
}

export interface OnlinePaymentOption {
  provider: string;
  displayName: string;
  methods: string[];
}

export interface Quote {
  currencyCode: string;
  pricesIncludeTax: boolean;
  lines: QuoteLine[];
  subTotal: number;
  discountTotal: number;
  taxTotal: number;
  total: number;
  deliveryFee: number | null;
  coupon: {code: string; isValid: boolean; refusalCode: string | null; label: string | null; discount: number} | null;
  reward: {rewardId: string; name: string; pointCost: number; discount: number} | null;
  minimumOrderAmount: number | null;
  meetsMinimum: boolean;
  canPayAtCounter: boolean;
  canPayOnline: boolean;
  onlinePaymentOptions: OnlinePaymentOption[];
}

export interface PlacedOrder {
  orderId: string;
  number: string;
  status: GuestOrderStatus;
  total: number;
  payment: PaymentMode;
  trackingToken: string;
}

export interface GuestOrder {
  orderId: string;
  number: string;
  locationId: string;
  locationName: string;
  status: GuestOrderStatus;
  payment: PaymentMode;
  currencyCode: string;
  lines: QuoteLine[];
  total: number;
  amountDueAtCounter: number;
  placedAtUtc: string;
  scheduledForUtc: string | null;
  estimatedReadyAtUtc: string | null;
  acceptBeforeUtc: string | null;
  canCancel: boolean;
  paymentState: GuestPaymentState;
  amountPaid: number;
  payBeforeUtc: string | null;
  canPay: boolean;
  paymentOptions: OnlinePaymentOption[];
  mode: ServiceMode;
  discountTotal: number;
  delivery: {zoneName: string; fee: number; status: DeliveryStatus; promisedAtUtc: string} | null;
}

export type DeliveryStatus = 'Pending' | 'Assigned' | 'OutForDelivery' | 'Completed' | 'Failed' | 'Cancelled';

export interface Reviews {
  count: number;
  averageRating: number | null;
  reviews: {id: string; rating: number; comment: string | null; submittedAtUtc: string}[];
}

export class StorefrontError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    message: string,
  ) {
    super(message);
    this.name = 'StorefrontError';
  }
}

/** Les refus que le client peut comprendre et corriger ; les autres gardent un message général. */
const messages: Record<string, string> = {
  'storefront.checkout.unavailable': 'Le restaurant ne prend pas de commandes en ligne pour le moment.',
  'storefront.checkout.paused': 'Les commandes en ligne sont en pause dans cet établissement.',
  'storefront.checkout.closed': 'L’établissement est fermé : choisissez une heure.',
  'storefront.checkout.closed_at_schedule': 'L’établissement est fermé à l’heure choisie.',
  'storefront.checkout.schedule_too_soon': 'Cette heure est trop proche : laissez le temps de préparer la commande.',
  'storefront.checkout.service_mode_unavailable': 'Ce mode n’est pas proposé par l’établissement.',
  'storefront.checkout.delivery_zone_required': 'Choisissez votre quartier de livraison.',
  'storefront.checkout.delivery_zone_unavailable': 'L’établissement ne livre plus ce quartier.',
  'storefront.checkout.delivery_address_required': 'Indiquez votre nom, votre téléphone et votre adresse de livraison.',
  'storefront.checkout.slot_full': 'Ce créneau est complet : choisissez une autre heure.',
  'storefront.checkout.coupon_invalid': 'Ce code promo ne peut pas être utilisé.',
  'storefront.checkout.too_many_pending_payments': 'Plusieurs commandes attendent déjà leur paiement : payez-les ou annulez-les avant d’en passer une autre.',
  'delivery.fulfillment.below_minimum_order': 'Le minimum de commande de votre quartier n’est pas atteint.',
  'delivery.fulfillment.delivery_time_invalid': 'L’heure de livraison doit être dans les 7 prochains jours.',
  'storefront.checkout.payment_mode_not_allowed': 'Ce moyen de paiement n’est pas proposé.',
  'storefront.checkout.online_payment_unavailable': 'Le paiement en ligne n’est pas disponible : payez au comptoir.',
  'storefront.checkout.cart_empty': 'Votre panier est vide.',
  'storefront.checkout.cart_too_large': 'Votre panier est trop grand (50 lignes de 99 articles au plus).',
  'storefront.checkout.below_minimum': 'Le minimum de commande n’est pas atteint.',
  'storefront.checkout.counter_limit_exceeded': 'Ce montant ne peut pas être payé au comptoir : payez en ligne.',
  'storefront.checkout.price_changed': 'Les prix ont changé : vérifiez votre panier avant de valider.',
  'storefront.checkout.customer_required': 'Indiquez votre nom et un téléphone ou un e-mail.',
  'storefront.checkout.reward_unavailable': 'Cette récompense n’est plus disponible.',
  'storefront.checkout.reward_not_affordable': 'Vous n’avez pas assez de points pour cette récompense.',
  'storefront.customer.sign_in_required': 'Votre session a expiré : reconnectez-vous.',
  'storefront.customer.ticket_invalid': 'Ce code a expiré : demandez-en un nouveau.',
  'storefront.customer.address_invalid': 'Indiquez un numéro de téléphone ou une adresse e-mail valide.',
  'storefront.customer.code_required': 'Saisissez le code reçu.',
  'notifications.otp.invalid': 'Code incorrect.',
  'notifications.otp.expired': 'Ce code a expiré : demandez-en un nouveau.',
  'notifications.otp.already_used': 'Ce code a déjà servi : demandez-en un nouveau.',
  'notifications.otp.too_many_attempts': 'Trop d’essais : demandez un nouveau code.',
  'notifications.otp.too_many_requests': 'Trop de codes demandés : patientez un peu avant de réessayer.',
  'notifications.otp.not_sent': 'Le code n’a pas pu être envoyé : essayez un autre moyen.',
  'storefront.order.not_found': 'Cette commande est introuvable.',
  'storefront.order.not_cancellable': 'Le restaurant a déjà accepté la commande : appelez-le pour l’annuler.',
  'storefront.payment.not_expected': 'Cette commande n’attend pas de paiement en ligne.',
  'storefront.payment.return_url_not_allowed': 'La page de retour n’est pas déclarée pour ce site.',
  'storefront.human_check.failed': 'Le contrôle anti-robot n’a pas abouti. Réessayez.',
  'storefront.location.not_found': 'Cet établissement ne prend pas de commandes sur ce site.',
  'storefront.reservation.not_found': 'Ce lien de réservation n’est pas valable.',
  'storefront.reservation.disabled': 'Cet établissement ne prend pas de réservations en ligne : appelez-le.',
  'storefront.reservation.date_invalid': 'Choisissez une date dans la période proposée.',
  'reservations.online.disabled': 'Cet établissement ne prend pas de réservations en ligne : appelez-le.',
  'reservations.online.contact_required': 'Indiquez votre téléphone pour recevoir la confirmation.',
  'reservations.reservation.capacity_insufficient': 'Plus assez de places à cette heure : choisissez-en une autre.',
  'reservations.reservation.location_closed_during_slot': 'L’établissement est fermé à cette heure : choisissez-en une autre.',
  'reservations.reservation.lead_time_not_respected': 'Cette heure est trop proche : choisissez-en une plus tard.',
  'reservations.reservation.notes_too_long': 'Votre message est trop long.',
  'reservations.reservation.beyond_booking_horizon': 'Cette date est trop lointaine.',
  'reservations.reservation.party_size_too_large': 'Groupe trop nombreux pour une réservation en ligne : appelez le restaurant.',
  'reservations.reservation.cancellation_cutoff_passed': 'Il est trop tard pour annuler en ligne : appelez le restaurant.',
  'reservations.guest_contact.name_required': 'Indiquez votre nom.',
  'storefront.review.order_not_completed': 'Vous pourrez donner votre avis une fois la commande remise.',
  'reviews.feedback.order_already_reviewed': 'Vous avez déjà donné votre avis sur cette commande. Merci !',
  'reviews.feedback.comment_too_long': 'Votre commentaire est trop long.',
  'customers.customer.contact_required': 'Indiquez un téléphone ou un e-mail.',
  'common.phone.invalid': 'Le numéro de téléphone n’est pas valide (format +229…).',
  'common.email.invalid': 'L’adresse e-mail n’est pas valide.',
  'orders.plan.monthly_limit_reached': 'Le restaurant ne prend plus de commandes en ligne ce mois-ci.',
  'common.plan.over_limit': 'Cet établissement ne prend pas de commandes ni de réservations en ligne pour le moment.',
  'catalog.product.not_sellable': 'Un produit du panier n’est plus disponible.',
  'catalog.product.variant_unavailable': 'Une variante du panier n’est plus disponible.',
  'payments.payer_phone.invalid': 'Le numéro de paiement n’est pas valide.',
  'payments.payment.too_many_attempts': 'Trop d’essais de paiement : payez au comptoir ou réessayez plus tard.',
  'payments.provider.call_failed': 'Le service de paiement ne répond pas : réessayez.',
};

export const couponRefusal = (code: string | null) => messages[code ?? ''] ?? 'Ce code promo ne peut pas être utilisé.';

export const errorMessage = (error: unknown) =>
  error instanceof StorefrontError && (error.status !== 0 || error.code)
    ? (error.code && (messages[error.code] ?? (error.code.startsWith('promotions.') ? couponRefusal(error.code) : undefined)))
      || (error.status === 401 ? 'Ce site n’est pas autorisé à joindre le restaurant (clé ou origine non déclarée).' : error.status === 429 ? 'Trop de demandes : réessayez dans un instant.' : 'La demande n’a pas abouti. Réessayez.')
    : 'Le restaurant est injoignable pour le moment. Vérifiez votre connexion et réessayez.';

interface CallOptions {
  method?: 'GET' | 'POST';
  body?: unknown;
  idempotencyKey?: string;
  signal?: AbortSignal;
  etag?: string;
  /** Route protégée par le contrôle anti-robot quand il est demandé. */
  humanCheck?: boolean;
}

/** Appel de l'API avec la clé publique. Renvoie null sur un 304 (ETag inchangé). */
export async function storefront<T>(path: string, options: CallOptions = {}): Promise<T | null> {
  const headers: Record<string, string> = {Accept: 'application/json', 'Accept-Language': 'fr', 'X-Storefront-Key': config.publishableKey};
  const customer = readCustomerToken();
  if (customer) headers['X-Customer-Token'] = customer;
  if (options.body !== undefined) headers['Content-Type'] = 'application/json';
  if (options.idempotencyKey) headers['Idempotency-Key'] = options.idempotencyKey;
  if (options.etag) headers['If-None-Match'] = options.etag;
  const human = options.humanCheck
    ? await humanCheckToken().catch(() => {
        throw new StorefrontError(0, 'storefront.human_check.failed', 'human-check');
      })
    : null;
  if (human) headers['X-Human-Check'] = human;
  let response: Response;
  try {
    response = await fetch(`${config.apiUrl}/v1/storefront${path}`, {
      method: options.method ?? 'GET',
      headers,
      body: options.body === undefined ? undefined : JSON.stringify(options.body),
      signal: options.signal,
      cache: 'no-store',
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    throw new StorefrontError(0, undefined, 'unreachable');
  } finally {
    if (human) void resetHumanCheck();
  }

  if (response.status === 304) return null;
  if (!response.ok) {
    let code: string | undefined;
    try {
      const body = await response.json();
      code = typeof body.code === 'string' ? body.code : undefined;
    } catch {}
    if (code === 'storefront.customer.sign_in_required') forgetCustomer();
    throw new StorefrontError(response.status, code, `${response.status}`);
  }

  if (response.status === 204) return undefined as T;
  const result = (await response.json()) as T;
  if (path === '/') setHumanCheckKey((result as Storefront | null)?.app?.humanCheckSiteKey);
  return result;
}

export const newIdempotencyKey = () =>
  typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(16).slice(2)}`;

// ---- Carte : gardée en mémoire avec son ETag.

const catalogs = new Map<string, Catalog>();

export async function loadCatalog(locationId: string, signal?: AbortSignal): Promise<Catalog> {
  const cached = catalogs.get(locationId);
  const fresh = await storefront<Catalog>(`/locations/${encodeURIComponent(locationId)}/catalog`, {signal, etag: cached ? `"${cached.version}"` : undefined});
  if (fresh) {
    catalogs.set(locationId, fresh);
    return fresh;
  }
  return cached!;
}

export const loadReviews = (take: number, signal?: AbortSignal) => storefront<Reviews>(`/reviews?take=${take}`, {signal});

export const sendReview = (orderToken: string, rating: number, comment: string | null) =>
  storefront<{id: string}>('/reviews', {method: 'POST', body: {orderToken, rating, comment}});

/** Le prix d'une ligne d'après la carte (affichage) ; le prix qui fait foi est celui du devis. */
export function linePrice(line: CartLine, catalog: Catalog): number | null {
  const product = catalog.products.find(item => item.id === line.productId);
  if (!product) return null;
  const base = line.variantId ? product.variants.find(variant => variant.id === line.variantId)?.price : product.price;
  if (base === undefined) return null;
  const all = catalog.modifierGroups.flatMap(group => group.modifiers);
  const sum = (ids: string[]) => all.filter(modifier => ids.includes(modifier.id)).reduce((total, modifier) => total + modifier.priceDelta, 0);
  const components = (line.components ?? []).reduce((total, component) => {
    const choice = product.formulaSteps?.find(step => step.id === component.stepId)?.choices
      .find(item => item.productId === component.productId && item.variantId === component.variantId);
    return total + (choice?.supplement ?? 0) + sum(component.modifierIds);
  }, 0);
  return (base + sum(line.modifierIds) + components) * line.quantity;
}

export const toPayload = (line: CartLine) => ({
  productId: line.productId,
  variantId: line.variantId,
  modifierIds: line.modifierIds,
  quantity: line.quantity,
  note: line.note.trim() || null,
  components: line.components?.length ? line.components : null,
});

/** « Poulet braisé (Grande) + Piment ». */
export const componentLabel = (component: QuoteComponent) =>
  `${component.name}${component.variantName ? ` (${component.variantName})` : ''}${component.modifiers.length ? ` + ${component.modifiers.map(modifier => modifier.name).join(', ')}` : ''}`;

// ---- Paiement en ligne.

export const startOrderPayment = (token: string, body: {provider: string; method: string | null; payerPhone: string | null; returnUrl: string}) =>
  storefront<{paymentId: string; checkoutUrl: string | null}>(`/orders/${encodeURIComponent(token)}/payments`, {
    method: 'POST', idempotencyKey: newIdempotencyKey(), body,
  });

// ---- Compte client : connexion par code, jeton gardé pour l'onglet (sessionStorage).

export type SignInChannel = 'Sms' | 'WhatsApp' | 'Email';

export interface CustomerAccount {
  name: string;
  email: string | null;
  phone: string | null;
  currencyCode: string;
  loyalty: {
    enabled: boolean;
    balance: number;
    pointsPerCurrencyUnit: number;
    rewards: {id: string; name: string; pointCost: number; discount: number; affordable: boolean}[];
  };
}

export interface CustomerOrder {
  orderId: string;
  number: string;
  locationName: string;
  mode: ServiceMode;
  status: GuestOrderStatus;
  currencyCode: string;
  total: number;
  createdAtUtc: string;
  trackingToken: string | null;
}

export interface CodeSent {
  ticket: string;
  expiresAtUtc: string;
  channel: SignInChannel;
  destination: string;
}

interface SignInResult {
  status: 'SignedIn' | 'NameRequired';
  token: string | null;
  expiresAtUtc: string | null;
  ticket: string | null;
}

const customerKey = 'maquis.customer';
const customerListeners = new Set<() => void>();

export function readCustomerToken(): string | null {
  if (typeof window === 'undefined') return null;
  try {
    const raw = window.sessionStorage.getItem(customerKey);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as {token: string; expiresAtUtc: string};
    return Date.parse(parsed.expiresAtUtc) > Date.now() ? parsed.token : null;
  } catch {
    return null;
  }
}

function setCustomer(value: {token: string; expiresAtUtc: string} | null) {
  try {
    if (value) window.sessionStorage.setItem(customerKey, JSON.stringify(value));
    else window.sessionStorage.removeItem(customerKey);
  } catch {}
  customerListeners.forEach(listener => listener());
}

export const forgetCustomer = () => setCustomer(null);

export function onCustomerChange(listener: () => void) {
  customerListeners.add(listener);
  return () => {
    customerListeners.delete(listener);
  };
}

export const requestSignInCode = (channel: SignInChannel, address: string) =>
  storefront<CodeSent>('/customer/code', {method: 'POST', body: {channel, address, language: 'fr'}, humanCheck: true});

export async function verifySignInCode(ticket: string, code: string | null, name: string | null, marketingConsent: boolean) {
  const result = await storefront<SignInResult>('/customer/code/verify', {method: 'POST', body: {ticket, code, name, marketingConsent, language: 'fr'}});
  if (result?.status === 'SignedIn' && result.token && result.expiresAtUtc) setCustomer({token: result.token, expiresAtUtc: result.expiresAtUtc});
  return result;
}

export const loadAccount = (signal?: AbortSignal) => storefront<CustomerAccount>('/customer', {signal});

export const loadCustomerOrders = (signal?: AbortSignal) => storefront<CustomerOrder[]>('/customer/orders?take=20', {signal});

export async function signOut() {
  try {
    await storefront('/customer/sign-out', {method: 'POST'});
  } finally {
    forgetCustomer();
  }
}

// ---- Réservations.

export interface BookingDepositRule {
  amount: number;
  currencyCode: string;
  perGuest: boolean;
  minPartySize: number;
  paymentWindowMinutes: number;
}

export interface BookingTimes {
  timeZoneId: string;
  date: string;
  durationMinutes: number;
  maxPartySize: number;
  times: {startAtUtc: string; isAvailable: boolean}[];
  deposit?: BookingDepositRule | null;
}

/** L'acompte demandé pour ce groupe ; null sans acompte. */
export const depositFor = (rule: BookingDepositRule | null | undefined, partySize: number) =>
  !rule || partySize < rule.minPartySize ? null : rule.perGuest ? rule.amount * partySize : rule.amount;

export type GuestDepositStatus = 'AwaitingPayment' | 'Paid' | 'Applied' | 'RefundRequested' | 'Refunded' | 'Forfeited' | 'Waived';
export type BookingStatus = 'Pending' | 'Confirmed' | 'Seated' | 'Completed' | 'Cancelled' | 'NoShow';

export interface GuestBooking {
  status: BookingStatus;
  locationId: string;
  locationName: string;
  startAtUtc: string;
  durationMinutes: number;
  partySize: number;
  guestName: string;
  canCancel: boolean;
  cancelBeforeUtc: string | null;
  deposit?: {
    amount: number;
    currencyCode: string;
    status: GuestDepositStatus;
    dueBeforeUtc: string | null;
    canPay: boolean;
    hasPendingPayment: boolean;
    paymentOptions: OnlinePaymentOption[];
  } | null;
}

export const loadBookingTimes = (locationId: string, date: string, partySize: number, signal?: AbortSignal) =>
  storefront<BookingTimes>(`/locations/${encodeURIComponent(locationId)}/booking?date=${date}&partySize=${partySize}`, {signal});

export const bookTable = (body: {locationId: string; startAt: string; partySize: number; name: string; phone: string; email: string | null; notes: string | null}) =>
  storefront<{token: string; startAtUtc: string; partySize: number}>('/reservations', {method: 'POST', body, idempotencyKey: newIdempotencyKey(), humanCheck: true});

export const loadBooking = (token: string, signal?: AbortSignal) => storefront<GuestBooking>(`/reservations/${encodeURIComponent(token)}`, {signal});

export const cancelBooking = (token: string) => storefront<void>(`/reservations/${encodeURIComponent(token)}/cancel`, {method: 'POST'});

/** Paie l'acompte ; renvoie la page du prestataire (null : demande envoyée sur le téléphone). */
export const payDeposit = (token: string, provider: string, method: string, returnUrl: string) =>
  storefront<{paymentId: string; checkoutUrl: string | null}>(`/reservations/${encodeURIComponent(token)}/deposit/pay`, {
    method: 'POST', idempotencyKey: newIdempotencyKey(), body: {provider, method, returnUrl},
  });
