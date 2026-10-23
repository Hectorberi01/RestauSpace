import {humanCheckToken, resetHumanCheck, setHumanCheckKey} from './human-check';
import {site} from '@/content/site';
import {currentLang, intlLocale, tr, translated} from '@/lib/i18n';

// Client de l'API de vente en ligne (/v1/storefront), appelée depuis le navigateur avec la clé publique de
// l'application (pk_live_…). Cette clé n'est pas un secret : elle ne fonctionne que depuis les origines déclarées dans
// la console du restaurant. Le restaurant vient de la clé, jamais d'une valeur envoyée ici.

export const storefrontKey = process.env.NEXT_PUBLIC_STOREFRONT_KEY ?? '';

/** La borne remplace la clé du site par celle de son application, et ajoute son jeton d'appareil (X-Kiosk-Device). */
const credentials: {key: string; device: string | null} = {key: storefrontKey, device: null};

export function setKioskCredentials(key: string, device: string | null) {
  credentials.key = key;
  credentials.device = device;
}

export type ServiceMode = 'DineIn' | 'Takeaway' | 'Delivery';
export type PaymentMode = 'Counter' | 'Online';
export type GuestOrderStatus = 'Received' | 'Accepted' | 'Preparing' | 'Ready' | 'Completed' | 'Cancelled';
export type GuestPaymentState = 'AtCounter' | 'AwaitingPayment' | 'Processing' | 'Paid' | 'Refunded';

export interface StorefrontLocation {
  id: string;
  name: string;
  street: string;
  city: string;
  timeZoneId: string;
  isOpenNow: boolean;
  nextOpeningUtc: string | null;
  isPaused: boolean;
  acceptsOrdersNow: boolean;
  serviceModes: ServiceMode[];
  preparationMinutes: number;
  minimumOrderAmount: number | null;
  payment: {payOnline: boolean; payAtCounter: boolean; counterLimit: number | null};
  delivery: StorefrontDelivery | null;
  slotCapacity: number | null;
}

export interface DeliveryZone {
  id: string;
  name: string;
  fee: number;
  minimumOrder: number;
  estimatedMinutes: number;
}

export interface StorefrontDelivery {
  leadMinutes: number;
  zones: DeliveryZone[];
}

export interface Slot {
  startUtc: string;
  isAvailable: boolean;
}

export interface Slots {
  locationId: string;
  mode: ServiceMode;
  timeZoneId: string;
  slotMinutes: number;
  capacity: number | null;
  slots: Slot[];
}

export interface QuoteCoupon {
  code: string;
  isValid: boolean;
  refusalCode: string | null;
  label: string | null;
  discount: number;
}

export interface Storefront {
  restaurant: {name: string; logoUrl: string | null; primaryColor: string | null; currencyCode: string; pricesIncludeTax: boolean};
  app: {id: string; name: string; kind: string; humanCheckSiteKey?: string | null};
  orderingEnabled: boolean;
  locations: StorefrontLocation[];
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

export interface CatalogVariant {
  id: string;
  name: string;
  price: number;
  isAvailable: boolean;
}

export interface CatalogProduct {
  id: string;
  name: string;
  description: string | null;
  price: number;
  isAvailable: boolean;
  variants: CatalogVariant[];
  modifierGroupIds: string[];
  allergens: string[];
  images: string[];
  /** Miniature de chaque photo, dans le même ordre (null sans miniature) : pour les listes. */
  thumbnails?: (string | null)[];
  /** Formule (menu composé) : les étapes à composer ; vide pour un produit ordinaire. */
  formulaSteps?: CatalogFormulaStep[];
}

/** Un produit proposé à une étape de formule ; le supplément s'ajoute au prix de la formule. */
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

/** Un produit choisi dans une formule, avec ses propres options. */
export interface CartComponent {
  stepId: string;
  productId: string;
  variantId: string | null;
  modifierIds: string[];
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
  productId: string;
  name: string;
  variantName: string | null;
  supplement: number;
  modifiers: {id: string; name: string; priceDelta: number}[];
}

/** « Poulet braisé (Grande) + Sauce arachide ». */
export const componentLabel = (component: QuoteComponent) =>
  `${component.name}${component.variantName ? ` (${component.variantName})` : ''}${component.modifiers.length ? ` + ${component.modifiers.map(modifier => modifier.name).join(', ')}` : ''}`;

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
  deliveryZoneId: string | null;
  coupon: QuoteCoupon | null;
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
  currencyCode: string;
  total: number;
  payment: PaymentMode;
  acceptBeforeUtc: string | null;
  payBeforeUtc: string | null;
  trackingToken: string;
  ticketPrinted?: boolean;
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
  delivery: GuestDelivery | null;
}

export type DeliveryStatus = 'Pending' | 'Assigned' | 'OutForDelivery' | 'Completed' | 'Failed' | 'Cancelled';

export interface GuestDelivery {
  zoneName: string;
  fee: number;
  status: DeliveryStatus;
  promisedAtUtc: string;
}

export const deliveryStatusLabel: Record<DeliveryStatus, string> = translated({
  Pending: 'En préparation',
  Assigned: 'Livreur désigné',
  OutForDelivery: 'En route',
  Completed: 'Livrée',
  Failed: 'Livraison manquée : le restaurant va vous appeler',
  Cancelled: 'Livraison annulée',
});

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
const messages: Record<string, string> = translated({
  'storefront.checkout.unavailable': 'Le restaurant ne prend pas de commandes en ligne pour le moment.',
  'storefront.table.not_found': 'Ce QR code de table n’est plus valable. Demandez au personnel de vous aider.',
  'storefront.table.dine_in_only': 'Une commande passée à table est servie sur place.',
  'storefront.table.out_of_service': 'Cette table n’est pas en service pour le moment.',
  'storefront.table.not_scheduled': 'Une commande passée à table est servie tout de suite.',
  'storefront.checkout.paused': 'Les commandes en ligne sont en pause dans cet établissement.',
  'storefront.checkout.closed': 'L’établissement est fermé : choisissez une heure de retrait.',
  'storefront.checkout.closed_at_schedule': 'L’établissement est fermé à l’heure choisie.',
  'storefront.checkout.schedule_too_soon': 'Cette heure est trop proche : laissez le temps de préparer la commande.',
  'storefront.checkout.service_mode_unavailable': 'Ce mode n’est pas proposé par l’établissement.',
  'storefront.checkout.delivery_zone_required': 'Choisissez votre zone de livraison.',
  'storefront.checkout.delivery_zone_unavailable': 'L’établissement ne livre plus cette zone.',
  'storefront.checkout.delivery_address_required': 'Indiquez votre nom, votre téléphone et votre adresse de livraison.',
  'storefront.checkout.slot_full': 'Ce créneau est complet : choisissez une autre heure.',
  'storefront.checkout.coupon_invalid': 'Ce code promo ne peut pas être utilisé.',
  'delivery.fulfillment.below_minimum_order': 'Le minimum de commande de votre zone n’est pas atteint.',
  'delivery.fulfillment.delivery_time_invalid': 'L’heure de livraison doit être dans les 7 prochains jours.',
  'storefront.checkout.payment_mode_not_allowed': 'Ce moyen de paiement n’est pas proposé.',
  'storefront.checkout.online_payment_unavailable': 'Le paiement en ligne n’est pas disponible : payez au comptoir.',
  'storefront.checkout.cart_empty': 'Votre panier est vide.',
  'storefront.checkout.cart_too_large': 'Votre panier est trop grand (50 lignes de 99 articles au plus).',
  'storefront.checkout.below_minimum': 'Le minimum de commande n’est pas atteint.',
  'storefront.checkout.counter_limit_exceeded': 'Ce montant ne peut pas être payé au comptoir : payez en ligne.',
  'storefront.checkout.price_changed': 'Les prix ont changé : vérifiez votre panier avant de valider.',
  'storefront.checkout.customer_required': 'Indiquez votre nom et un téléphone ou un e-mail.',
  'storefront.kiosk.device_required': 'Cette borne n’est pas appairée.',
  'storefront.kiosk.pairing_invalid': 'Le code a expiré : un nouveau code s’affiche.',
  'storefront.customer.sign_in_required': 'Votre session a expiré : reconnectez-vous.',
  'storefront.customer.ticket_invalid': 'Ce code a expiré : demandez-en un nouveau.',
  'storefront.customer.address_invalid': 'Indiquez un numéro de téléphone (SMS, WhatsApp) ou une adresse e-mail.',
  'storefront.customer.code_required': 'Saisissez le code reçu.',
  'storefront.customer.kiosk_not_allowed': 'La connexion n’est pas possible sur une borne.',
  'storefront.checkout.reward_unavailable': 'Cette récompense n’est plus disponible.',
  'storefront.checkout.reward_not_affordable': 'Vous n’avez pas assez de points pour cette récompense.',
  'notifications.otp.invalid': 'Code incorrect.',
  'notifications.otp.expired': 'Ce code a expiré : demandez-en un nouveau.',
  'notifications.otp.already_used': 'Ce code a déjà servi : demandez-en un nouveau.',
  'notifications.otp.too_many_attempts': 'Trop d’essais : demandez un nouveau code.',
  'notifications.otp.too_many_requests': 'Trop de codes demandés : patientez un peu avant de réessayer.',
  'notifications.otp.not_sent': 'Le code n’a pas pu être envoyé : essayez un autre moyen.',
  'storefront.order.not_found': 'Cette commande est introuvable.',
  'storefront.receipt.not_found': 'Ce lien de reçu n’est pas valable.',
  'storefront.human_check.failed': 'Le contrôle anti-robot n’a pas abouti. Réessayez.',
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
  'storefront.survey.not_found': 'Ce questionnaire n’existe pas ou plus.',
  'storefront.survey.order_not_completed': 'Vous pourrez répondre une fois la commande remise.',
  'reviews.survey_response.not_open': 'Ce questionnaire est clos : merci de votre intérêt.',
  'reviews.survey_response.already_answered': 'Vous avez déjà répondu pour cette commande. Merci !',
  'reviews.survey_response.required_missing': 'Répondez aux questions marquées d’un astérisque.',
  'reviews.survey_response.empty': 'Répondez à au moins une question.',
  'reviews.survey_response.text_too_long': 'Une réponse fait 1 000 caractères au plus.',
  'storefront.order.not_cancellable': 'Le restaurant a déjà accepté la commande : contactez-le pour l’annuler.',
  'storefront.payment.not_expected': 'Cette commande n’attend pas de paiement en ligne.',
  'storefront.payment.return_url_not_allowed': 'La page de retour n’est pas déclarée pour ce site.',
  'customers.customer.contact_required': 'Indiquez un téléphone ou un e-mail.',
  'common.phone.invalid': 'Le numéro de téléphone n’est pas valide.',
  'common.email.invalid': 'L’adresse e-mail n’est pas valide.',
  'orders.plan.monthly_limit_reached': 'Le restaurant ne prend plus de commandes en ligne ce mois-ci.',
  'catalog.product.not_sellable': 'Un produit du panier n’est plus disponible.',
  'catalog.product.variant_unavailable': 'Une variante du panier n’est plus disponible.',
});

/** Pourquoi un code promo est refusé, en clair. */
export const couponRefusal = (code: string | null) => {
  if (!code) return tr('Ce code promo ne peut pas être utilisé.');
  if (code === 'promotions.coupon.not_found' || code.startsWith('promotions.coupon_code.')) return tr('Ce code promo n’existe pas.');
  if (code === 'promotions.coupon.exhausted' || code === 'promotions.promotion.usage_limit_reached') return tr('Ce code promo a déjà été utilisé autant de fois que prévu.');
  if (code === 'promotions.discount.minimum_order_amount_not_reached') return tr('Le montant minimum de cette offre n’est pas atteint.');
  if (code === 'promotions.discount.order_type_not_eligible') return tr('Cette offre ne vaut pas pour ce mode (sur place, à emporter ou livraison).');
  if (code === 'promotions.discount.location_not_eligible') return tr('Cette offre ne vaut pas dans cet établissement.');
  if (code === 'promotions.discount.no_eligible_amount') return tr('Aucun produit de votre panier n’est concerné par cette offre.');
  if (code === 'promotions.discount.outside_validity_period' || code === 'promotions.promotion.expired' || code === 'promotions.promotion.not_active'
    || code === 'promotions.coupon.disabled' || code === 'promotions.coupon.promotion_expired') return tr('Cette offre n’est plus valable.');
  return tr('Ce code promo ne peut pas être utilisé.');
};

export const errorMessage = (error: unknown) =>
  error instanceof StorefrontError
    ? (error.code && (messages[error.code] ?? (error.code.startsWith('promotions.') ? couponRefusal(error.code) : undefined))) || (error.status === 429 ? tr('Trop de demandes : réessayez dans un instant.') : tr('La demande n’a pas abouti. Réessayez.'))
    : tr('Le service est injoignable. Vérifiez votre connexion.');

interface CallOptions {
  method?: 'GET' | 'POST';
  body?: unknown;
  idempotencyKey?: string;
  signal?: AbortSignal;
  etag?: string;
  /** Route protégée par le contrôle anti-robot quand l'application l'exige. */
  humanCheck?: boolean;
}

/** Appel de l'API avec la clé publique. Renvoie null sur un 304 (ETag inchangé). */
export async function storefront<T>(path: string, options: CallOptions = {}): Promise<T | null> {
  const headers: Record<string, string> = {Accept: 'application/json', 'Accept-Language': currentLang(), 'X-Storefront-Key': credentials.key};
  if (credentials.device) headers['X-Kiosk-Device'] = credentials.device;
  const customer = credentials.device ? null : readCustomerToken();
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
    response = await fetch(`${site.apiUrl}/v1/storefront${path}`, {
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
  if (path === '/' || path === '') setHumanCheckKey((result as Storefront | null)?.app?.humanCheckSiteKey);
  return result;
}

/** Catalogue gardé en mémoire avec son ETag : un rechargement inchangé ne retransfère rien. */
const catalogs = new Map<string, Catalog>();

export async function loadCatalog(locationId: string, signal?: AbortSignal): Promise<Catalog> {
  const cached = catalogs.get(locationId);
  const fresh = await storefront<Catalog>(`/locations/${locationId}/catalog`, {signal, etag: cached ? `"${cached.version}"` : undefined});
  if (fresh) {
    catalogs.set(locationId, fresh);
    return fresh;
  }

  return cached!;
}

export const newIdempotencyKey = () =>
  typeof crypto !== 'undefined' && 'randomUUID' in crypto ? crypto.randomUUID() : `${Date.now()}-${Math.random().toString(16).slice(2)}`;

export const money = (value: number, currency: string) => {
  try {
    return new Intl.NumberFormat('fr-FR', {style: 'currency', currency}).format(value);
  } catch {
    return `${value} ${currency}`;
  }
};

/** Heure « 12:30 », dans le fuseau de l'établissement s'il est fourni (sinon celui du navigateur). */
export const clock = (utc: string, timeZone?: string) => {
  try {
    return new Intl.DateTimeFormat(intlLocale(), {hour: '2-digit', minute: '2-digit', timeZone}).format(new Date(utc));
  } catch {
    return new Intl.DateTimeFormat(intlLocale(), {hour: '2-digit', minute: '2-digit'}).format(new Date(utc));
  }
};

export const allergenLabel: Record<string, string> = translated({
  gluten: 'Gluten',
  crustaceans: 'Crustacés',
  eggs: 'Œufs',
  fish: 'Poissons',
  peanuts: 'Arachides',
  soybeans: 'Soja',
  milk: 'Lait',
  nuts: 'Fruits à coque',
  celery: 'Céleri',
  mustard: 'Moutarde',
  sesame: 'Sésame',
  sulphites: 'Sulfites',
  lupin: 'Lupin',
  molluscs: 'Mollusques',
});

export const modeLabel: Record<ServiceMode, string> = translated({DineIn: 'Sur place', Takeaway: 'À emporter', Delivery: 'Livraison'});

export const methodLabel: Record<string, string> = translated({
  Card: 'Carte bancaire',
  MobileMoney: 'Mobile money',
  ApplePay: 'Apple Pay',
  GooglePay: 'Google Pay',
  PayPal: 'PayPal',
  BankTransfer: 'Virement',
});

const HEX = /^#[0-9a-f]{6}$/i;

/** Couleur de l'enseigne, seulement si c'est une couleur hexadécimale (elle vient de l'API et finit dans du style). */
export const accentOf = (color: string | null | undefined) => (color && HEX.test(color) ? color : '#5b3df5');

// ---- Panier : gardé dans le navigateur, un par établissement.

const cartKey = (locationId: string) => `restaurantos.cart.${locationId}`;

export function readCart(locationId: string): CartLine[] {
  try {
    const raw = window.localStorage.getItem(cartKey(locationId));
    const parsed = raw ? (JSON.parse(raw) as CartLine[]) : [];
    return Array.isArray(parsed) ? parsed.filter(line => typeof line.productId === 'string' && line.quantity > 0) : [];
  } catch {
    return [];
  }
}

export function writeCart(locationId: string, lines: CartLine[]) {
  try {
    if (lines.length === 0) window.localStorage.removeItem(cartKey(locationId));
    else window.localStorage.setItem(cartKey(locationId), JSON.stringify(lines));
  } catch {}
}

/** Les commandes passées depuis ce navigateur (jetons de suivi), pour les retrouver. */
const ordersKey = 'restaurantos.orders';

export function rememberOrder(token: string, number: string) {
  try {
    const list = JSON.parse(window.localStorage.getItem(ordersKey) ?? '[]') as {token: string; number: string; at: string}[];
    const next = [{token, number, at: new Date().toISOString()}, ...list.filter(item => item.token !== token)].slice(0, 10);
    window.localStorage.setItem(ordersKey, JSON.stringify(next));
  } catch {}
}

export const toPayload = (line: CartLine) => ({
  productId: line.productId,
  variantId: line.variantId,
  modifierIds: line.modifierIds,
  quantity: line.quantity,
  note: line.note.trim() || null,
  components: line.components?.length ? line.components : null,
});

/** Le prix d'une ligne du panier d'après le catalogue (affichage) ; le prix qui fait foi est celui du devis. */
export function linePrice(line: CartLine, catalog: Catalog): number | null {
  const product = catalog.products.find(item => item.id === line.productId);
  if (!product) return null;
  const base = line.variantId ? product.variants.find(variant => variant.id === line.variantId)?.price : product.price;
  if (base === undefined) return null;
  const all = catalog.modifierGroups.flatMap(group => group.modifiers);
  const modifiers = all.filter(modifier => line.modifierIds.includes(modifier.id));
  const components = (line.components ?? []).reduce((sum, component) => {
    const choice = product.formulaSteps?.find(step => step.id === component.stepId)?.choices
      .find(item => item.productId === component.productId && item.variantId === component.variantId);
    return sum + (choice?.supplement ?? 0) + all.filter(modifier => component.modifierIds.includes(modifier.id)).reduce((total, modifier) => total + modifier.priceDelta, 0);
  }, 0);
  return (base + modifiers.reduce((sum, modifier) => sum + modifier.priceDelta, 0) + components) * line.quantity;
}

// ---- Compte client : connexion par code, jeton de session gardé dans le navigateur.

export type SignInChannel = 'Sms' | 'WhatsApp' | 'Email';

export interface CustomerReward {
  id: string;
  name: string;
  pointCost: number;
  discount: number;
  affordable: boolean;
}

export interface CustomerAccount {
  name: string;
  email: string | null;
  phone: string | null;
  currencyCode: string;
  loyalty: {enabled: boolean; balance: number; pointsPerCurrencyUnit: number; rewards: CustomerReward[]};
}

export interface CustomerOrder {
  orderId: string;
  number: string;
  locationId: string;
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

export interface SignInResult {
  status: 'SignedIn' | 'NameRequired';
  token: string | null;
  expiresAtUtc: string | null;
  ticket: string | null;
  account: CustomerAccount | null;
}

// Jeton du client en sessionStorage, comme la console : limité à l'onglet (le retour du paiement externe s'y fait).
const customerKey = 'restaurantos.customer';
const customerListeners = new Set<() => void>();

export function readCustomerToken(): string | null {
  if (typeof window === 'undefined') return null;
  try {
    window.localStorage.removeItem(customerKey); // ancien emplacement
    const raw = window.sessionStorage.getItem(customerKey);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as {token: string; expiresAtUtc: string};
    return Date.parse(parsed.expiresAtUtc) > Date.now() ? parsed.token : null;
  } catch {
    return null;
  }
}

function rememberCustomer(token: string, expiresAtUtc: string) {
  try {
    window.sessionStorage.setItem(customerKey, JSON.stringify({token, expiresAtUtc}));
  } catch {}
  customerListeners.forEach(listener => listener());
}

export function forgetCustomer() {
  try {
    window.sessionStorage.removeItem(customerKey);
  } catch {}
  customerListeners.forEach(listener => listener());
}

/** Prévient quand le client se connecte ou se déconnecte (dans cet onglet). */
export function onCustomerChange(listener: () => void) {
  customerListeners.add(listener);
  return () => {
    customerListeners.delete(listener);
  };
}

export const requestSignInCode = (channel: SignInChannel, address: string) =>
  storefront<CodeSent>('/customer/code', {method: 'POST', body: {channel, address, language: currentLang()}, humanCheck: true});

export async function verifySignInCode(ticket: string, code: string | null, name?: string, marketingConsent = false) {
  const result = await storefront<SignInResult>('/customer/code/verify', {method: 'POST', body: {ticket, code, name: name || null, marketingConsent, language: currentLang()}});
  if (result?.status === 'SignedIn' && result.token && result.expiresAtUtc) rememberCustomer(result.token, result.expiresAtUtc);
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

export type SurveyQuestionType = 'Rating' | 'FreeText' | 'SingleChoice' | 'MultipleChoice';

export interface SurveyQuestion {
  id: string;
  order: number;
  text: string;
  type: SurveyQuestionType;
  isRequired: boolean;
  choices: string[];
}

export interface Survey {
  id: string;
  title: string;
  isOpen: boolean;
  questions: SurveyQuestion[];
}

export interface SurveyAnswer {
  questionId: string;
  rating?: number;
  text?: string;
  choices?: string[];
}

export const loadSurvey = (id: string | null, signal?: AbortSignal) =>
  storefront<Survey>(id ? `/surveys/${encodeURIComponent(id)}` : '/surveys/active', {signal});

export const submitSurvey = (id: string, answers: SurveyAnswer[], orderToken: string | null, locationId: string | null) =>
  storefront<void>(`/surveys/${encodeURIComponent(id)}/responses`, {method: 'POST', body: {answers, orderToken, locationId}});

const ANSWERED = 'restaurantos.surveys.answered';

/** Questionnaires déjà remplis sur ce navigateur (pour ne plus les proposer). */
export function answeredSurveys(): string[] {
  try {
    const value = JSON.parse(window.localStorage.getItem(ANSWERED) ?? '[]');
    return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [];
  } catch {
    return [];
  }
}

export function rememberAnswered(key: string) {
  try {
    window.localStorage.setItem(ANSWERED, JSON.stringify([...answeredSurveys().filter(item => item !== key), key].slice(-50)));
  } catch {}
}

export interface BookingTimes {
  locationId: string;
  timeZoneId: string;
  date: string;
  durationMinutes: number;
  maxPartySize: number;
  cancellationCutoffMinutes: number;
  times: {startAtUtc: string; isAvailable: boolean}[];
  /** Un créneau complet propose l'inscription en liste d'attente. */
  acceptsWaitlist?: boolean;
  /** Règle d'acompte de l'établissement ; null sans acompte. */
  deposit?: BookingDepositRule | null;
}

export interface BookingDepositRule {
  amount: number;
  currencyCode: string;
  perGuest: boolean;
  minPartySize: number;
  paymentWindowMinutes: number;
}

/** L'acompte demandé pour ce groupe ; null sans acompte. */
export const depositFor = (rule: BookingDepositRule | null | undefined, partySize: number) =>
  !rule || partySize < rule.minPartySize ? null : rule.perGuest ? rule.amount * partySize : rule.amount;

export type GuestDepositStatus = 'AwaitingPayment' | 'Paid' | 'Applied' | 'RefundRequested' | 'Refunded' | 'Forfeited' | 'Waived';

export interface GuestDeposit {
  amount: number;
  currencyCode: string;
  status: GuestDepositStatus;
  dueBeforeUtc: string | null;
  canPay: boolean;
  hasPendingPayment: boolean;
  paymentOptions: OnlinePaymentOption[];
}

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
  deposit?: GuestDeposit | null;
}

export const loadBookingTimes = (locationId: string, date: string, partySize: number, signal?: AbortSignal) =>
  storefront<BookingTimes>(`/locations/${locationId}/booking?date=${date}&partySize=${partySize}`, {signal});

export const bookTable = (body: {locationId: string; startAt: string; partySize: number; name: string; phone: string; email: string | null; notes: string | null}) =>
  storefront<{token: string; startAtUtc: string; partySize: number; deposit: GuestDeposit | null}>('/reservations', {method: 'POST', body, idempotencyKey: newIdempotencyKey(), humanCheck: true});

/** Paie l'acompte de la réservation ; renvoie la page du prestataire (null quand la demande part sur le téléphone). */
export const payDeposit = (token: string, provider: string, method: string, returnUrl: string) =>
  storefront<{paymentId: string; checkoutUrl: string | null}>(`/reservations/${encodeURIComponent(token)}/deposit/pay`, {
    method: 'POST', idempotencyKey: newIdempotencyKey(), body: {provider, method, returnUrl},
  });

export type WaitlistStatus = 'Waiting' | 'Offered' | 'Booked' | 'Expired' | 'Cancelled';

/** Une inscription en liste d'attente, par son lien. */
export interface GuestWaitlist {
  status: WaitlistStatus;
  locationId: string;
  locationName: string;
  desiredStartUtc: string;
  flexibilityMinutes: number;
  partySize: number;
  guestName: string;
  offeredStartUtc: string | null;
  offerExpiresAtUtc: string | null;
  reservationToken: string | null;
}

export const joinWaitlist = (body: {locationId: string; desiredStart: string; partySize: number; flexibilityMinutes: number; name: string; phone: string; email: string | null}) =>
  storefront<{token: string; desiredStartUtc: string; partySize: number}>('/waitlist', {method: 'POST', body, idempotencyKey: newIdempotencyKey(), humanCheck: true});

export const loadWaitlist = (token: string, signal?: AbortSignal) => storefront<GuestWaitlist>(`/waitlist/${encodeURIComponent(token)}`, {signal});

export const acceptWaitlistOffer = (token: string) =>
  storefront<{token: string; startAtUtc: string; partySize: number; deposit: GuestDeposit | null}>(`/waitlist/${encodeURIComponent(token)}/accept`, {
    method: 'POST', idempotencyKey: newIdempotencyKey(),
  });

export const cancelWaitlist = (token: string) => storefront<void>(`/waitlist/${encodeURIComponent(token)}/cancel`, {method: 'POST'});

export const loadBooking = (token: string, signal?: AbortSignal) => storefront<GuestBooking>(`/reservations/${encodeURIComponent(token)}`, {signal});

export const cancelBooking = (token: string) => storefront<void>(`/reservations/${encodeURIComponent(token)}/cancel`, {method: 'POST'});

const BOOKINGS = 'restaurantos.bookings';

/** Réservations faites sur ce navigateur (liens de gestion), les plus récentes d'abord. */
export function rememberedBookings(): string[] {
  try {
    const value = JSON.parse(window.localStorage.getItem(BOOKINGS) ?? '[]');
    return Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : [];
  } catch {
    return [];
  }
}

export function rememberBooking(token: string) {
  try {
    window.localStorage.setItem(BOOKINGS, JSON.stringify([token, ...rememberedBookings().filter(item => item !== token)].slice(0, 10)));
  } catch {}
}

/** Date et heure dans le fuseau de l'établissement. */
export const localTime = (utc: string, timeZone: string) =>
  new Intl.DateTimeFormat(intlLocale(), {hour: '2-digit', minute: '2-digit', timeZone}).format(new Date(utc));

export const localDay = (utc: string, timeZone: string) =>
  new Intl.DateTimeFormat(intlLocale(), {weekday: 'long', day: 'numeric', month: 'long', timeZone}).format(new Date(utc));

export interface ReceiptLine {
  quantity: number;
  name: string;
  details: string | null;
  amount: string;
}

export interface ReceiptAmount {
  label: string;
  amount: string;
}

/** Le reçu d'une commande, ouvert par son lien : textes et montants déjà formatés par le restaurant. */
export interface PublicReceipt {
  restaurantName: string;
  locationName: string;
  address: string | null;
  orderNumber: string;
  serviceMode: string;
  date: string;
  isCancelled: boolean;
  lines: ReceiptLine[];
  totals: ReceiptAmount[];
  total: string;
  payments: ReceiptAmount[];
  amountDue: string | null;
  footer: string[];
  qrCode: string | null;
  pricesIncludeTax: boolean;
}

export const loadReceipt = (token: string, signal?: AbortSignal) => storefront<PublicReceipt>(`/receipts/${encodeURIComponent(token)}`, {signal});
