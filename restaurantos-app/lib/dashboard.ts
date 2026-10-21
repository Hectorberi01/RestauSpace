import {all, api, type Page} from './api';
import {isoDate} from './format';
import type {Dashboard, Order, OrderSummary, OrderType, ProductSummary, ReportRow} from './types';

export type {Reservation} from './reservations';

export interface Feedback {
  id: string;
  locationId: string;
  customerId: string | null;
  orderId: string | null;
  rating: number;
  comment: string | null;
  submittedAtUtc: string;
}

export function startOfDay(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate());
}

export function addDays(date: Date, days: number): Date {
  const next = new Date(date);
  next.setDate(next.getDate() + days);
  return next;
}

/**
 * Commandes de l'établissement créées depuis `since` (hors brouillons), lues page par page dans l'ordre de création
 * décroissant de l'API ; l'API n'a pas de filtre par date. Plafonné à `maxPages` pages de 100.
 */
export async function ordersSince(locationId: string, since: Date, signal?: AbortSignal, maxPages = 8): Promise<OrderSummary[]> {
  const result: OrderSummary[] = [];
  for (let page = 1; page <= maxPages; page++) {
    const data = await api<Page<OrderSummary>>('/orders', {query: {locationId, page, pageSize: 100}, signal});
    let older = false;
    for (const order of data.items) {
      if (new Date(order.createdAtUtc) < since) {
        older = true;
        break;
      }
      if (order.status !== 'Draft') result.push(order);
    }
    if (older || !data.hasMore || data.items.length === 0) break;
  }
  return result;
}

export function sameDay(iso: string, day: Date): boolean {
  const date = new Date(iso);
  return date >= day && date < addDays(day, 1);
}

export interface DaySummary {
  orders: number;
  revenue: number;
  averageTicket: number;
  byType: Record<OrderType, number>;
}

/** Commandes passées du jour (hors annulées) ; CA = commandes terminées. */
export function summarize(orders: OrderSummary[]): DaySummary {
  const valid = orders.filter(order => order.status !== 'Cancelled');
  const completed = valid.filter(order => order.status === 'Completed');
  const revenue = completed.reduce((sum, order) => sum + order.total, 0);
  const byType: Record<OrderType, number> = {DineIn: 0, Takeaway: 0, Delivery: 0};
  valid.forEach(order => (byType[order.type] += 1));
  return {orders: valid.length, revenue, averageTicket: completed.length ? revenue / completed.length : 0, byType};
}

/** CA cumulé heure par heure (commandes terminées, à l'heure de création), de `fromHour` à `toHour` inclus. */
export function cumulativeByHour(orders: OrderSummary[], fromHour: number, toHour: number): {label: string; value: number}[] {
  const perHour = new Array<number>(24).fill(0);
  orders
    .filter(order => order.status === 'Completed')
    .forEach(order => (perHour[new Date(order.createdAtUtc).getHours()] += order.total));
  const points: {label: string; value: number}[] = [];
  let running = perHour.slice(0, fromHour).reduce((a, b) => a + b, 0);
  for (let hour = fromHour; hour <= toHour; hour++) {
    running += perHour[hour];
    points.push({label: `${String(hour).padStart(2, '0')}h`, value: running});
  }
  return points;
}

/** Variation en pourcentage ; null quand la base est nulle. */
export function change(current: number, previous: number): number | null {
  if (!previous) return null;
  return (current - previous) / previous;
}

export type ProductSales = {name: string; quantity: number; productId: string; source: 'reporting' | 'orders'};

/** Plats les plus vendus sur 30 jours (mesure « sales.items_sold » du reporting, dimension = id du produit). */
async function soldFromReporting(locationId: string, signal?: AbortSignal): Promise<ProductSales[]> {
  const today = new Date();
  const from = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 29);
  const result = await api<{rows: ReportRow[]}>('/reporting/run', {
    method: 'POST',
    body: {
      metrics: ['sales.items_sold'],
      filters: [{field: 'location.id', operator: 'EqualTo', value: locationId}],
      from: isoDate(from),
      to: isoDate(today),
      granularity: 'Total',
      byLocation: false,
    },
    signal,
  });
  const quantities = new Map<string, number>();
  result.rows.forEach(row => quantities.set(row.dimension, (quantities.get(row.dimension) ?? 0) + row.value));
  if (quantities.size === 0) return [];
  const products = await all<ProductSummary>('/catalog/products', {}, 2000);
  const names = new Map(products.map(product => [product.id, product.name]));
  return [...quantities]
    .filter(([productId, quantity]) => quantity > 0 && names.has(productId))
    .map(([productId, quantity]) => ({productId, quantity, name: names.get(productId)!, source: 'reporting' as const}))
    .sort((a, b) => b.quantity - a.quantity);
}

/** Repli sans reporting : le détail des `count` dernières commandes terminées. */
async function soldFromOrders(locationId: string, count: number, signal?: AbortSignal): Promise<ProductSales[]> {
  const page = await api<Page<OrderSummary>>('/orders', {query: {locationId, status: 'Completed', pageSize: count}, signal});
  const details = await Promise.all(page.items.map(order => api<Order>(`/orders/${order.id}`, {signal}).catch(() => null)));
  const totals = new Map<string, ProductSales>();
  details.forEach(order =>
    order?.items.forEach(item => {
      const current = totals.get(item.productId) ?? {name: item.productName, quantity: 0, productId: item.productId, source: 'orders' as const};
      current.quantity += item.quantity;
      totals.set(item.productId, current);
    }),
  );
  return [...totals.values()].sort((a, b) => b.quantity - a.quantity);
}

/** Plats les plus vendus : reporting d'abord ; sans droit ou sans données, les `count` dernières commandes. */
export async function topProducts(locationId: string, count: number, signal?: AbortSignal): Promise<ProductSales[]> {
  const sold = await soldFromReporting(locationId, signal).catch(error => {
    if (signal?.aborted) throw error;
    return [];
  });
  return sold.length > 0 ? sold : soldFromOrders(locationId, count, signal);
}

export const typeDimension: Record<OrderType, string> = {DineIn: 'dine_in', Delivery: 'delivery', Takeaway: 'takeaway'};

/** Commandes passées par jour et par canal sur 7 jours (reporting). */
export function channelSeries(rows: ReportRow[], days: Date[]): Record<OrderType, number[]> {
  const key = (date: Date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
  const result: Record<OrderType, number[]> = {DineIn: [], Takeaway: [], Delivery: []};
  (Object.keys(typeDimension) as OrderType[]).forEach(type => {
    result[type] = days.map(day =>
      rows.filter(row => row.metric === 'sales.orders_placed' && row.dimension === typeDimension[type] && row.periodStart.slice(0, 10) === key(day)).reduce((sum, row) => sum + row.value, 0),
    );
  });
  return result;
}

const ratios = new Set(['sales.average_ticket', 'sales.cancellation_rate', 'reservations.no_show_rate', 'kitchen.average_preparation_minutes', 'payments.refund_rate']);

/** Indicateurs agrégés par le reporting pour une période (jours locaux inclus), pour un établissement. */
export const loadDashboard = (locationId: string, from: Date, to: Date, signal?: AbortSignal) =>
  api<Dashboard>('/reporting/dashboard', {query: {from: isoDate(from), to: isoDate(to), locationId}, signal});

/** Valeur d'un indicateur : somme des dimensions, ou ligne globale pour un taux ou une moyenne. */
export function indicator(rows: ReportRow[], metric: string): number | null {
  const matching = rows.filter(row => row.metric === metric);
  if (matching.length === 0) return null;
  if (ratios.has(metric)) return (matching.find(row => row.dimension === '') ?? matching[0]).value;
  return matching.reduce((sum, row) => sum + row.value, 0);
}

export type Period = 'day' | 'week' | 'month';

export interface PeriodRange {
  period: Period;
  from: Date;
  to: Date;
  previousFrom: Date;
  previousTo: Date;
  /** Les jours de la période, du plus ancien au plus récent. */
  days: Date[];
  /** Les jours de la période précédente, alignés sur `days`. */
  previousDays: Date[];
  compareLabel: string;
}

/** Aujourd'hui, les 7 ou les 30 derniers jours (aujourd'hui compris), et la période d'avant, de même longueur. */
export function periodRange(period: Period, today: Date): PeriodRange {
  const length = period === 'day' ? 1 : period === 'week' ? 7 : 30;
  const from = addDays(today, 1 - length);
  const previousFrom = addDays(from, -length);
  const days = Array.from({length}, (_, i) => addDays(from, i));
  return {
    period,
    from,
    to: today,
    previousFrom,
    previousTo: addDays(from, -1),
    days,
    previousDays: days.map(day => addDays(day, -length)),
    compareLabel: period === 'day' ? 'vs hier' : period === 'week' ? 'vs 7 j précédents' : 'vs 30 j précédents',
  };
}

/** Commandes créées entre deux jours locaux inclus. */
export function ordersBetween(orders: OrderSummary[], from: Date, to: Date): OrderSummary[] {
  const end = addDays(to, 1);
  return orders.filter(order => {
    const at = new Date(order.createdAtUtc);
    return at >= from && at < end;
  });
}

const dayKey = (date: Date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;

/** Une valeur par jour : somme des lignes d'une mesure (toutes dimensions) dont la période commence ce jour-là. */
export function dailyValues(rows: ReportRow[], metric: string, days: Date[]): number[] {
  const sums = new Map<string, number>();
  rows.filter(row => row.metric === metric).forEach(row => sums.set(row.periodStart.slice(0, 10), (sums.get(row.periodStart.slice(0, 10)) ?? 0) + row.value));
  return days.map(day => sums.get(dayKey(day)) ?? 0);
}

/** Repli sans reporting : CA (commandes terminées) ou nombre de commandes (hors annulées) par jour. */
export function dailyFromOrders(orders: OrderSummary[], days: Date[], measure: 'revenue' | 'orders'): number[] {
  return days.map(day => {
    const ofDay = orders.filter(order => sameDay(order.createdAtUtc, day));
    return measure === 'revenue'
      ? ofDay.filter(order => order.status === 'Completed').reduce((sum, order) => sum + order.total, 0)
      : ofDay.filter(order => order.status !== 'Cancelled').length;
  });
}

/** Lignes d'une mesure par dimension (moyen de paiement, canal…), triées par valeur décroissante. */
export function byDimension(rows: ReportRow[], metric: string): {dimension: string; value: number}[] {
  const sums = new Map<string, number>();
  rows.filter(row => row.metric === metric && row.dimension).forEach(row => sums.set(row.dimension, (sums.get(row.dimension) ?? 0) + row.value));
  return [...sums].map(([dimension, value]) => ({dimension, value})).filter(item => item.value > 0).sort((a, b) => b.value - a.value);
}
