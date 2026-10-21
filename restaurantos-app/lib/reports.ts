import {all, api, del, post, put} from './api';
import {addDays, startOfDay} from './dashboard';
import {isoDate} from './format';
import type {ProductSummary, ReportRow} from './types';

export type Granularity = 'Day' | 'Week' | 'Month' | 'Total';

export type ReportKind = 'sales' | 'orders' | 'reservations' | 'kitchen' | 'payments';

export const METRICS: Record<ReportKind, string[]> = {
  sales: ['sales.revenue', 'sales.placed_amount', 'sales.discount_amount', 'sales.tax_amount', 'sales.cancelled_amount', 'sales.average_ticket'],
  orders: ['sales.orders_placed', 'sales.orders_completed', 'sales.orders_cancelled', 'sales.cancellation_rate'],
  reservations: ['reservations.created', 'reservations.covers_booked', 'reservations.seated', 'reservations.covers_seated', 'reservations.cancelled', 'reservations.no_shows', 'reservations.no_show_rate'],
  kitchen: ['kitchen.tickets_created', 'kitchen.tickets_ready', 'kitchen.tickets_cancelled', 'kitchen.average_preparation_minutes'],
  payments: ['payments.captured_count', 'payments.captured_amount', 'payments.failed_count', 'payments.refund_count', 'payments.refunded_amount', 'payments.refund_rate'],
};

export interface Period {
  from: Date;
  to: Date;
}

export const presets: {key: string; label: string; period: () => Period}[] = [
  {key: '7d', label: '7 derniers jours', period: () => ({from: addDays(startOfDay(new Date()), -6), to: startOfDay(new Date())})},
  {key: '30d', label: '30 derniers jours', period: () => ({from: addDays(startOfDay(new Date()), -29), to: startOfDay(new Date())})},
  {
    key: 'month',
    label: 'Ce mois-ci',
    period: () => {
      const today = startOfDay(new Date());
      return {from: new Date(today.getFullYear(), today.getMonth(), 1), to: today};
    },
  },
  {
    key: 'last-month',
    label: 'Mois dernier',
    period: () => {
      const today = new Date();
      return {from: new Date(today.getFullYear(), today.getMonth() - 1, 1), to: new Date(today.getFullYear(), today.getMonth(), 0)};
    },
  },
  {key: '90d', label: '90 derniers jours', period: () => ({from: addDays(startOfDay(new Date()), -89), to: startOfDay(new Date())})},
];

export function days(period: Period): number {
  return Math.round((period.to.getTime() - period.from.getTime()) / 86_400_000) + 1;
}

/** Période précédente de même durée, juste avant. */
export function previous(period: Period): Period {
  const length = days(period);
  return {from: addDays(period.from, -length), to: addDays(period.from, -1)};
}

export async function runReport(metrics: string[], period: Period, granularity: Granularity, locationId: string | null, signal?: AbortSignal): Promise<ReportRow[]> {
  const result = await api<{rows: ReportRow[]}>('/reporting/run', {
    method: 'POST',
    body: {
      metrics,
      filters: locationId ? [{field: 'location.id', operator: 'EqualTo', value: locationId}] : [],
      from: isoDate(period.from),
      to: isoDate(period.to),
      granularity,
      byLocation: false,
    },
    signal,
  });
  return result.rows;
}

/** Somme d'une mesure (toutes dimensions, ou une seule). */
export function total(rows: ReportRow[], metric: string, dimension?: string): number {
  return rows.filter(row => row.metric === metric && (dimension === undefined || row.dimension === dimension)).reduce((sum, row) => sum + row.value, 0);
}

/** Valeurs d'une mesure par dimension, triées par valeur décroissante. */
export function byDimension(rows: ReportRow[], metric: string): {dimension: string; value: number}[] {
  const values = new Map<string, number>();
  rows.filter(row => row.metric === metric).forEach(row => values.set(row.dimension, (values.get(row.dimension) ?? 0) + row.value));
  return [...values.entries()].map(([dimension, value]) => ({dimension, value})).sort((a, b) => b.value - a.value);
}

const shortDay = new Intl.DateTimeFormat('fr-FR', {day: 'numeric', month: 'short'});
const monthLabel = new Intl.DateTimeFormat('fr-FR', {month: 'short', year: '2-digit'});

/** Série d'une mesure par période ; en granularité jour, les jours sans valeur valent 0. */
export function series(rows: ReportRow[], metric: string, period: Period, granularity: Granularity, dimension?: string): {label: string; value: number; start: string}[] {
  const values = new Map<string, number>();
  rows
    .filter(row => row.metric === metric && (dimension === undefined || row.dimension === dimension))
    .forEach(row => values.set(row.periodStart.slice(0, 10), (values.get(row.periodStart.slice(0, 10)) ?? 0) + row.value));
  let starts: string[];
  if (granularity === 'Day') {
    starts = Array.from({length: days(period)}, (_, i) => isoDate(addDays(period.from, i)));
  } else {
    starts = [...new Set([...values.keys()])].sort();
  }
  return starts.map(start => {
    const date = new Date(`${start}T00:00:00`);
    const label = granularity === 'Month' ? monthLabel.format(date) : granularity === 'Week' ? `sem. ${shortDay.format(date)}` : shortDay.format(date);
    return {start, label, value: values.get(start) ?? 0};
  });
}

export const dimensionLabel: Record<string, string> = {
  dine_in: 'Sur place',
  takeaway: 'À emporter',
  delivery: 'Livraison',
  card: 'Carte en ligne',
  apple_pay: 'Apple Pay',
  google_pay: 'Google Pay',
  pay_pal: 'PayPal',
  mobile_money: 'Mobile money',
  bank_transfer: 'Virement',
  cash: 'Espèces',
  card_on_site: 'Carte (TPE)',
  mobile_money_on_site: 'Mobile money (caisse)',
  meal_voucher: 'Titres-restaurant',
  guest: 'Par le client',
  restaurant: 'Par le restaurant',
};

export function labelOf(dimension: string): string {
  return dimensionLabel[dimension] ?? (dimension && dimension !== 'all' && dimension !== 'none' ? dimension : 'Autres');
}

export const palette = ['#6b3ee6', '#f59e0b', '#3b82f6', '#16a85f', '#ef4444', '#14b8a6', '#94a3b8'];

export interface ProductSales {
  productId: string;
  name: string;
  quantity: number;
  amount: number;
}

export interface SalesBreakdown {
  /** Commandes terminées de la période (mesure horaire). */
  orders: number;
  /** Commandes terminées par jour de semaine (lundi d'abord) et par heure locale, de `fromHour` à `toHour`. */
  heat: number[][];
  products: ProductSales[];
}

/**
 * Ventes par produit et carte de chaleur d'après les compteurs du reporting (toutes les commandes terminées de la
 * période, un ou tous les établissements). Un produit supprimé du catalogue garde ses ventes sous un nom générique.
 */
export async function salesBreakdown(period: Period, locationId: string | null, fromHour: number, toHour: number, signal?: AbortSignal): Promise<SalesBreakdown> {
  const [sold, hourly, catalog] = await Promise.all([
    runReport(['sales.items_sold', 'sales.product_revenue'], period, 'Total', locationId, signal),
    runReport(['sales.hourly_orders'], period, 'Day', locationId, signal),
    all<ProductSummary>('/catalog/products', {}, 2000).catch(() => [] as ProductSummary[]),
  ]);
  const names = new Map(catalog.map(product => [product.id, product.name]));
  const products = new Map<string, ProductSales>();
  sold.forEach(row => {
    const current = products.get(row.dimension) ?? {productId: row.dimension, name: names.get(row.dimension) ?? 'Produit retiré de la carte', quantity: 0, amount: 0};
    if (row.metric === 'sales.items_sold') current.quantity += row.value;
    else current.amount += row.value;
    products.set(row.dimension, current);
  });

  const heat = Array.from({length: 7}, () => new Array<number>(toHour - fromHour + 1).fill(0));
  let orders = 0;
  hourly.forEach(row => {
    orders += row.value;
    const hour = Number(row.dimension);
    if (!Number.isInteger(hour) || hour < fromHour || hour > toHour) return;
    const [year, month, day] = row.periodStart.slice(0, 10).split('-').map(Number);
    heat[(new Date(year, month - 1, day).getDay() + 6) % 7][hour - fromHour] += row.value;
  });
  return {orders, heat, products: [...products.values()].filter(product => product.quantity > 0).sort((a, b) => b.quantity - a.quantity)};
}

export interface ExportJob {
  id: string;
  status: 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Expired';
  downloadable: boolean;
  failureReason: string | null;
}

export type ExportFormat = 'Csv' | 'Xlsx' | 'Pdf';

export const exportFormats: {value: ExportFormat; label: string; extension: string}[] = [
  {value: 'Xlsx', label: 'Excel (.xlsx)', extension: 'xlsx'},
  {value: 'Pdf', label: 'PDF', extension: 'pdf'},
  {value: 'Csv', label: 'CSV', extension: 'csv'},
];

export const extensionOf = (format: ExportFormat) => exportFormats.find(item => item.value === format)!.extension;

/** Attend qu'un export soit prêt (2 minutes au plus). */
async function waitForExport(id: string): Promise<string> {
  for (let attempt = 0; attempt < 60; attempt++) {
    const job = await api<ExportJob>(`/reporting/exports/${id}`);
    if (job.status === 'Completed' && job.downloadable) return id;
    if (job.status === 'Failed' || job.status === 'Expired') throw new Error(job.failureReason ? `Export échoué : ${job.failureReason}` : 'L’export a échoué.');
    await new Promise(resolve => window.setTimeout(resolve, 2000));
  }
  throw new Error('L’export prend plus de temps que prévu : réessayez dans quelques minutes.');
}

/** Demande un export (CSV, Excel ou PDF) et attend qu'il soit prêt. */
export async function exportReport(metrics: string[], period: Period, granularity: Granularity, locationId: string | null, format: ExportFormat = 'Csv'): Promise<string> {
  const created = await post<{id: string}>('/reporting/exports', {
    definitionId: null,
    metrics,
    filters: locationId ? [{field: 'location.id', operator: 'EqualTo', value: locationId}] : [],
    from: isoDate(period.from),
    to: isoDate(period.to),
    granularity: granularity === 'Total' ? 'Day' : granularity,
    byLocation: !locationId,
    format,
  });
  return waitForExport(created.id);
}

/** Plan de comptes de l'export comptable ; moyens de paiement par code de mesure. */
export interface AccountingPlan {
  salesJournal: string;
  treasuryJournal: string;
  customersAccount: string;
  salesAccount: string;
  vatAccount: string;
  defaultPaymentAccount: string;
  paymentAccounts: Record<string, string>;
}

/** Le plan SYSCOHADA proposé par défaut (le même que le serveur). */
export const defaultAccountingPlan: AccountingPlan = {
  salesJournal: 'VT',
  treasuryJournal: 'TR',
  customersAccount: '411100',
  salesAccount: '701100',
  vatAccount: '443100',
  defaultPaymentAccount: '585000',
  paymentAccounts: {
    cash: '571100',
    card_on_site: '585100',
    mobile_money_on_site: '585200',
    meal_voucher: '585300',
    bank_transfer: '521100',
    gift_card: '419100',
    delivery_platform: '411200',
    cash_on_delivery: '571100',
    card: '585400',
    mobile_money: '585600',
  },
};

export const paymentAccountLabel: Record<string, string> = {
  cash: 'Espèces',
  card_on_site: 'Carte (terminal)',
  mobile_money_on_site: 'Mobile money (comptoir)',
  meal_voucher: 'Titres-restaurant',
  bank_transfer: 'Virements',
  gift_card: 'Cartes cadeaux et avoirs',
  delivery_platform: 'Plateformes de livraison',
  cash_on_delivery: 'Espèces encaissées à la livraison',
  card: 'Cartes en ligne',
  mobile_money: 'Mobile money en ligne',
};

/** Journal comptable de la période (ventes et trésorerie, par jour), au format CSV des logiciels de comptabilité. */
export async function exportAccounting(period: Period, plan: AccountingPlan): Promise<string> {
  const created = await post<{id: string}>('/reporting/exports/accounting', {from: isoDate(period.from), to: isoDate(period.to), plan});
  return waitForExport(created.id);
}

export type Frequency = 'Daily' | 'Weekly' | 'Monthly';
export type Weekday = 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday' | 'Sunday';

export interface ReportSchedule {
  frequency: Frequency;
  dayOfWeek: Weekday | null;
  dayOfMonth: number | null;
  localTime: string;
  timeZone: string;
  recipients: string[];
  nextRunAtUtc: string | null;
  lastRunAtUtc: string | null;
}

export interface ReportDefinition {
  id: string;
  name: string;
  metrics: string[];
  filters: {field: string; operator: string; value: string}[];
  schedule: ReportSchedule | null;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface ScheduleInput {
  frequency: Frequency;
  dayOfWeek: Weekday | null;
  dayOfMonth: number | null;
  localTime: string;
  recipients: string[];
}

const locationFilter = (locationId: string | null) => (locationId ? [{field: 'location.id', operator: 'EqualTo', value: locationId}] : []);

export const listDefinitions = (signal?: AbortSignal) => api<ReportDefinition[]>('/reporting/definitions', {signal});

export const createDefinition = (name: string, metrics: string[], locationId: string | null) =>
  post<{id: string}>('/reporting/definitions', {name, metrics, filters: locationFilter(locationId)}).then(created => created.id);

export const renameDefinition = (id: string, name: string) => put(`/reporting/definitions/${id}/name`, {name});

export const changeDefinitionLocation = (id: string, locationId: string | null) => put(`/reporting/definitions/${id}/filters`, {filters: locationFilter(locationId)});

export const scheduleDefinition = (id: string, input: ScheduleInput) => put(`/reporting/definitions/${id}/schedule`, input);

export const unscheduleDefinition = (id: string) => del(`/reporting/definitions/${id}/schedule`);

/** Établissement filtré par un rapport enregistré, ou null s'il couvre tout le restaurant. */
export const definitionLocation = (definition: ReportDefinition) =>
  definition.filters.find(filter => filter.field === 'location.id' && filter.operator === 'EqualTo')?.value ?? null;

/** Type de rapport dont les mesures correspondent exactement, sinon null (rapport personnalisé). */
export function definitionKind(definition: ReportDefinition): ReportKind | 'all' | null {
  const same = (metrics: string[]) => metrics.length === definition.metrics.length && metrics.every(metric => definition.metrics.includes(metric));
  const all = Object.values(METRICS).flat();
  if (same(all)) return 'all';
  return (Object.keys(METRICS) as ReportKind[]).find(kind => same(METRICS[kind])) ?? null;
}

/** Libellés des mesures du reporting ; une mesure inconnue garde son code. */
export const metricLabel: Record<string, string> = {
  'sales.orders_placed': 'Commandes passées',
  'sales.placed_amount': 'Montant commandé',
  'sales.discount_amount': 'Remises accordées',
  'sales.tax_amount': 'Taxes',
  'sales.orders_completed': 'Commandes terminées',
  'sales.revenue': 'Chiffre d’affaires',
  'sales.orders_cancelled': 'Commandes annulées',
  'sales.cancelled_amount': 'Montant annulé',
  'sales.average_ticket': 'Panier moyen',
  'sales.cancellation_rate': 'Taux d’annulation',
  'sales.items_sold': 'Articles vendus',
  'sales.product_revenue': 'Ventes par produit',
  'sales.hourly_orders': 'Commandes terminées par heure',
  'sales.hourly_revenue': 'Chiffre d’affaires par heure',
  'payments.captured_count': 'Paiements encaissés',
  'payments.captured_amount': 'Montant encaissé',
  'payments.failed_count': 'Paiements échoués',
  'payments.refund_count': 'Remboursements',
  'payments.refunded_amount': 'Montant remboursé',
  'payments.refund_rate': 'Taux de remboursement',
  'reservations.created': 'Réservations créées',
  'reservations.covers_booked': 'Couverts réservés',
  'reservations.seated': 'Réservations installées',
  'reservations.covers_seated': 'Couverts installés',
  'reservations.cancelled': 'Réservations annulées',
  'reservations.no_shows': 'Absences',
  'reservations.no_show_rate': 'Taux d’absence',
  'kitchen.tickets_created': 'Tickets cuisine',
  'kitchen.tickets_ready': 'Tickets prêts',
  'kitchen.tickets_cancelled': 'Tickets annulés',
  'kitchen.preparation_seconds': 'Temps de préparation cumulé (s)',
  'kitchen.average_preparation_minutes': 'Préparation moyenne (min)',
  'inventory.receipts': 'Réceptions de stock',
  'inventory.waste_records': 'Pertes déclarées',
  'inventory.adjustments': 'Ajustements de stock',
  'inventory.low_stock_alerts': 'Alertes stock bas',
  'inventory.negative_stock_alerts': 'Alertes stock négatif',
  'promotions.applied': 'Promotions appliquées',
  'promotions.discount_amount': 'Remises des promotions',
  'promotions.reverted': 'Promotions retirées',
  'promotions.coupons_redeemed': 'Codes promo utilisés',
  'loyalty.accounts_opened': 'Comptes fidélité ouverts',
  'loyalty.points_earned': 'Points gagnés',
  'loyalty.points_redeemed': 'Points utilisés',
  'loyalty.points_expired': 'Points expirés',
  'loyalty.rewards_redeemed': 'Récompenses utilisées',
  'loyalty.reward_discount_amount': 'Remises des récompenses',
};

export const sourceLabel: Record<string, string> = {
  sales: 'Ventes', payments: 'Paiements', reservations: 'Réservations', kitchen: 'Cuisine', inventory: 'Stock', promotions: 'Promotions', loyalty: 'Fidélité',
};

export interface ReportCatalog {
  metrics: {code: string; source: string; monetary: boolean; derived: boolean}[];
  filterFields: string[];
  operators: string[];
}

export const loadCatalog = (signal?: AbortSignal) => api<ReportCatalog>('/reporting/catalog', {signal});

export interface ExportSummary {
  id: string;
  status: ExportJob['status'];
  type: string;
  definitionId: string | null;
  title: string | null;
  from: string | null;
  to: string | null;
  scheduledDelivery: boolean;
  downloadable: boolean;
  failureReason: string | null;
  queuedAtUtc: string;
  completedAtUtc: string | null;
  expiredAtUtc: string | null;
}

export const listExports = (page: number, signal?: AbortSignal) =>
  api<{items: ExportSummary[]; hasMore: boolean}>('/reporting/exports', {query: {page, pageSize: 15}, signal});

/** Exécute un rapport enregistré à l'écran : un total par mesure sur la période. */
export const runDefinition = (id: string, period: Period, signal?: AbortSignal) =>
  post<{rows: ReportRow[]}>(`/reporting/definitions/${id}/run`, {from: isoDate(period.from), to: isoDate(period.to), granularity: 'Total', byLocation: false}).then(result => result.rows);

/** Export CSV d'un rapport enregistré ; attend qu'il soit prêt. */
export async function exportDefinition(id: string, period: Period): Promise<string> {
  const created = await post<{id: string}>('/reporting/exports', {definitionId: id, metrics: null, filters: null, from: isoDate(period.from), to: isoDate(period.to), granularity: 'Day', byLocation: false});
  for (let attempt = 0; attempt < 60; attempt++) {
    const job = await api<ExportJob>(`/reporting/exports/${created.id}`);
    if (job.status === 'Completed' && job.downloadable) return created.id;
    if (job.status === 'Failed' || job.status === 'Expired') throw new Error(job.failureReason ? `Export échoué : ${job.failureReason}` : 'L’export a échoué.');
    await new Promise(resolve => window.setTimeout(resolve, 2000));
  }
  throw new Error('L’export prend plus de temps que prévu : réessayez dans quelques minutes.');
}
