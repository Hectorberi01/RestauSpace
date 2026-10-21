import {all, api, del, post, put} from './api';
import {tablesBySession, type TableInfo} from './orders';
import type {Course, KitchenTicket, Menu, MenuSummary, Order, ProductSummary, StationRouting} from './types';

export interface BoardTicket extends KitchenTicket {
  order: Order | null;
  table: TableInfo | null;
}

const orders = new Map<string, {order: Order; at: number}>();

/** Détail d'une commande, gardé 30 s (le type et la table ne changent pas pendant la préparation). */
async function orderOf(id: string, signal?: AbortSignal): Promise<Order | null> {
  const cached = orders.get(id);
  if (cached && Date.now() - cached.at < 30_000) return cached.order;
  try {
    const order = await api<Order>(`/orders/${id}`, {signal});
    orders.set(id, {order, at: Date.now()});
    return order;
  } catch {
    return cached?.order ?? null;
  }
}

/** Tickets ouverts de l'établissement, avec leur commande (numéro, type, produits) et leur table. */
export async function loadBoard(locationId: string, signal?: AbortSignal): Promise<BoardTicket[]> {
  const [tickets, tables] = await Promise.all([
    api<KitchenTicket[]>('/kitchen/tickets', {query: {locationId}, signal}),
    tablesBySession(locationId, signal).catch(() => new Map<string, TableInfo>()),
  ]);
  const details = await Promise.all(tickets.map(ticket => orderOf(ticket.orderId, signal)));
  return tickets.map((ticket, i) => {
    const order = details[i];
    return {...ticket, order, table: order?.tableSessionId ? tables.get(order.tableSessionId) ?? null : null};
  });
}

export function forgetOrder(id: string) {
  orders.delete(id);
}

/**
 * « Marquer comme prêt » : quand la règle du ticket n'est pas encore remplie, les plats restants sont lancés puis
 * déclarés prêts un à un, avant de passer le ticket prêt.
 */
export async function finishTicket(ticket: KitchenTicket): Promise<void> {
  const base = `/kitchen/tickets/${ticket.id}`;
  if (!ticket.canBeMarkedReady) {
    for (const item of ticket.items) {
      if (item.status === 'Queued') await post(`${base}/items/${item.id}/start`);
      if (item.status === 'Queued' || item.status === 'Preparing') await post(`${base}/items/${item.id}/ready`);
    }
  }
  await post(`${base}/ready`);
}

/** Remise au service : les plats non encore cochés sont déclarés prêts, le ticket est clos. */
export async function handOver(ticket: KitchenTicket): Promise<void> {
  const base = `/kitchen/tickets/${ticket.id}`;
  for (const item of ticket.items) {
    if (item.status === 'Queued') await post(`${base}/items/${item.id}/start`);
    if (item.status === 'Queued' || item.status === 'Preparing') await post(`${base}/items/${item.id}/ready`);
  }
  await post(`${base}/complete`);
}

export interface RoutableSection {
  key: string;
  name: string;
  products: ProductSummary[];
}

/**
 * Les plats à répartir entre les postes, rangés comme sur les cartes publiées de l'établissement. Les produits hors
 * carte (brouillons, ou pas encore placés) suivent dans « Hors carte ». Les produits archivés ne sont pas proposés.
 */
export async function routableProducts(locationId: string, signal?: AbortSignal): Promise<RoutableSection[]> {
  const [menus, products] = await Promise.all([
    api<MenuSummary[]>('/catalog/menus', {query: {locationId}, signal}),
    all<ProductSummary>('/catalog/products', {}, 2000),
  ]);
  const byId = new Map(products.filter(product => product.status !== 'Archived').map(product => [product.id, product]));
  const published = menus.filter(menu => menu.status === 'Published');
  const details = await Promise.all(published.map(menu => api<Menu>(`/catalog/menus/${menu.id}`, {signal})));
  const placed = new Set<string>();
  const sections: RoutableSection[] = details.flatMap(menu =>
    menu.sections.map(section => {
      const items = section.productIds.map(id => byId.get(id)).filter((product): product is ProductSummary => !!product && !placed.has(product.id));
      items.forEach(product => placed.add(product.id));
      return {key: `${menu.id}-${section.id}`, name: details.length > 1 ? `${menu.name} · ${section.name}` : section.name, products: items};
    }),
  );
  const rest = [...byId.values()].filter(product => !placed.has(product.id)).sort((a, b) => a.name.localeCompare(b.name, 'fr'));
  if (rest.length) sections.push({key: 'hors-carte', name: details.length ? 'Hors carte' : 'Tous les produits', products: rest});
  return sections.filter(section => section.products.length > 0);
}

export const loadRouting = (locationId: string, signal?: AbortSignal) => api<StationRouting>(`/kitchen/routings/${locationId}`, {signal});

/** Envoie le plat à un poste ; null le renvoie au poste par défaut. */
export const routeProduct = (locationId: string, productId: string, stationId: string | null) =>
  stationId ? put(`/kitchen/routings/${locationId}/routes/${productId}`, {stationId}) : del(`/kitchen/routings/${locationId}/routes/${productId}`);

export const courseLabel: Record<Course, string> = {Direct: 'Envoi direct', Starter: 'Entrée', Main: 'Plat', Dessert: 'Dessert'};

/** Libellé du bouton qui envoie un service en cuisine. */
export const fireLabel: Record<Course, string> = {Direct: 'Envoyer', Starter: 'Envoyer les entrées', Main: 'Envoyer les plats', Dessert: 'Envoyer les desserts'};

export const setProductCourse = (locationId: string, productId: string, course: Course) =>
  put(`/kitchen/routings/${locationId}/courses/${productId}`, {course});

export const fireCourse = (ticketId: string, course: Course) => post(`/kitchen/tickets/${ticketId}/fire`, {course});

export const renameStation = (id: string, name: string) => put(`/kitchen/stations/${id}/name`, {name});
