import {api, del, post, put, type Page} from './api';
import type {Tone} from './format';
import type {OrderStatus, OrderSummary} from './types';

export type FulfillmentMode = 'Pickup' | 'Delivery';
export type FulfillmentStatus = 'Pending' | 'ReadyForPickup' | 'Assigned' | 'OutForDelivery' | 'Completed' | 'Failed' | 'Cancelled';
export type Step = 'ready' | 'picked-up' | 'depart' | 'delivered' | 'retry';
export type CourierKind = 'Staff' | 'Independent';
export type CollectionMethod = 'Cash' | 'MobileMoney';

export interface DeliveryZone {
  id: string;
  name: string;
  fee: number;
  minimumOrder: number;
  estimatedMinutes: number;
  isActive: boolean;
}

export interface DeliverySettings {
  locationId: string;
  currencyCode: string;
  pickupEnabled: boolean;
  pickupLeadMinutes: number;
  deliveryEnabled: boolean;
  deliveryLeadMinutes: number;
  zones: DeliveryZone[];
  isConfigured: boolean;
  updatedAtUtc: string | null;
}

export interface Fulfillment {
  id: string;
  orderId: string;
  locationId: string;
  mode: FulfillmentMode;
  status: FulfillmentStatus;
  customerName: string;
  customerPhone: string | null;
  promisedAtUtc: string;
  addressLine: string | null;
  landmark: string | null;
  instructions: string | null;
  zoneId: string | null;
  zoneName: string | null;
  deliveryFee: number | null;
  feeCurrencyCode: string | null;
  courierName: string | null;
  courierPhone: string | null;
  createdAtUtc: string;
  readyAtUtc: string | null;
  assignedAtUtc: string | null;
  departedAtUtc: string | null;
  completedAtUtc: string | null;
  failedAtUtc: string | null;
  failureReason: string | null;
  cancelledAtUtc: string | null;
  cancellationReason: string | null;
  courierId: string | null;
  courierFee: number | null;
  /** Reste à payer quand le livreur est parti, qu'il encaissera ; null : commande réglée. */
  amountToCollect: number | null;
  amountCollected: number | null;
  collectionMethod: CollectionMethod | null;
  remittanceId: string | null;
  remittedAtUtc: string | null;
}

export interface Courier {
  id: string;
  name: string;
  phone: string | null;
  kind: CourierKind;
  feePerDelivery: number | null;
  feeCurrencyCode: string | null;
  isActive: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
}

export interface CourierInput {
  name: string;
  phone: string | null;
  kind: CourierKind;
  feePerDelivery: number | null;
}

export interface PendingDelivery {
  fulfillmentId: string;
  orderId: string;
  customerName: string;
  zoneName: string | null;
  completedAtUtc: string | null;
  amountCollected: number | null;
  collectionMethod: CollectionMethod | null;
  cashHeld: number;
  courierFee: number;
}

/** Ce que le livreur doit (attendu positif) ou ce qu'on lui doit (négatif) sur ses courses pas encore remises. */
export interface CourierBalance {
  courierId: string | null;
  courierName: string;
  courierPhone: string | null;
  locationId: string;
  currencyCode: string;
  deliveries: number;
  cashHeld: number;
  mobileMoneyCollected: number;
  courierFees: number;
  expected: number;
  items: PendingDelivery[];
}

export interface CourierRemittance {
  id: string;
  locationId: string;
  courierId: string | null;
  courierName: string;
  currencyCode: string;
  deliveries: number;
  cashCollected: number;
  courierFees: number;
  expected: number;
  /** Positif : remis par le livreur ; négatif : payé au livreur. */
  counted: number;
  difference: number;
  note: string | null;
  recordedBy: string | null;
  recordedAtUtc: string;
}

export const courierKindLabel: Record<CourierKind, string> = {Staff: 'Salarié', Independent: 'Indépendant'};

export const collectionLabel: Record<CollectionMethod, string> = {Cash: 'Espèces', MobileMoney: 'Mobile money'};

export interface ZoneInput {
  name: string;
  fee: number;
  minimumOrder: number;
  estimatedMinutes: number;
}

export const modeLabel: Record<FulfillmentMode, string> = {Pickup: 'Retrait', Delivery: 'Livraison'};

export const statusLabel: Record<FulfillmentStatus, string> = {
  Pending: 'À préparer',
  ReadyForPickup: 'Prête à retirer',
  Assigned: 'Livreur assigné',
  OutForDelivery: 'En route',
  Completed: 'Terminée',
  Failed: 'Échec',
  Cancelled: 'Annulée',
};

export const statusTone: Record<FulfillmentStatus, Tone> = {
  Pending: 'amber',
  ReadyForPickup: 'green',
  Assigned: 'brand',
  OutForDelivery: 'blue',
  Completed: 'neutral',
  Failed: 'red',
  Cancelled: 'neutral',
};

export const isOpen = (fulfillment: Fulfillment) => fulfillment.status !== 'Completed' && fulfillment.status !== 'Cancelled';

export const isLate = (fulfillment: Fulfillment, now = Date.now()) =>
  isOpen(fulfillment) && fulfillment.status !== 'ReadyForPickup' && new Date(fulfillment.promisedAtUtc).getTime() < now;

const openOrderStatuses: OrderStatus[] = ['Placed', 'Accepted', 'Preparing', 'Ready'];

export function loadSettings(locationId: string, signal?: AbortSignal) {
  return api<DeliverySettings>(`/delivery/settings/${locationId}`, {signal});
}

/** Fiches ouvertes de l'établissement, de la promesse la plus proche à la plus lointaine. */
export async function openFulfillments(locationId: string, signal?: AbortSignal): Promise<Fulfillment[]> {
  const page = await api<Page<Fulfillment>>('/delivery/fulfillments', {query: {locationId, openOnly: true, pageSize: 100}, signal});
  return page.items;
}

/** Fiches créées depuis <paramref name="since"/> (toutes, y compris terminées et annulées). */
export async function fulfillmentsSince(locationId: string, since: Date, signal?: AbortSignal): Promise<Fulfillment[]> {
  const page = await api<Page<Fulfillment>>('/delivery/fulfillments', {query: {locationId, from: since.toISOString(), pageSize: 100}, signal});
  return page.items;
}

/** Commandes à emporter et en livraison encore ouvertes de l'établissement. */
export async function openOffsiteOrders(locationId: string, signal?: AbortSignal): Promise<OrderSummary[]> {
  const pages = await Promise.all(
    openOrderStatuses.map(status => api<Page<OrderSummary>>('/orders', {query: {locationId, status, pageSize: 100}, signal})),
  );
  return pages.flatMap(page => page.items).filter(order => order.type !== 'DineIn');
}

export function orderOf(id: string, signal?: AbortSignal) {
  return api<OrderSummary & {cancellationReason: string | null}>(`/orders/${id}`, {signal});
}

export async function fulfillmentOfOrder(orderId: string, signal?: AbortSignal): Promise<Fulfillment | null> {
  try {
    return await api<Fulfillment>(`/delivery/fulfillments/by-order/${orderId}`, {signal});
  } catch (error) {
    if ((error as {status?: number}).status === 404) return null;
    throw error;
  }
}

export const configurePickup = (locationId: string, enabled: boolean, leadMinutes: number) =>
  put(`/delivery/settings/${locationId}/pickup`, {enabled, leadMinutes});

export const configureDelivery = (locationId: string, enabled: boolean, leadMinutes: number) =>
  put(`/delivery/settings/${locationId}/delivery`, {enabled, leadMinutes});

export const addZone = (locationId: string, zone: ZoneInput) => post<{id: string}>(`/delivery/settings/${locationId}/zones`, zone);

export const updateZone = (locationId: string, zoneId: string, zone: ZoneInput) => put(`/delivery/settings/${locationId}/zones/${zoneId}`, zone);

export const setZoneActive = (locationId: string, zoneId: string, active: boolean) =>
  post(`/delivery/settings/${locationId}/zones/${zoneId}/${active ? 'activate' : 'deactivate'}`);

export const removeZone = (locationId: string, zoneId: string) => del(`/delivery/settings/${locationId}/zones/${zoneId}`);

export const createPickup = (body: {orderId: string; customerName: string; customerPhone: string | null; pickupAt: string | null}) =>
  post<{id: string}>('/delivery/fulfillments/pickup', body);

export const createDelivery = (body: {
  orderId: string;
  zoneId: string;
  customerName: string;
  customerPhone: string;
  addressLine: string;
  landmark: string | null;
  instructions: string | null;
}) => post<{id: string}>('/delivery/fulfillments/delivery', body);

export const step = (id: string, action: Step) => post(`/delivery/fulfillments/${id}/${action}`);

export const fail = (id: string, reason: string) => post(`/delivery/fulfillments/${id}/failed`, {reason});

export const cancelFulfillment = (id: string, reason: string | null) => post(`/delivery/fulfillments/${id}/cancel`, {reason});

export const assignCourier = (id: string, courier: {courierId: string} | {name: string; phone: string | null}) =>
  put(`/delivery/fulfillments/${id}/courier`, courier);

/** Livraison remise ; s'il restait à payer : le montant reçu par le livreur (tout le dû par défaut) et le mode. */
export const confirmDelivered = (id: string, collection?: {collected: number | null; method: CollectionMethod}) =>
  post(`/delivery/fulfillments/${id}/delivered`, collection ?? {});

export const listCouriers = (includeInactive = false, signal?: AbortSignal) =>
  api<Courier[]>('/delivery/couriers', {query: {includeInactive}, signal});

export const createCourier = (courier: CourierInput) => post<{id: string}>('/delivery/couriers', courier);

export const updateCourier = (id: string, courier: CourierInput) => put(`/delivery/couriers/${id}`, courier);

export const setCourierActive = (id: string, active: boolean) => post(`/delivery/couriers/${id}/${active ? 'activate' : 'deactivate'}`);

export const courierBalances = (locationId: string, signal?: AbortSignal) =>
  api<CourierBalance[]>('/delivery/couriers/balances', {query: {locationId}, signal});

/** <paramref name="counted"/> : positif, remis par le livreur ; négatif, payé au livreur. */
export const recordRemittance = (fulfillmentIds: string[], counted: number, note: string | null) =>
  post<{id: string}>('/delivery/remittances', {fulfillmentIds, counted, note});

export async function remittancesSince(locationId: string, since: Date, signal?: AbortSignal): Promise<CourierRemittance[]> {
  const page = await api<Page<CourierRemittance>>('/delivery/remittances', {query: {locationId, from: since.toISOString(), pageSize: 100}, signal});
  return page.items;
}

/** Numéro E.164 sans « + » pour wa.me. */
const waNumber = (phone: string) => phone.replace(/[^0-9]/g, '');

/**
 * Lien WhatsApp vers le livreur, avec la course : commande, client, adresse, repère, consignes et, une fois parti, le
 * montant à encaisser (<c>amountLabel</c> null : rien à encaisser ; absent : pas encore connu).
 * Le message part du téléphone de la personne qui clique : rien n'est envoyé par la plateforme.
 */
export function courierWhatsAppLink(fulfillment: Fulfillment, details: {orderNumber?: string; restaurant?: string; amountLabel?: string | null}): string | null {
  if (!fulfillment.courierPhone) return null;
  const lines = [
    `${details.restaurant ? `${details.restaurant} : ` : ''}course${details.orderNumber ? ` commande #${details.orderNumber}` : ''}`,
    `Client : ${fulfillment.customerName}${fulfillment.customerPhone ? ` (${fulfillment.customerPhone})` : ''}`,
    fulfillment.addressLine ? `Adresse : ${fulfillment.addressLine}` : null,
    fulfillment.landmark ? `Repère : ${fulfillment.landmark}` : null,
    fulfillment.instructions ? `Consignes : ${fulfillment.instructions}` : null,
    details.amountLabel === undefined ? null : details.amountLabel ? `À encaisser : ${details.amountLabel}` : 'Commande déjà réglée : rien à encaisser.',
  ].filter(Boolean);
  return `https://wa.me/${waNumber(fulfillment.courierPhone)}?text=${encodeURIComponent(lines.join('\n'))}`;
}

export const updateContact = (id: string, customerName: string, customerPhone: string | null) =>
  put(`/delivery/fulfillments/${id}/contact`, {customerName, customerPhone});

export const changeAddress = (id: string, addressLine: string, landmark: string | null, instructions: string | null) =>
  put(`/delivery/fulfillments/${id}/address`, {addressLine, landmark, instructions});

/** Étape suivante proposée sur la carte, selon le mode et le statut. */
export function nextAction(fulfillment: Fulfillment): {action: Step; label: string; success: string} | null {
  if (fulfillment.mode === 'Pickup') {
    if (fulfillment.status === 'Pending') return {action: 'ready', label: 'Prête à retirer', success: 'Commande prête à retirer.'};
    if (fulfillment.status === 'ReadyForPickup') return {action: 'picked-up', label: 'Remise au client', success: 'Commande remise au client.'};
    return null;
  }
  if (fulfillment.status === 'Assigned') return {action: 'depart', label: 'Départ du livreur', success: 'Livreur parti.'};
  if (fulfillment.status === 'OutForDelivery') return {action: 'delivered', label: 'Livrée', success: 'Livraison confirmée.'};
  if (fulfillment.status === 'Failed') return {action: 'retry', label: 'Relancer', success: 'Livraison relancée : assignez un livreur.'};
  return null;
}
