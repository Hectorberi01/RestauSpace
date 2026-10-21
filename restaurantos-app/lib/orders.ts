import {api, post} from './api';
import type {DiningArea, OrderStatus, OrderType, TableSession} from './types';

export interface Transition {
  path: string;
  label: string;
  permission: string;
  success: string;
}

/** L'étape suivante d'une commande, dans l'ordre du cycle de vie de l'API. */
export const nextStep: Partial<Record<OrderStatus, Transition>> = {
  Draft: {path: 'place', label: 'Envoyer la commande', permission: 'orders.take', success: 'Commande envoyée.'},
  Placed: {path: 'accept', label: 'Accepter', permission: 'orders.process', success: 'Commande acceptée : le ticket part en cuisine.'},
  Accepted: {path: 'start-preparation', label: 'Lancer la préparation', permission: 'orders.process', success: 'Préparation lancée.'},
  Preparing: {path: 'ready', label: 'Marquer prête', permission: 'orders.process', success: 'Commande prête.'},
  Ready: {path: 'complete', label: 'Servie / remise', permission: 'orders.process', success: 'Commande terminée.'},
};

export const cancellable: OrderStatus[] = ['Draft', 'Placed', 'Accepted', 'Preparing', 'Ready'];

export function advance(orderId: string, status: OrderStatus) {
  const step = nextStep[status];
  if (!step) throw new Error('Aucune étape suivante.');
  return post(`/orders/${orderId}/${step.path}`);
}

export function cancel(orderId: string, reason: string) {
  return post(`/orders/${orderId}/cancel`, {reason});
}

export async function createOrder(
  locationId: string, type: OrderType, tableSessionId?: string,
  remote?: {channel: 'Phone' | 'WhatsApp' | 'DeliveryPlatform'; customerId: string | null; platform?: string; externalReference?: string},
): Promise<string> {
  const created = await post<{id: string}>('/orders', {
    locationId, type, tableSessionId: tableSessionId ?? null, customerId: remote?.customerId ?? null, channel: remote?.channel ?? null,
    platform: remote?.platform ?? null, externalReference: remote?.externalReference?.trim() || null,
  });
  return created.id;
}

/** Plateformes de livraison proposées ; un autre code reste possible (« autre »). */
export const deliveryPlatforms: {code: string; name: string}[] = [
  {code: 'glovo', name: 'Glovo'},
  {code: 'gozem', name: 'Gozem'},
  {code: 'yango', name: 'Yango Delivery'},
  {code: 'jumia_food', name: 'Jumia Food'},
  {code: 'other', name: 'Autre plateforme'},
];

export const platformName = (code?: string | null) => (code ? deliveryPlatforms.find(item => item.code === code)?.name ?? code : '');

export interface TableInfo {
  label: string;
  area: string;
}

/** Libellé de table de chaque session ouverte de l'établissement : « Terrasse · T4 ». */
export async function tablesBySession(locationId: string, signal?: AbortSignal): Promise<Map<string, TableInfo>> {
  const [areas, sessions] = await Promise.all([
    api<DiningArea[]>('/floor/dining-areas', {query: {locationId}, signal}),
    api<TableSession[]>('/floor/table-sessions', {query: {locationId}, signal}),
  ]);
  const tables = new Map(areas.flatMap(area => area.tables.map(table => [table.id, {label: table.label, area: area.name}] as const)));
  const result = new Map<string, TableInfo>();
  sessions.forEach(session => {
    const table = tables.get(session.tableId);
    if (table) result.set(session.id, table);
  });
  return result;
}
