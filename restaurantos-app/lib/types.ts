export type StaffStatus = 'Invited' | 'Active' | 'Suspended' | 'Left';

export interface Membership {
  tenantId: string;
  tenantName: string;
  staffMemberId: string;
  status: StaffStatus;
}

export interface Tenant {
  id: string;
  name: string;
  status: string;
  canAcceptBusinessWrites: boolean;
  defaultCurrencyCode: string;
  timeZoneId: string;
  pricesIncludeTax: boolean;
  branding: {displayName: string; logoUrl: string | null; primaryColor: string | null};
  publicProfile?: PublicProfile | null;
}

export type SocialNetwork = 'Website' | 'Facebook' | 'Instagram' | 'TikTok' | 'X' | 'YouTube' | 'GoogleMaps';

export interface PublicProfile {
  tagline: string | null;
  description: string | null;
  coverImageUrl: string | null;
  links: {network: SocialNetwork; url: string}[];
}

export interface MyPermissions {
  staffMemberId: string;
  status: StaffStatus;
  tenantWideRoleId: string | null;
  permissions: string[];
  hasPin?: boolean;
}

export type LocationStatus = 'Draft' | 'Active' | 'TemporarilyClosed' | 'Archived';

export interface LocationSummary {
  id: string;
  name: string;
  status: LocationStatus;
  city: string;
  countryCode: string;
  seats: number;
  /** Au-delà de la limite du plan : lisible, sans nouvelles commandes ni réservations. */
  isFrozen?: boolean;
}

export type OrderType = 'DineIn' | 'Takeaway' | 'Delivery';
export type OrderStatus = 'Draft' | 'Placed' | 'Accepted' | 'Preparing' | 'Ready' | 'Completed' | 'Cancelled';
export type OrderChannel = 'Counter' | 'QrCode' | 'Web' | 'MobileApp' | 'Kiosk' | 'Api' | 'Phone' | 'WhatsApp' | 'DeliveryPlatform';

export interface OrderSummary {
  id: string;
  number: string;
  locationId: string;
  type: OrderType;
  status: OrderStatus;
  tableSessionId: string | null;
  itemCount: number;
  currencyCode: string;
  total: number;
  createdAtUtc: string;
  channel?: OrderChannel;
  scheduledForUtc?: string | null;
  platform?: string | null;
  externalReference?: string | null;
}

export interface OrderItem {
  id: string;
  productId: string;
  productName: string;
  variantId: string | null;
  variantName: string | null;
  quantity: number;
  unitPrice: number;
  modifiers: {modifierId: string; name: string; priceDelta: number}[];
  note: string | null;
  taxRate: number;
  lineTotal: number;
  discountAmount: number;
  taxAmount: number;
  totalWithTax: number;
  components?: OrderItemComponent[];
}

/** Un produit choisi dans une formule, figé sur la ligne. */
export interface OrderItemComponent {
  id: string;
  stepName: string;
  productId: string;
  productName: string;
  variantId: string | null;
  variantName: string | null;
  supplement: number;
  modifiers: {modifierId: string; name: string; priceDelta: number}[];
}

export interface Order {
  id: string;
  number: string;
  locationId: string;
  type: OrderType;
  status: OrderStatus;
  currencyCode: string;
  priceTaxMode: 'TaxInclusive' | 'TaxExclusive';
  tableSessionId: string | null;
  customerId: string | null;
  items: OrderItem[];
  discounts: {source: string; reference: string; label: string | null; amount: number}[];
  subTotal: number;
  discountTotal: number;
  taxTotal: number;
  netTotal: number;
  total: number;
  createdAtUtc: string;
  placedAtUtc: string | null;
  completedAtUtc: string | null;
  cancelledAtUtc: string | null;
  cancellationReason: string | null;
  deliveryFee?: number | null;
  channel?: OrderChannel;
  platform?: string | null;
  externalReference?: string | null;
}

export type ProductStatus = 'Draft' | 'Active' | 'Unavailable' | 'Archived';

export interface ProductSummary {
  id: string;
  name: string;
  status: ProductStatus;
  isSellable: boolean;
  currencyCode: string;
  basePrice: number;
  variantCount: number;
  isFormula?: boolean;
  /** Établissements où le produit est vendu ; vide : tous. */
  locationIds?: string[];
  /** Établissements où il est en rupture. */
  unavailableAt?: string[];
}

export interface Variant {
  id: string;
  name: string;
  priceDelta: number;
  price: number;
  sku: string | null;
  isAvailable: boolean;
}

export interface Product {
  id: string;
  name: string;
  description: string | null;
  status: ProductStatus;
  isSellable: boolean;
  currencyCode: string;
  basePrice: number;
  taxCategoryId: string;
  variants: Variant[];
  modifierGroupIds: string[];
  allergens: AllergenCode[];
  hiddenChannels: OnlineChannel[];
  priceOverrides?: PriceOverride[];
  formulaSteps?: FormulaStep[];
  isFormula?: boolean;
  locationIds?: string[];
  unavailableAt?: string[];
}

/** Un produit proposé à une étape de formule, avec son supplément (ajouté au prix de la formule). */
export interface FormulaChoice {
  productId: string;
  variantId: string | null;
  supplement: number;
}

/** Une étape de formule (« Plat », « Boisson ») : de minChoices (0 : facultative) à maxChoices produits. */
export interface FormulaStep {
  id: string;
  name: string;
  minChoices: number;
  maxChoices: number;
  choices: FormulaChoice[];
}

export type PriceChannel = 'InHouse' | 'Web' | 'MobileApp' | 'Kiosk';

/** Prix de base propre à un établissement (null : tous), à un canal (null : tous), ou aux deux. */
export interface PriceOverride {
  locationId: string | null;
  channel: PriceChannel | null;
  basePrice: number;
}

export type AllergenCode =
  | 'gluten' | 'crustaceans' | 'eggs' | 'fish' | 'peanuts' | 'soybeans' | 'milk'
  | 'nuts' | 'celery' | 'mustard' | 'sesame' | 'sulphites' | 'lupin' | 'molluscs';

export type OnlineChannel = 'Web' | 'MobileApp' | 'Kiosk';

export interface Modifier {
  id: string;
  name: string;
  priceDelta: number;
  isAvailable: boolean;
}

export interface ModifierGroup {
  id: string;
  name: string;
  currencyCode: string;
  minSelections: number;
  maxSelections: number;
  isSatisfiable: boolean;
  modifiers: Modifier[];
}

export interface TaxCategory {
  id: string;
  code: string;
  name: string;
  rate: number;
  status: 'Active' | 'Archived';
}

export type MenuStatus = 'Draft' | 'Published' | 'Archived';

export interface MenuSummary {
  id: string;
  locationId: string;
  name: string;
  status: MenuStatus;
  publishedAtUtc: string | null;
  sectionCount: number;
}

export interface Menu {
  id: string;
  locationId: string;
  name: string;
  status: MenuStatus;
  publishedAtUtc: string | null;
  sections: {id: string; name: string; productIds: string[]}[];
  slots?: MenuSlot[];
}

export type Weekday = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday';

/** Créneau d'une carte en heure locale (HH:mm) ; une fin avant le début passe minuit. */
export interface MenuSlot {
  day: Weekday;
  start: string;
  end: string;
}

export interface Station {
  id: string;
  locationId: string;
  name: string;
  isActive: boolean;
}

export interface StationRouting {
  id: string;
  locationId: string;
  defaultStationId: string;
  routes: {productId: string; stationId: string}[];
  courses?: {productId: string; course: Course}[];
}

/** Service d'un plat : à table, les services après le premier attendent d'être envoyés par la salle. */
export type Course = 'Direct' | 'Starter' | 'Main' | 'Dessert';

export type TicketStatus = 'Queued' | 'InProgress' | 'Ready' | 'Completed' | 'Cancelled';
export type TicketPriority = 'Normal' | 'High' | 'Rush';

export interface KitchenTicket {
  id: string;
  orderId: string;
  locationId: string;
  status: TicketStatus;
  priority: TicketPriority;
  requiredReadyPercentage: number;
  canBeMarkedReady: boolean;
  canBeCompleted: boolean;
  items: {
    id: string;
    orderItemId: string;
    stationId: string;
    name: string;
    variantName: string | null;
    modifiers: string[];
    note: string | null;
    quantity: number;
    status: 'Queued' | 'Preparing' | 'Ready';
    course?: Course;
    isHeld?: boolean;
  }[];
  heldCourses?: Course[];
  createdAtUtc: string;
  startedAtUtc: string | null;
  readyAtUtc: string | null;
}

export interface DiningTable {
  id: string;
  label: string;
  capacity: number;
  status: 'Available' | 'OutOfService';
  isInService: boolean;
  openSessionId: string | null;
  layout?: TableLayout | null;
}

export type TableShape = 'Square' | 'Round' | 'Rectangle';

/** Place sur le plan dessiné, dans une grille de 100 × 100 ; rotation en degrés autour du centre, par pas de 15. */
export interface TableLayout {
  x: number;
  y: number;
  width: number;
  height: number;
  shape: TableShape;
  rotation?: number;
}

export interface DiningArea {
  id: string;
  locationId: string;
  name: string;
  seatsInService: number;
  tables: DiningTable[];
}

export interface TableSession {
  id: string;
  locationId: string;
  tableId: string;
  guestCount: number;
  reservationId: string | null;
  isOpen: boolean;
  openedAtUtc: string;
  closedAtUtc: string | null;
}

export interface ReportRow {
  periodStart: string;
  periodEnd: string;
  locationId: string | null;
  metric: string;
  dimension: string;
  currency: string;
  value: number;
}

export interface Dashboard {
  from: string;
  to: string;
  totals: ReportRow[];
  revenueByDay: ReportRow[];
}
